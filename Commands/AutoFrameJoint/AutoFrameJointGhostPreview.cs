using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.AutoFrameJoint
{
    /// <summary>
    /// Temporary original-body overlay and verified profile-rule preview.
    /// All preview cut operations are on disposable B-rep copies ONLY;
    /// this Preview class only prepares the reviewed ticket for the native writer.
    /// Runs on the SOLIDWORKS UI thread from explicit PMP buttons.
    /// The native A/B selector must pass its host smoke test first.
    /// </summary>
    public sealed class AutoFrameJointGhostPreview
    {
        private const int MaxBodiesPerComponent = 12;

        private readonly ISldWorks app;
        private readonly ModelDoc2 sourceAssembly;
        private readonly List<ShownBody> shown = new List<ShownBody>();
        private bool busy, closeRequested;
        private AfjJointSolution cachedSolution;
        private PreviewSourceState cachedSource;
        private int displayedCandidate;
        private string displayedMessage;
        // AFJ-PREVIEW-OUTCOME-20261022
        // One exact-source non-actionable result. COM/Boolean failures are never cached.
        private const int RejectedPreviewCacheMilliseconds = 30000;
        private PreviewSourceState rejectedSource;
        private string rejectedPreviewMessage;
        private long rejectedPreviewTimestamp;
        private static readonly bool verboseViewportDiagnostics =
            String.Equals(System.Environment.GetEnvironmentVariable("AFJ_TRACE_VIEWPORT"), "1", StringComparison.Ordinal);
        internal bool IsBusy { get { return busy; } }

        private sealed class ShownBody
        {
            internal readonly Body2 Copy;
            internal readonly ModelDoc2 Part;
            internal readonly bool IsRemoved;

            internal ShownBody(Body2 copy, ModelDoc2 part, bool isRemoved = false)
            {
                Copy = copy;
                Part = part;
                IsRemoved = isRemoved;
            }
        }

        public AutoFrameJointGhostPreview(ISldWorks swApp,
                                          ModelDoc2 originalAssembly)
        {
            if (swApp == null) throw new ArgumentNullException("swApp");
            if (originalAssembly == null)
                throw new ArgumentNullException("originalAssembly");

            app = swApp;
            sourceAssembly = originalAssembly;
            Trace.WriteLine("[AFJ-R53] SOURCE=UNIFIED_U_ADJUSTED_BISECTOR_FACE_SKETCH plane_fallback=DISABLED MVID=" +
                typeof(AutoFrameJointGhostPreview).Assembly.ManifestModule.ModuleVersionId);
        }

        // Includes bodies with a pending cleanup after a COM display error.
        // Do not treat this property as proof all bodies are rendered.
        public bool IsShowing { get { return shown.Count != 0; } }

        public bool TryShow(Component2 frameA, Component2 frameB,
                            out string message)
        {
            message = "Preview chưa được tạo.";
            if (busy || closeRequested)
            {
                message = "Preview đang xử lý hoặc đang đóng.";
                return false;
            }

            busy = true;
            try
            {
                string clearError;
                if (!ClearInternal(out clearError))
                {
                    message = "Chưa dọn được Preview cũ: " + clearError;
                    return false;
                }

                if (!IsOriginalAssemblyActive())
                {
                    message = "Assembly đã thay đổi. Hãy mở lại lệnh.";
                    return false;
                }

                if (frameA == null || frameB == null ||
                    app.IsSame(frameA, frameB) !=
                        (int)swObjectEquality.swObjectNotSame)
                {
                    message = "Cần hai Component khác nhau và đã xác minh.";
                    return false;
                }

                // IBody2.Display3 does not support components in subassemblies.
                // Nested parts will be added only after an explicit host test.
                if (frameA.GetParent() != null || frameB.GetParent() != null)
                {
                    message = "Preview thử nghiệm chỉ nhận Part cấp đầu Assembly.";
                    return false;
                }

                // Check visible occurrence state in the active Assembly without
                // changing suppression, visibility or resolving lightweight parts.
                // IsHidden(false) checks display visibility without treating
                // every Lightweight occurrence as hidden.
                if (frameA.IsSuppressed() || frameB.IsSuppressed())
                {
                    message = "Frame A hoặc B đang Suppressed. Hãy Unsuppress trước khi xem.";
                    return false;
                }

                if (frameA.IsHidden(false) || frameB.IsHidden(false))
                {
                    message = "Frame A hoặc B đang Hidden. Hãy Show Component trước khi xem.";
                    return false;
                }

                // COLORREF is 0x00BBGGRR, not System.Drawing.Color.ToArgb().
                int colorA = ColorTranslator.ToWin32(Color.FromArgb(0, 180, 235));
                int colorB = ColorTranslator.ToWin32(Color.FromArgb(255, 155, 60));

                int countA = ShowComponent(frameA, colorA, 0.65);
                ThrowIfClosing();
                int countB = ShowComponent(frameB, colorB, 0.65);
                ThrowIfClosing();
                RedrawSafely(); // One redraw after the complete pair, not per body.
                message = String.Format(
                    "Đang xem bản sao trạng thái hiện tại: {0} body A + {1} body B. CHƯA MÔ PHỎNG VẾT CẮT MỚI.",
                    countA, countB);
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[AFJ-GHOST] TryShow failed: " + ex);
                string cleanError;
                bool cleaned = ClearInternal(out cleanError);
                message = cleaned
                    ? "Không tạo được Preview: " + ex.Message
                    : "Lỗi Preview (" + ex.Message + "); còn " + shown.Count +
                      " body chưa dọn được bằng Hide(): " + cleanError;
                return false;
            }
            finally
            {
                busy = false;
            }
        }

        /// <summary>
        /// Generate independent profile/rule cutters for both selected Frames.
        /// Mode 1 shows KEEP; mode 2 additionally shows CUT material.
        /// All Boolean operations use temporary copies and validated skins.
        /// Original Parts/Assembly and the native A/B selector are unchanged.
        /// </summary>
        public bool TryShowCutCandidate(Component2 frameA, Component2 frameB,
                                        int candidate, out string message)
        {
            Trace.WriteLine("[AFJ-R52] stage=GHOST_ENTER candidate=" + candidate + " busy=" + busy);
            message = "Chưa tạo preview vết cắt.";
            if (busy || closeRequested) { message = "Preview đang xử lý hoặc đang đóng."; return false; }
            if (candidate != 1 && candidate != 2)
            { message = "Chỉ có chế độ KEEP hoặc KEEP/CUT."; return false; }
            busy = true;
            var total = Stopwatch.StartNew();
            bool reused = false;
            PreviewSourceState attemptedSource = null;
            bool solverAttempted = false;
            try
            {
                if (!IsOriginalAssemblyActive() || frameA == null || frameB == null ||
                    app.IsSame(frameA, frameB) != (int)swObjectEquality.swObjectNotSame)
                { return RejectInvalidPreview("Assembly/Frame A-B không còn hợp lệ.", out message); }
                if (frameA.GetParent() != null || frameB.GetParent() != null ||
                    frameA.IsSuppressed() || frameB.IsSuppressed() ||
                    frameA.IsHidden(false) || frameB.IsHidden(false))
                { return RejectInvalidPreview("Chỉ hỗ trợ 2 Part cấp đầu, không Hidden/Suppressed.", out message); }

                var phase = Stopwatch.StartNew();
                PreviewSourceState before = TryCaptureSourceState(frameA, frameB);
                attemptedSource = before;
                reused = cachedSolution != null && before != null && SameSourceState(cachedSource, before);
                Trace.WriteLine("[AFJ-R52-PERF] phase=source_state ms=" + phase.ElapsedMilliseconds + " cache=" + (reused ? "HIT" : "MISS"));
                ThrowIfClosing();
                if (!IsOriginalAssemblyActive())
                { return RejectInvalidPreview("Assembly đã thay đổi; không cập nhật Preview.", out message); }
                if (reused && displayedCandidate == candidate)
                {
                    message = displayedMessage;
                    Trace.WriteLine("[AFJ-R52-PERF] phase=display mode=UNCHANGED no_solve=True no_hide=True no_display3=True");
                    return true;
                }

                string rejected;
                if (!reused && TryGetRejectedPreview(before, true, out rejected))
                {
                    // The failed state has no qualified plan. Remove any old
                    // overlay without discarding the exact-source rejection.
                    string cleanup;
                    bool cleared = ClearInternal(out cleanup);
                    message = rejected + (cleared ? "" : " | Còn Preview cần dọn: " + cleanup);
                    Trace.WriteLine("[AFJ-R52-PERF] phase=solve mode=REJECTED_CACHE no_solve=True");
                    return false;
                }
                AfjJointSolution result = reused ? cachedSolution : null;
                PreviewSourceState after = before;
                if (!reused)
                {
                    InvalidateCachedSolution("source changed or unverified");
                    Trace.WriteLine("[AFJ-R51] source=profile-skin-cutter; MVID=" + typeof(AutoFrameJointGhostPreview).Assembly.ManifestModule.ModuleVersionId);
                    Trace.WriteLine("[AFJ-SOLVER] PROFILE -> RULE -> PLAN A/B -> BOOLEAN -> VALIDATION");
                    Trace.WriteLine("[AFJ-R52] stage=SOLVER_BEGIN");
                    ClearRejectedPreview("new solver attempt");
                    phase.Restart();
                    solverAttempted = true;
                    bool solveCompleted = false;
                    try
                    {
                        result = AfjJointSolver.Solve(app, sourceAssembly, frameA, frameB);
                        solveCompleted = true;
                    }
                    finally
                    {
                        Trace.WriteLine("[AFJ-R52-PERF] phase=solve ms=" + phase.ElapsedMilliseconds + " completed=" + solveCompleted);
                    }
                    Trace.WriteLine("[AFJ-R52] stage=SOLVER_END keptA=" + result.A.Kept.Count + " keptB=" + result.B.Kept.Count + " removedA_m3=" + result.A.RemovedVolume.ToString("R", CultureInfo.InvariantCulture) + " removedB_m3=" + result.B.RemovedVolume.ToString("R", CultureInfo.InvariantCulture));
                    ThrowIfClosing();
                    after = TryCaptureSourceState(frameA, frameB);
                    if (before != null && after != null && !SameSourceState(before, after))
                        throw new InvalidOperationException("SOURCE_CHANGED: Frame thay đổi khi tính Preview; hãy chọn lại.");
                    if (!IsOriginalAssemblyActive())
                        throw new InvalidOperationException("Assembly thay đổi khi tính Preview.");
                    if (result.A.RemovedVolume + result.B.RemovedVolume <= 1e-12)
                    {
                        string unusedCleanup;
                        bool cleaned = ClearInternal(out unusedCleanup);
                        message = "NO_NEW_CUT_REQUIRED: Hai Frame đã khớp quy tắc; không dựng lại Body gốc thành Preview cắt mới." +
                            (cleaned ? "" : " Còn Preview cần dọn: " + unusedCleanup);
                        ClearRejectedPreview("no new cut required");
                        if (cleaned && before != null && after != null && SameSourceState(before, after))
                            RememberRejectedPreview(after, message);
                        return false;
                    }
                    string previousError;
                    phase.Restart();
                    // Keep the old display until a new result has passed all validators.
                    // Replacement redraws only once, after all new bodies are displayed.
                    if (!ClearInternal(out previousError, false))
                    { RedrawSafely(); message = "Không thể dọn preview cũ: " + previousError; return false; }
                    Trace.WriteLine("[AFJ-R52-PERF] phase=replace_hide ms=" + phase.ElapsedMilliseconds);
                }

                ThrowIfClosing();
                phase.Restart();
                MathTransform shift = CreateWorldShift(0.12);
                MathTransform inverseA = result.TransformA.Inverse() as MathTransform;
                MathTransform inverseB = result.TransformB.Inverse() as MathTransform;
                if (inverseA == null || inverseB == null)
                    throw new InvalidOperationException("Không đảo được hệ tọa độ Part.");
                if (!reused)
                {
                    DisplayCandidateBodies(AfjJointSolver.World(result.A.Kept, result.TransformA),
                        frameA, result.PartA, inverseA, shift,
                        ColorTranslator.ToWin32(Color.FromArgb(0, 180, 235)), 0.33, "A_KEEP");
                    DisplayCandidateBodies(AfjJointSolver.World(result.B.Kept, result.TransformB),
                        frameB, result.PartB, inverseB, shift,
                        ColorTranslator.ToWin32(Color.FromArgb(255, 165, 65)), 0.33, "B_KEEP");
                }
                if (candidate == 2)
                {
                    DisplayCandidateBodies(AfjJointSolver.World(result.A.Removed, result.TransformA),
                        frameA, result.PartA, inverseA, shift,
                        ColorTranslator.ToWin32(Color.FromArgb(235, 35, 85)), 0.08, "A_CUT");
                    DisplayCandidateBodies(AfjJointSolver.World(result.B.Removed, result.TransformB),
                        frameB, result.PartB, inverseB, shift,
                        ColorTranslator.ToWin32(Color.FromArgb(170, 35, 235)), 0.08, "B_CUT");
                }
                else if (reused)
                {
                    string hideError;
                    if (!HideDisplayedBodies(true, false, out hideError))
                        throw new InvalidOperationException("Không ẩn được vùng CUT: " + hideError);
                }
                ThrowIfClosing();
                RedrawSafely();
                Trace.WriteLine("[AFJ-R52-PERF] phase=display ms=" + phase.ElapsedMilliseconds + " reuse_keep=" + reused);
                message = String.Format(CultureInfo.InvariantCulture,
                    "{0}: Đã kiểm tra B-rep tạm. A bỏ {1:0.###} mm³; B bỏ {2:0.###} mm³. " +
                    "Frame ngắn {3} che; khe hở {4:0.###} mm. Bản sao nâng Z +120 mm. {5}", result.Plan.RuleId,
                    result.A.RemovedVolume * 1e9, result.B.RemovedVolume * 1e9,
                    result.CoverA ? "A" : "B", result.AfterGap * 1000,
                    candidate == 2 ? "Hồng/tím: CUT; xanh/cam: KEEP." : "Đang xem KEEP.");
                if (before != null && after != null)
                {
                    cachedSolution = result;
                    cachedSource = after;
                    displayedCandidate = candidate;
                    displayedMessage = String.IsNullOrEmpty(result.CoverageWarning) ? message : message + " " + result.CoverageWarning;
                }
                if (!String.IsNullOrEmpty(result.CoverageWarning)) message += " " + result.CoverageWarning;
                Trace.WriteLine("[AFJ-SOLVER] " + message);
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[AFJ-SOLVER] HRESULT=0x" + ex.HResult.ToString("X8") + " " + ex);
                string cleanupError;
                bool cleared = ClearInternal(out cleanupError);
                string reason = ex is OperationCanceledException
                    ? "Đã kết thúc Preview; không thay đổi Part."
                    : ex is AfjBooleanException
                        ? "Hình học cắt chưa tạo được solid hợp lệ. Xem log AFJ-BOOLEAN."
                        : ex.Message.StartsWith("UNVERIFIED_RULE", StringComparison.Ordinal)
                            ? ex.Message
                            : "Không tạo được Preview hình học. Xem log AFJ-SOLVER.";
                ClearRejectedPreview("preview failed");
                // Cache only a deterministic rule refusal for the same source
                // on both sides of this attempted solve. Never cache native
                // exceptions, failed cleanup or an inconclusive API identity.
                if (solverAttempted && cleared && !closeRequested &&
                    ex is InvalidOperationException && !(ex is AfjBooleanException) &&
                    ex.Message.StartsWith("UNVERIFIED_RULE", StringComparison.Ordinal) &&
                    attemptedSource != null && IsOriginalAssemblyActive())
                {
                    PreviewSourceState failedSource = TryCaptureSourceState(frameA, frameB);
                    if (failedSource != null && SameSourceState(attemptedSource, failedSource))
                        RememberRejectedPreview(failedSource, reason);
                }
                message = reason + (cleared ? "" : " | Cần dọn Preview cũ; xem log cleanup.");
                return false;
            }
            finally
            {
                busy = false;
                Trace.WriteLine("[AFJ-R52-PERF] phase=preview_total ms=" + total.ElapsedMilliseconds + " reused=" + reused + " closing=" + closeRequested);
            }
        }

        private sealed class PreviewComponentState
        {
            internal Component2 Component;
            internal ModelDoc2 Part;
            internal Body2 Body;
            internal int PartStamp, BodyStamp;
            internal string Configuration;
            internal double[] Transform;
        }
        private sealed class PreviewSourceState
        {
            internal int AssemblyStamp;
            internal string AssemblyConfiguration;
            internal PreviewComponentState A, B;
        }
        private PreviewComponentState CaptureComponentState(Component2 component)
        {
            var state = new PreviewComponentState { Component = component };
            state.Part = component.GetModelDoc2() as ModelDoc2;
            if (state.Part == null) throw new InvalidOperationException("unresolved Part");
            state.PartStamp = state.Part.GetUpdateStamp();
            state.Configuration = component.ReferencedConfiguration;
            var active = state.Part.ConfigurationManager.ActiveConfiguration;
            if (String.IsNullOrEmpty(state.Configuration) || active == null || active.Name != state.Configuration)
                throw new InvalidOperationException("configuration mismatch");
            MathTransform transform = component.GetTotalTransform(false);
            double[] matrix = transform == null ? null : transform.ArrayData as double[];
            if (matrix == null || matrix.Length != 16 || matrix.Any(x => Double.IsNaN(x) || Double.IsInfinity(x)))
                throw new InvalidOperationException("transform unavailable");
            state.Transform = (double[])matrix.Clone();
            object info;
            Array bodies = component.GetBodies3((int)swBodyType_e.swSolidBody, out info) as Array;
            if (bodies == null || bodies.Length != 1 || (state.Body = bodies.GetValue(0) as Body2) == null)
                throw new InvalidOperationException("single source body unavailable");
            state.BodyStamp = state.Body.GetUpdateStamp();
            if (state.PartStamp != state.Part.GetUpdateStamp()) throw new InvalidOperationException("source changed while reading");
            return state;
        }
        private PreviewSourceState TryCaptureSourceState(Component2 a, Component2 b)
        {
            try
            {
                var state = new PreviewSourceState { AssemblyStamp = sourceAssembly.GetUpdateStamp() };
                var cfg = sourceAssembly.ConfigurationManager.ActiveConfiguration;
                if (cfg == null) throw new InvalidOperationException("assembly configuration unavailable");
                state.AssemblyConfiguration = cfg.Name;
                state.A = CaptureComponentState(a);
                state.B = CaptureComponentState(b);
                if (state.AssemblyStamp != sourceAssembly.GetUpdateStamp()) throw new InvalidOperationException("assembly changed while reading");
                return state;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[AFJ-R52-PERF] cache=DISABLED HRESULT=0x" + ex.HResult.ToString("X8") + " " + ex.Message);
                return null; // Full solver validation remains mandatory; no guessed identity.
            }
        }
        private bool SameComponentState(PreviewComponentState a, PreviewComponentState b)
        {
            return a != null && b != null && a.PartStamp == b.PartStamp && a.BodyStamp == b.BodyStamp &&
                a.Configuration == b.Configuration && a.Transform.SequenceEqual(b.Transform) &&
                app.IsSame(a.Component, b.Component) == (int)swObjectEquality.swObjectSame &&
                app.IsSame(a.Part, b.Part) == (int)swObjectEquality.swObjectSame &&
                app.IsSame(a.Body, b.Body) == (int)swObjectEquality.swObjectSame;
        }
        private bool SameSourceState(PreviewSourceState a, PreviewSourceState b)
        {
            try
            {
                return a != null && b != null && a.AssemblyStamp == b.AssemblyStamp &&
                    a.AssemblyConfiguration == b.AssemblyConfiguration && SameComponentState(a.A, b.A) && SameComponentState(a.B, b.B);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[AFJ-R52-PERF] cache=INVALID identity unavailable " + ex.Message);
                return false;
            }
        }
        private void InvalidateCachedSolution(string reason)
        {
            if (cachedSolution != null) Trace.WriteLine("[AFJ-R52-PERF] cache=INVALID reason=" + reason);
            cachedSolution = null; cachedSource = null; displayedCandidate = 0; displayedMessage = null;
        }
        internal bool TryPrepareApply(Component2 frameA, Component2 frameB,
                                      out AfjApplyTicket ticket, out string message)
        {
            ticket = null;
            if (busy || closeRequested)
            { message = "PREVIEW_BUSY: Preview đang xử lý hoặc đã đóng."; return false; }
            if (frameA == null || frameB == null)
            { message = "PREVIEW_SELECTION_INVALID: Cần chọn đủ Frame A và B."; return false; }
            if (!IsOriginalAssemblyActive())
            { message = "PREVIEW_ASSEMBLY_INACTIVE: Assembly nguồn không còn hoạt động."; return false; }
            if (frameA.GetParent()!=null || frameB.GetParent()!=null || frameA.IsSuppressed() || frameB.IsSuppressed() ||
                frameA.IsHidden(false) || frameB.IsHidden(false))
            { message="APPLY: cần hai Part cấp đầu đang hiển thị và resolved."; return false; }
            PreviewSourceState current = TryCaptureSourceState(frameA, frameB);
            if (current == null)
            { message = "PREVIEW_SOURCE_UNVERIFIED: Chưa xác minh được trạng thái Frame hiện tại."; return false; }
            if (cachedSolution == null || cachedSource == null)
            {
                string rejected;
                message = TryGetRejectedPreview(current, false, out rejected)
                    ? rejected + " Không có Cut Plan hợp lệ để áp dụng."
                    : "PREVIEW_REQUIRED: hãy tạo Preview trước khi OK.";
                return false;
            }
            PreviewSourceState state = cachedSource;
            AfjJointSolution result = cachedSolution;
            if (!SameSourceState(state, current))
            { message = "PREVIEW_STALE: Frame đã thay đổi; hãy tạo Preview lại."; return false; }
            ModelDoc2 pa, pb;
            MathTransform ta, tb;
            Body2 a = AfjJointSolver.NativeBody(frameA, out pa, out ta);
            Body2 b = AfjJointSolver.NativeBody(frameB, out pb, out tb);
            ticket = new AfjApplyTicket { FrameA=frameA, FrameB=frameB, Preview=result,
                BeforeA=a, BeforeB=b,
                IsSourceCurrent=() => !closeRequested && IsOriginalAssemblyActive() &&
                    SameSourceState(state, TryCaptureSourceState(frameA, frameB)) };
            message = "Validated Preview ticket prepared; no Part mutation.";
            return true;
        }

        private void ClearRejectedPreview(string reason)
        {
            if (rejectedSource != null)
                Trace.WriteLine("[AFJ-R52-PERF] rejected_cache=INVALID reason=" + reason);
            rejectedSource = null;
            rejectedPreviewMessage = null;
            rejectedPreviewTimestamp = 0;
        }

        private void RememberRejectedPreview(PreviewSourceState state, string message)
        {
            if (state == null || String.IsNullOrEmpty(message)) return;
            rejectedSource = state;
            rejectedPreviewMessage = message;
            rejectedPreviewTimestamp = Stopwatch.GetTimestamp();
            Trace.WriteLine("[AFJ-R52-PERF] rejected_cache=STORED lifetime_ms=" + RejectedPreviewCacheMilliseconds);
        }

        private bool TryGetRejectedPreview(PreviewSourceState current, bool boundedSolveCache, out string message)
        {
            message = null;
            if (rejectedSource == null || String.IsNullOrEmpty(rejectedPreviewMessage)) return false;
            if (current == null || !SameSourceState(rejectedSource, current))
            { ClearRejectedPreview("source changed or identity unverified"); return false; }
            double age = (Stopwatch.GetTimestamp() - rejectedPreviewTimestamp) * 1000.0 / Stopwatch.Frequency;
            if (boundedSolveCache && (age < 0 || age > RejectedPreviewCacheMilliseconds)) return false;
            message = rejectedPreviewMessage;
            Trace.WriteLine("[AFJ-R52-PERF] rejected_cache=HIT age_ms=" + age.ToString("0.###", CultureInfo.InvariantCulture) + " bounded=" + boundedSolveCache);
            return true;
        }

        private bool RejectInvalidPreview(string reason, out string message)
        {
            ClearRejectedPreview("invalid Preview request");
            InvalidateCachedSolution("invalid Preview request");
            if (!IsOriginalAssemblyActive())
            {
                // Never operate Hide against another active document. Keep
                // strong ownership so session cleanup can retry safely later.
                message = reason + (shown.Count == 0 ? "" : " Preview cũ chờ dọn tại Assembly nguồn.");
                return false;
            }
            string cleanup;
            bool cleared = ClearInternal(out cleanup);
            message = reason + (cleared ? "" : " Còn Preview cần dọn: " + cleanup);
            return false;
        }

        internal void RequestClose()
        {
            closeRequested = true; // Managed latch, including reentrant close during a native operation.
            ClearRejectedPreview("page closed");
            InvalidateCachedSolution("page closed");
        }
        private void ThrowIfClosing()
        {
            if (closeRequested) throw new OperationCanceledException("AFJ page has closed");
        }

        private static Body2 CopyBody(Body2 original)
        {
            Body2 copy = original == null ? null : original.Copy() as Body2;
            if (copy == null || !copy.IsTemporaryBody())
                throw new InvalidOperationException("Không Copy được B-rep tạm.");
            return copy;
        }











        private MathTransform CreateWorldShift(double z)
        {
            MathUtility math = app.GetMathUtility() as MathUtility;
            if (math == null)
                throw new InvalidOperationException("Không lấy được MathUtility.");
            double[] matrix = new double[16] {
                1,0,0, 0,1,0, 0,0,1,
                0,0,z, 1, 0,0,0
            };
            MathTransform result = math.CreateTransform(matrix) as MathTransform;
            if (result == null)
                throw new InvalidOperationException("Không tạo được ghost offset.");
            return result;
        }



        private static string DiagnosticArray(object value)
        {
            Array values = value as Array;
            return values == null ? "NOT_AVAILABLE" : String.Join(";", values.Cast<object>().Select(x => Convert.ToDouble(x).ToString("R", CultureInfo.InvariantCulture)));
        }
        private void TraceProjectedPreview(Body2 shiftedWorld, string group)
        {
            if (!verboseViewportDiagnostics) return;
            // Diagnostic only. Bounds do not define a cutter or prove pixels
            // are visible; occlusion, clipping and transparency are not tested.
            try
            {
                double[] box = shiftedWorld.GetBodyBox() as double[];
                ModelView view = sourceAssembly.ActiveView as ModelView;
                if (box == null || box.Length < 6 || view == null)
                { Trace.WriteLine("[AFJ-R52] stage=VIEWPORT group=" + group + " status=UNVERIFIED no view/bounds"); return; }
                MathTransform projection = view.Transform;
                Array visible = view.GetVisibleBox() as Array;
                Trace.WriteLine("[AFJ-R52] stage=VIEW_STATE group=" + group + " world_box_m=" + DiagnosticArray(box) +
                    " visible_box_px=" + DiagnosticArray(visible) + " transform=" + DiagnosticArray(projection == null ? null : projection.ArrayData) +
                    " scale=" + view.Scale2.ToString("R", CultureInfo.InvariantCulture) + " graphics_update=" + view.EnableGraphicsUpdate);
                if (projection == null || visible == null || visible.Length != 4 || view.HasPerspective())
                { Trace.WriteLine("[AFJ-R52] stage=VIEWPORT group=" + group + " status=UNVERIFIED projection/perspective"); return; }
                MathUtility math = app.GetMathUtility() as MathUtility;
                var points = new List<double[]>();
                for (int mask = 0; mask < 8; mask++)
                {
                    double[] p = { box[(mask & 1) == 0 ? 0 : 3], box[(mask & 2) == 0 ? 1 : 4], box[(mask & 4) == 0 ? 2 : 5] };
                    MathPoint point = math.CreatePoint(p) as MathPoint;
                    MathPoint projected = point.MultiplyTransform(projection) as MathPoint;
                    points.Add((double[])projected.ArrayData);
                }
                double[] rectangle = { points.Min(p => p[0]), points.Min(p => p[1]), points.Max(p => p[0]), points.Max(p => p[1]) };
                double[] window = visible.Cast<object>().Select(Convert.ToDouble).ToArray();
                Trace.WriteLine("[AFJ-R52] stage=VIEWPORT group=" + group + " projection_estimate=" + ViewportEstimate(rectangle, window) +
                    " projected_box_px=" + DiagnosticArray(rectangle) + " actual_visibility=UNVERIFIED");
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[AFJ-R52] stage=VIEWPORT group=" + group + " status=UNVERIFIED HRESULT=0x" + ex.HResult.ToString("X8") + " " + ex.Message);
            }
        }
        private static string ViewportEstimate(double[] rectangle, double[] window)
        {
            if (rectangle == null || window == null || rectangle.Length != 4 || window.Length != 4 ||
                rectangle.Concat(window).Any(x => Double.IsNaN(x) || Double.IsInfinity(x)) ||
                rectangle[0] > rectangle[2] || rectangle[1] > rectangle[3] || window[0] >= window[2] || window[1] >= window[3]) return "UNVERIFIED";
            return rectangle[2] < window[0] || rectangle[0] > window[2] || rectangle[3] < window[1] || rectangle[1] > window[3]
                ? "OUTSIDE_WINDOW_ESTIMATE" : "OVERLAPS_WINDOW_ESTIMATE";
        }

        private void DisplayCandidateBodies(List<Body2> bodies,
                       Component2 component, ModelDoc2 part,
                       MathTransform inverse, MathTransform shift,
                       int color, double transparency, string group)
        {
            foreach (Body2 world in bodies)
            {
                ThrowIfClosing();
                // Display3(component) places local solid in Assembly.
                // world+shift --> inverse component --> Display3(component).
                Body2 local = CopyBody(world);
                if (!local.ApplyTransform(shift))
                    throw new InvalidOperationException("Không đặt được vị trí Preview.");
                TraceProjectedPreview(local, group);
                if (!local.ApplyTransform(inverse))
                    throw new InvalidOperationException(
                        "Không chuyển ghost từ World XYZ về Part XYZ.");
                try
                {
                    bool isRemoved = transparency < 0.1;
                    local.MaterialPropertyValues2 = new double[] {
                        isRemoved ? 0.92 : 0.32, 0.35, 0.85,
                        0.35, 0.65, 0.20, 0.25, transparency, 0.0
                    };
                }
                catch (Exception colorError)
                {
                    Trace.WriteLine("[AFJ-CUT-CANDIDATE] Color: " +
                                    colorError.Message);
                }
                // Register first. Hide() cleanup after SOLIDWORKS may partly
                // display an object, just like original GhostPreview.
                ThrowIfClosing();
                shown.Add(new ShownBody(local, part, group.EndsWith("_CUT", StringComparison.Ordinal)));
                Trace.WriteLine("[AFJ-R52] stage=DISPLAY3_BEGIN group=" + group + " temporary=" + local.IsTemporaryBody() + " context=selected_component");
                int status = local.Display3(component, color,
                    (int)swTempBodySelectOptions_e.swTempBodySelectOptionNone);
                Trace.WriteLine("[AFJ-R52] stage=DISPLAY3_END group=" + group + " status=" + status + " tracked=" + shown.Count + " actual_visibility=UNVERIFIED");
                if (status != 0)
                    throw new InvalidOperationException(
                        "Display3 lỗi=" + status + ", ghost được giữ để dọn.");
            }
        }

        public bool TryClear(out string error)
        {
            error = null;
            if (busy)
            {
                error = "Preview đang xử lý; chưa thể dọn.";
                return false;
            }

            busy = true;
            try
            {
                return ClearInternal(out error);
            }
            finally
            {
                busy = false;
            }
        }

        private bool IsOriginalAssemblyActive()
        {
            try
            {
                ModelDoc2 active = app.ActiveDoc as ModelDoc2;
                return active != null &&
                    active.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY &&
                    app.IsSame(active, sourceAssembly) ==
                        (int)swObjectEquality.swObjectSame;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[AFJ-GHOST] Document identity error: " + ex);
                return false;
            }
        }

        private int ShowComponent(Component2 component, int colorRef,
                                  double transparency)
        {
            ModelDoc2 part = component.GetModelDoc2() as ModelDoc2;
            if (part == null ||
                part.GetType() != (int)swDocumentTypes_e.swDocPART)
                throw new InvalidOperationException(
                    "Part chưa được Resolve hoặc Component không phải Part.");

            object info;
            object raw = component.GetBodies3(
                (int)swBodyType_e.swSolidBody, out info);
            Array bodies = raw as Array;
            if (bodies == null || bodies.Length == 0)
                throw new InvalidOperationException(
                    "Không có Solid Body được tải trong Component.");
            if (bodies.Length > MaxBodiesPerComponent)
                throw new InvalidOperationException(
                    "Part có quá nhiều body cho Preview thử nghiệm.");

            int count = 0;
            foreach (object item in bodies)
            {
                ThrowIfClosing();
                Body2 original = item as Body2;
                if (original == null)
                    throw new InvalidOperationException(
                        "SOLIDWORKS trả về body không hợp lệ.");

                Body2 copy = original.Copy() as Body2;
                if (copy == null || !copy.IsTemporaryBody())
                    throw new InvalidOperationException(
                        "Không tạo được bản sao Temporary Body.");

                // MaterialPropertyValues2: [R,G,B,Ambient,Diffuse,Specular,
                // Shininess,Transparency,Emission], range 0..1.
                // Best effort ONLY on copy. If host ignores transparency,
                // still show ghost in distinct colors.
                try
                {
                    bool isA = colorRef ==
                        ColorTranslator.ToWin32(Color.FromArgb(0, 180, 235));
                    copy.MaterialPropertyValues2 = isA
                        ? new double[] { 0.0, 0.71, 0.92, 0.35, 0.65,
                                         0.20, 0.25, transparency, 0.0 }
                        : new double[] { 1.0, 0.61, 0.24, 0.35, 0.65,
                                         0.20, 0.25, transparency, 0.0 };
                }
                catch (Exception appearanceError)
                {
                    Trace.WriteLine(
                        "[AFJ-GHOST] Transparency not available: " +
                        appearanceError.Message);
                }

                // Register the COM body BEFORE entering Display3.
                // If COM throws after a partial draw, cleanup still has a
                // strong reference to the possibly displayed body.
                ThrowIfClosing();
                ShownBody tracked = new ShownBody(copy, part);
                shown.Add(tracked);

                // Non-selectable preview avoids swModifyBlock.
                // Display in the selected Component context; do not apply
                // another component transform.
                int result = copy.Display3(
                    component, colorRef,
                    (int)swTempBodySelectOptions_e.swTempBodySelectOptionNone);
                Trace.WriteLine("[AFJ-R52] stage=DISPLAY3_OVERLAY status=" + result + " actual_visibility=UNVERIFIED");
                if (result != 0)
                    throw new InvalidOperationException(
                        "IBody2.Display3 thất bại, mã lỗi=" + result +
                        ". Body giữ lại để dọn bằng Hide().");
                count++;
            }
            return count;
        }

        /// <summary>
        /// Hide before releasing managed references. Failed hides are RETAINED
        /// for another cleanup attempt; never lose a displayed Body2 reference.
        /// </summary>
        private void RedrawSafely()
        {
            try
            {
                // Do not activate an Assembly or redraw a different document.
                bool active = IsOriginalAssemblyActive();
                Trace.WriteLine("[AFJ-R52] stage=REDRAW_BEGIN bound_active=" + active + " tracked=" + shown.Count);
                if (active) sourceAssembly.GraphicsRedraw2();
                Trace.WriteLine("[AFJ-R52] stage=REDRAW_END actual_visibility=UNVERIFIED");
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[AFJ-GHOST] Graphics redraw warning: " + ex);
            }
        }

        private bool ClearInternal(out string error, bool redraw = true)
        {
            InvalidateCachedSolution("clear");
            return HideDisplayedBodies(false, redraw, out error);
        }

        private bool HideDisplayedBodies(bool removedOnly, bool redraw, out string error)
        {
            error = null;
            bool attempted = false;
            for (int i = shown.Count - 1; i >= 0; i--)
            {
                ShownBody entry = shown[i];
                if (removedOnly && !entry.IsRemoved) continue;
                attempted = true;
                try
                {
                    Trace.WriteLine("[AFJ-R52] stage=HIDE_BEGIN tracked_index=" + i);
                    entry.Copy.Hide(entry.Part);
                    shown.RemoveAt(i); // Remove ownership only after Hide returns.
                    Trace.WriteLine("[AFJ-R52] stage=HIDE_END tracked_index=" + i);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("[AFJ-GHOST] Hide failed: " + ex);
                    error = "Hide thất bại: " + ex.Message;
                }
            }
            if (attempted && redraw) RedrawSafely();
            return error == null;
        }
    }
}

namespace ADDIN.Commands.AutoFrameJoint
{
    internal enum AfjOperationKind
    {
        Cut, Intersect
    }
    internal sealed class AfjToolDefinition
    {
        internal double[][] Polygon;
        internal double[][] ArcCenters;
        internal double[] Direction;
        internal double Length;
        internal string DepthPolicy = "FORMED_PROFILE_ENVELOPE";
        internal double NominalSheetDepth, ApiSheetThickness;
    }
    internal sealed class AfjCutOperation
    {
        internal string EvidenceId;
        internal AfjOperationKind Kind;
        internal readonly List<AfjToolDefinition> Tools=new List<AfjToolDefinition>();
        internal string RegionEvidence;
        internal bool NormalCutIntent;
    }
    internal sealed class AfjCutPlan
    {
        internal readonly List<AfjCutOperation> Operations=new List<AfjCutOperation>();
    }
    internal sealed class AfjJointPlan
    {
        internal string RuleId;
        internal AfjCutPlan A=new AfjCutPlan(), B=new AfjCutPlan();
    }
    internal sealed class AfjBooleanResult
    {
        internal readonly List<Body2> Kept=new List<Body2>(), Removed=new List<Body2>();
        internal double BeforeVolume, AfterVolume, RemovedVolume;
    }
    internal static class AfjTemporaryBoolean
    {
        internal static Body2 Copy(Body2 b)
        {
            Body2 c=b==null?null:b.Copy() as Body2;
            if(c==null||!c.IsTemporaryBody())throw new InvalidOperationException("AFJ: temporary copy unavailable");
            return c;
        }
        internal static double Volume(Body2 b)
        {
            double[] p=b.GetMassProperties(1) as double[];
            if(p==null||p.Length<4||!Finite(p[3])||p[3]<=0)throw new InvalidOperationException("AFJ: invalid solid volume");
            return p[3];
        }
        internal static bool Finite(double x)
        {
            return !Double.IsNaN(x)&&!Double.IsInfinity(x);
        }
        internal static double Total(IEnumerable<Body2> bodies)
        {
            double v=0;
            foreach(Body2 b in bodies)v+=Volume(b);
            return v;
        }
        internal static void Validate(Body2 b)
        {
            if (b == null || !b.IsTemporaryBody() || b.GetType() != (int)swBodyType_e.swSolidBody)
                throw new InvalidOperationException("AFJ: requires a temporary solid B-rep");
            int check2 = b.Check2();
            FaultEntity faults = b.Check3;
            int check3 = faults == null ? 0 : faults.Count;
            if (check2 != 0 || check3 != 0)
                throw new InvalidOperationException("AFJ: invalid B-rep; Check2=" + check2 + "; Check3 faults=" + check3);
            Volume(b);
        }
        internal static List<Body2> Boolean(Body2 a, Body2 b, int operation, out int code, string label = null)
        {
            Validate(a);
            Validate(b);
            // Both operands are disposable copies. Never query/reuse consumed
            // operands after Operations2, including its error path.
            Body2 targetCopy = Copy(a), toolCopy = Copy(b);
            Array raw = targetCopy.Operations2(operation, toolCopy, out code) as Array;
            string errorName = Enum.IsDefined(typeof(swBodyOperationError_e), code)
                ? ((swBodyOperationError_e)code).ToString() : "UNKNOWN";
            string operationName = label ?? ((swBodyOperationType_e)operation).ToString();
            Trace.WriteLine("[AFJ-BOOLEAN] operation=" + operationName +
                " code=" + code + " name=" + errorName);
            var result = new List<Body2>();
            if (code != (int)swBodyOperationError_e.swBodyOperationNoError)
            {
                bool emptyIntersection = operation == (int)swBodyOperationType_e.SWBODYINTERSECT &&
                    (code == (int)swBodyOperationError_e.swBodyOperationDisjointBodies ||
                     code == (int)swBodyOperationError_e.swBodyOperationEmptyBody ||
                     code == (int)swBodyOperationError_e.swBodyOperationNoIntersect);
                if (emptyIntersection || (operation == (int)swBodyOperationType_e.SWBODYCUT &&
                    code == (int)swBodyOperationError_e.swBodyOperationEmptyBody)) return result;
                // No production retry, ResetEdgeTolerances or fallback cutter.
                throw new AfjBooleanException(operationName, code, errorName);
            }
            if (raw == null) return result;
            if (raw.Length > 12) throw new InvalidOperationException("AFJ: too many solid fragments");
            foreach (object value in raw)
            {
                Body2 body = value as Body2;
                Validate(body);
                result.Add(body);
            }
            return result;
        }
        internal static double DifferenceVolume(Body2 a, Body2 b)
        {
            int code;
            var common=Boolean(a, b, (int)swBodyOperationType_e.SWBODYINTERSECT, out code);
            if(common.Count==0)return Volume(a);
            return Total(Boolean(a, b, (int)swBodyOperationType_e.SWBODYCUT, out code));
        }
        internal static Body2 CreatePrism(ISldWorks app, AfjToolDefinition spec)
        {
            if(spec==null||spec.Polygon==null||spec.Polygon.Length<3||spec.Polygon.Length>256||!Finite(spec.Length)||spec.Length<=1e-9)throw new InvalidOperationException("AFJ: invalid prism specification");
            if(spec.ArcCenters!=null&&spec.ArcCenters.Length!=spec.Polygon.Length)throw new InvalidOperationException("AFJ: arc/profile size mismatch");
            double[] n=spec.Direction;
            if(n==null||n.Length!=3||!Finite(n[0])||!Finite(n[1])||!Finite(n[2]))throw new InvalidOperationException("AFJ: missing prism direction");
            double norm=Math.Sqrt(n[0]*n[0]+n[1]*n[1]+n[2]*n[2]);
            if(Math.Abs(norm-1)>1e-7)throw new InvalidOperationException("AFJ: non-unit prism direction");
            var modeler=app.GetModeler() as Modeler;
            var math=app.GetMathUtility() as MathUtility;
            if(modeler==null||math==null)throw new InvalidOperationException("AFJ: modeler unavailable");
            var curves=new List<object>();
            double[] origin=spec.Polygon[0];
            double[] u=null;
            for(int i=0;i<spec.Polygon.Length;i++)
            {
                double[] p=spec.Polygon[i], q=spec.Polygon[(i+1)%spec.Polygon.Length];
                if(p==null||q==null||p.Length!=3||q.Length!=3)throw new InvalidOperationException("AFJ: malformed polygon");
                double[] d=
                {
                    q[0]-p[0], q[1]-p[1], q[2]-p[2]
                }
                ;
                double len=Math.Sqrt(d[0]*d[0]+d[1]*d[1]+d[2]*d[2]);
                if(len<1e-10)throw new InvalidOperationException("AFJ: zero polygon edge");
                for(int j=0;j<3;j++)if(!Finite(p[j]))throw new InvalidOperationException("AFJ: nonfinite vertex");
                if(Math.Abs((p[0]-origin[0])*n[0]+(p[1]-origin[1])*n[1]+(p[2]-origin[2])*n[2])>1e-7)throw new InvalidOperationException("AFJ: non-planar tool");
                if(u==null)u=new double[]
                {
                    d[0]/len, d[1]/len, d[2]/len
                }
                ;
                if(spec.ArcCenters!=null&&spec.ArcCenters[i]!=null)
                {
                    double[] c=spec.ArcCenters[i];
                    if(c.Length!=3)throw new InvalidOperationException("AFJ: invalid arc center");
                    double[] a=
                    {
                        p[0]-c[0], p[1]-c[1], p[2]-c[2]
                    }
                    , b=
                    {
                        q[0]-c[0], q[1]-c[1], q[2]-c[2]
                    }
                    ;
                    double r=Math.Sqrt(a[0]*a[0]+a[1]*a[1]+a[2]*a[2]), rq=Math.Sqrt(b[0]*b[0]+b[1]*b[1]+b[2]*b[2]);
                    double[] axis=
                    {
                        a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]
                    }
                    ;
                    double an=Math.Sqrt(axis[0]*axis[0]+axis[1]*axis[1]+axis[2]*axis[2]);
                    if(!Finite(r)||r<1e-8||Math.Abs(r-rq)>1e-8||an<1e-14)throw new InvalidOperationException("AFJ: unsupported arc geometry");
                    for(int j=0;j<3;j++)axis[j]/=an;
                    Curve arc=modeler.CreateArc(c, axis, r, p, q) as Curve;
                    if(arc==null)throw new InvalidOperationException("AFJ: analytic arc failed");
                    Curve trimmedArc=arc.CreateTrimmedCurve2(p[0], p[1], p[2], q[0], q[1], q[2]);
                    if(trimmedArc==null)throw new InvalidOperationException("AFJ: trimmed analytic arc failed");
                    curves.Add(trimmedArc);
                }
                else
                {
                    Curve line=modeler.CreateLine(p, d) as Curve;
                    if(line==null)throw new InvalidOperationException("AFJ: line failed");
                    Curve trim=line.CreateTrimmedCurve2(p[0], p[1], p[2], q[0], q[1], q[2]);
                    if(trim==null)throw new InvalidOperationException("AFJ: trimmed line failed");
                    curves.Add(trim);
                }
            }
            Surface plane=modeler.CreatePlanarSurface2(origin, n, u) as Surface;
            var dispatch=new System.Runtime.InteropServices.DispatchWrapper[curves.Count];
            for(int i=0;i<curves.Count;i++)dispatch[i]=new System.Runtime.InteropServices.DispatchWrapper(curves[i]);
            Body2 sheet=plane==null?null:plane.CreateTrimmedSheet4(dispatch, true) as Body2;
            if(sheet==null)throw new InvalidOperationException("AFJ: planar tool profile failed");
            MathVector direction=math.CreateVector(n) as MathVector;
            Body2 body=modeler.CreateExtrudedBody(sheet, direction, spec.Length);
            Validate(body);
            return body;
        }
        static void VerifySingleSheetStock(ISldWorks app, Body2 source, AfjToolDefinition spec)
        {
            if (spec.DepthPolicy != "SINGLE_SHEET_BLIND_T_PLUS_OVERRUN") return;
            double[] n = AfjParametricRules.Unit(spec.Direction);
            double entry = AfjManufacturingConventions.CutterEntryClearance;
            double[][] outer = spec.Polygon.Select(p => AfjParametricRules.Add(p, AfjParametricRules.Scale(n, entry))).ToArray();
            bool outerFound = false, innerFound = false;
            foreach (Face2 face in (Array)source.GetFaces())
            {
                Surface surface = face.GetSurface() as Surface;
                if (surface == null || !surface.IsPlane()) continue;
                double[] p = surface.PlaneParams as double[];
                if (p == null || p.Length < 6 || Math.Abs(AfjParametricRules.Dot(AfjParametricRules.Unit(p.Take(3).ToArray()), n)) < .999999) continue;
                double depth = AfjParametricRules.Dot(AfjParametricRules.Sub(p.Skip(3).Take(3).ToArray(), outer[0]), n);
                if (Math.Abs(depth) < 1e-7) outerFound = true;
                if (Math.Abs(depth - spec.ApiSheetThickness) < 1e-7) innerFound = true;
            }
            if (!outerFound || !innerFound)
                throw new InvalidOperationException("UNVERIFIED_RULE: single-sheet source does not expose both verified skin planes");
            var extensions = new[] {
                new AfjToolDefinition { Polygon = spec.Polygon, Direction = n, Length = entry },
                new AfjToolDefinition { Polygon = outer.Select(p => AfjParametricRules.Add(p, AfjParametricRules.Scale(n, spec.ApiSheetThickness))).ToArray(),
                    Direction = n, Length = AfjManufacturingConventions.SheetCutOverrun }
            };
            foreach (AfjToolDefinition extension in extensions)
            {
                int code;
                double intrusion = Total(Boolean(source, CreatePrism(app, extension),
                    (int)swBodyOperationType_e.SWBODYINTERSECT, out code, "single_sheet:overrun_stock_guard"));
                if (intrusion > 1e-13)
                    throw new InvalidOperationException("UNVERIFIED_RULE: single-sheet overrun reaches additional material or a Bend; use a verified compound plan");
            }
        }

        internal static AfjBooleanResult Execute(ISldWorks app, Body2 source, AfjCutPlan plan)
        {
            if(plan==null||plan.Operations.Count>64)throw new InvalidOperationException("AFJ: invalid plan");
            Validate(source);
            var result=new AfjBooleanResult();
            result.BeforeVolume=Volume(source);
            var kept=new List<Body2>
            {
                Copy(source)
            }
            ;
            foreach(AfjCutOperation operation in plan.Operations)
            {
                if(String.IsNullOrEmpty(operation.EvidenceId)||operation.Tools.Count==0)throw new InvalidOperationException("UNVERIFIED_RULE: operation has no evidence/tool");
                foreach(AfjToolDefinition spec in operation.Tools)
                {
                    var effectiveTools=new List<Body2>
                    {
                        CreatePrism(app, spec)
                    }
                    ;
                    foreach(Body2 tool in effectiveTools)
                    {
                        var next=new List<Body2>();
                        foreach(Body2 body in kept)
                        {
                            VerifySingleSheetStock(app, body, spec);
                            int code;
                            var intersection=Boolean(body, tool, (int)swBodyOperationType_e.SWBODYINTERSECT, out code, operation.EvidenceId + ":measure");
                            if(operation.Kind==AfjOperationKind.Cut)
                            {
                                if(intersection.Count==0||Total(intersection)<=1e-13)
                                {
                                    next.Add(Copy(body));
                                    continue;
                                }
                                next.AddRange(Boolean(body, tool, (int)swBodyOperationType_e.SWBODYCUT, out code, operation.EvidenceId + ":cut"));
                                result.Removed.AddRange(intersection);
                            }
                            else
                            {
                                if(intersection.Count==0)throw new InvalidOperationException("AFJ: plan would erase entire member");
                                next.AddRange(intersection);
                                result.Removed.AddRange(Boolean(body, tool, (int)swBodyOperationType_e.SWBODYCUT, out code, operation.EvidenceId + ":cut"));
                            }
                        }
                        if(next.Count==0||next.Count>12)throw new InvalidOperationException("AFJ: invalid remaining-body count");
                        kept=next;
                    }
                }
            }
            result.Kept.AddRange(kept);
            result.AfterVolume=Total(kept);
            result.RemovedVolume=Total(result.Removed);
            if(Math.Abs(result.BeforeVolume-result.AfterVolume-result.RemovedVolume)>Math.Max(1e-12, result.BeforeVolume*1e-8))throw new InvalidOperationException("AFJ: volume conservation failed");
            return result;
        }
    }
}
namespace ADDIN.Commands.AutoFrameJoint
{
    internal sealed class AfjProfile
    {
        internal string Signature;
        internal double Thickness, Radius, StockLength;
        internal double[][] Lines;
        // Pair-specific selection is always an actual member of Lines.
        // It is derived from native occurrence orientation and B-rep skins,
        // not from line length, name or nominal region labels.
        internal double[] SelectedWeb;
        internal double[] Axis;
        internal int BendFaceCount;
        internal string[] Regions;
    }
    internal static class AfjProfileAnalyzer
    {
        private static string Q(double x)
        {
            if(!AfjTemporaryBoolean.Finite(x))throw new InvalidOperationException("UNVERIFIED_RULE: nonfinite profile");
            return Math.Round(x/1e-7, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        }
        internal static string Point(double[] p)
        {
            return String.Join(",", p.Select(Q));
        }
        internal static double[] Transform(double[] p, double[] m)
        {
            var q=new double[3];
            for(int i=0;i<3;i++)q[i]=(p[0]*m[i]+p[1]*m[3+i]+p[2]*m[6+i])*m[12]+m[9+i];
            return q;
        }
        internal static AfjProfile Analyze(ModelDoc2 model, Body2 nativeBody)
        {
            if(model==null||nativeBody==null)throw new InvalidOperationException("UNVERIFIED_RULE: missing Part/body");
            Feature flange=null;
            SheetMetalFeatureData sm=null;
            int flangeCount=0;
            for(Feature f=model.FirstFeature() as Feature;f!=null;f=f.GetNextFeature() as Feature)
            {
                if(f.GetTypeName2()=="SMBaseFlange")
                {
                    flange=f;
                    flangeCount++;
                }
                if(f.GetTypeName2()=="SheetMetal")sm=f.GetDefinition() as SheetMetalFeatureData;
            }
            if(flangeCount!=1||sm==null||flange.IsSuppressed())throw new InvalidOperationException("UNVERIFIED_RULE: requires one formed base flange and SheetMetal");
            BaseFlangeFeatureData def=flange.GetDefinition() as BaseFlangeFeatureData;
            if(def==null)throw new InvalidOperationException("UNVERIFIED_RULE: base flange definition unavailable");
            // ReverseThickness is Sketch-order dependent. Verify physical skins in Generate instead.
            Sketch sketch=null;
            Array parents=flange.GetParents() as Array;
            if(parents!=null)foreach(object p in parents)
            {
                var parent=p as Feature;
                if(parent==null)continue;
                var s=parent.GetSpecificFeature2() as Sketch;
                if(s!=null)
                {
                    if(sketch!=null)throw new InvalidOperationException("UNVERIFIED_RULE: ambiguous profile Sketch");
                    sketch=s;
                }
            }
            if(sketch==null)throw new InvalidOperationException("UNVERIFIED_RULE: base profile Sketch unavailable");
            MathTransform inverse=sketch.ModelToSketchTransform.Inverse() as MathTransform;
            double[] matrix=inverse==null?null:inverse.ArrayData as double[];
            if(matrix==null||matrix.Length!=16)throw new InvalidOperationException("UNVERIFIED_RULE: invalid profile transform");
            var lines=new List<double[]>();
            var canonical=new List<string>();
            Array segments=sketch.GetSketchSegments() as Array;
            if(segments!=null)foreach(object o in segments)
            {
                var segment=o as SketchSegment;
                if(segment==null||segment.ConstructionGeometry)continue;
                var line=o as SketchLine;
                if(line==null)throw new InvalidOperationException("UNVERIFIED_RULE: curved base profile");
                var a=line.GetStartPoint2() as SketchPoint;
                var b=line.GetEndPoint2() as SketchPoint;
                double[] u=Transform(new double[]
                {
                    a.X, a.Y, a.Z
                }
                , matrix), v=Transform(new double[]
                {
                    b.X, b.Y, b.Z
                }
                , matrix);
                lines.Add(u.Concat(v).ToArray());
                string x=Point(u), y=Point(v);
                canonical.Add(String.CompareOrdinal(x, y)<0?x+":"+y:y+":"+x);
            }
            if(lines.Count<2||lines.Count>16)throw new InvalidOperationException("UNVERIFIED_RULE: unsupported base profile topology");
            canonical.Sort(StringComparer.Ordinal);
            var result=new AfjProfile
            {
                Thickness=sm.Thickness, Radius=sm.BendRadius, StockLength=def.D1OffsetDistance, Lines=lines.ToArray(), Axis=new double[]
                {
                    matrix[6], matrix[7], matrix[8]
                }
            }
            ;
            if(Math.Abs(def.Thickness-result.Thickness)>1e-8||Math.Abs(def.BendRadius-result.Radius)>1e-8)throw new InvalidOperationException("UNVERIFIED_RULE: overridden thickness/radius");
            result.Signature=String.Join("|", canonical)+";t="+Q(result.Thickness)+";r="+Q(result.Radius)+";L="+Q(result.StockLength)+";ends="+def.D1OffsetType+","+def.D2OffsetType+";offset="+def.OffsetDirections+";reverse="+def.ReverseThickness+","+def.ReverseDirection+";axis="+Point(result.Axis);
            try
            {
                int web=lines.IndexOf(AfjParametricRules.WebLine(result));
                var depth=Enumerable.Repeat(99, lines.Count).ToArray();
                depth[web]=0;
                for(int pass=0;pass<lines.Count;pass++)for(int i=0;i<lines.Count;i++)for(int j=0;j<lines.Count;j++)if(Adjacent(lines[i], lines[j]))depth[j]=Math.Min(depth[j], depth[i]+1);
                result.Regions=depth.Select(d=>d==0?"WEB":d==1?"WALL":"RETURN_LIP_OR_STEP").ToArray();
            }
            catch(InvalidOperationException ex)
            {
                if(!ex.Message.StartsWith("UNVERIFIED_RULE",StringComparison.Ordinal))throw;
                result.Regions=Enumerable.Repeat("UNKNOWN_GRAPH",lines.Count).ToArray();
                Trace.WriteLine("[AFJ-FOLDED] Primary Web label unavailable; preserve all base strips for qualified axial butt rule");
            }
            Array faces=nativeBody.GetFaces() as Array;
            if(faces!=null)foreach(object o in faces)
            {
                Surface s=((Face2)o).GetSurface() as Surface;
                if(!s.IsCylinder())continue;
                double[] c=s.CylinderParams as double[];
                double dot=c[3]*result.Axis[0]+c[4]*result.Axis[1]+c[5]*result.Axis[2];
                if(Math.Abs(Math.Abs(dot)-1)<1e-7&&(Math.Abs(c[6]-result.Radius)<1e-8||Math.Abs(c[6]-result.Radius-result.Thickness)<1e-8))result.BendFaceCount++;
            }
            if(result.BendFaceCount<2)throw new InvalidOperationException("UNVERIFIED_RULE: formed Bend B-rep not found (flat configuration?)");
            return result;
        }
        private static bool Adjacent(double[] a, double[] b)
        {
            for(int i=0;i<2;i++)for(int j=0;j<2;j++)
            {
                double d=0;
                for(int k=0;k<3;k++)d+=(a[i*3+k]-b[j*3+k])*(a[i*3+k]-b[j*3+k]);
                if(d<1e-14)return true;
            }
            return false;
        }
    }
}
namespace ADDIN.Commands.AutoFrameJoint
{
    internal sealed class AfjJointSolution
    {
        internal AfjJointPlan Plan;
        // Construction intent from the reviewed Preview; OK must not run Solve again.
        internal AfjParametricPlan ReviewedIntent;
        internal AfjBooleanResult A, B;
        internal ModelDoc2 PartA, PartB;
        internal MathTransform TransformA, TransformB;
        internal double BeforeOverlap, AfterOverlap, AfterGap, LengthA, LengthB;
        internal bool CoverA;
        internal string CoverageWarning, ProfileGraphAJson, ProfileGraphBJson;
        internal string CoverageMeasurementStatus="NOT_APPLICABLE", CoverageMeasurementDetail;
        internal double? LongCutExposedAreaSquareMetres, LongCutTotalAreaSquareMetres, LongCutCoveredAreaSquareMetres;
        internal double? LongCutExposedBandMinimumMetres, LongCutExposedBandMaximumMetres;
        internal double[] LongCutExposedBandDirectionWorld;
    }
    internal static class AfjJointRuleLibrary
    {
        internal static AfjParametricPlan Create(AfjProfile a, AfjProfile b, Body2 ba, Body2 bb, MathTransform ta, MathTransform tb, bool aCovers)
        {
            return AfjParametricRules.Generate(a, b, ba, bb, ta, tb, aCovers, AfjParametricRules.AssemblyGap);
        }
    }
    internal static class AfjJointSolver
    {
        internal static Body2 NativeBody(Component2 c, out ModelDoc2 part, out MathTransform transform)
        {
            part=c.GetModelDoc2() as ModelDoc2;
            transform=c.GetTotalTransform(false);
            if(part==null||part.GetType()!=(int)swDocumentTypes_e.swDocPART||transform==null)throw new InvalidOperationException("UNVERIFIED_RULE: unresolved Part/transform");
            var cfg=part.ConfigurationManager.ActiveConfiguration;
            if(cfg==null||!String.Equals(cfg.Name, c.ReferencedConfiguration, StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("UNVERIFIED_RULE: Part/occurrence configuration mismatch; activate folded configuration manually");
            Rigid(transform.ArrayData as double[]);
            object info;
            Array bodies=c.GetBodies3((int)swBodyType_e.swSolidBody, out info) as Array;
            if(bodies==null||bodies.Length!=1)throw new InvalidOperationException("UNVERIFIED_RULE: requires one solid per Frame");
            Body2 body=AfjTemporaryBoolean.Copy(bodies.GetValue(0) as Body2);
            AfjTemporaryBoolean.Validate(body);
            return body;
        }
        private static void Rigid(double[] t)
        {
            if(t==null||t.Length!=16||t.Any(x=>!AfjTemporaryBoolean.Finite(x))||Math.Abs(t[12]-1)>1e-8)throw new InvalidOperationException("UNVERIFIED_RULE: unsupported scaled transform");
            for(int i=0;i<3;i++)for(int j=0;j<3;j++)
            {
                double dot=0;
                for(int k=0;k<3;k++)dot+=t[i*3+k]*t[j*3+k];
                if(Math.Abs(dot-(i==j?1:0))>1e-7)throw new InvalidOperationException("UNVERIFIED_RULE: nonrigid transform");
            }
            double det=t[0]*(t[4]*t[8]-t[5]*t[7])-t[1]*(t[3]*t[8]-t[5]*t[6])+t[2]*(t[3]*t[7]-t[4]*t[6]);
            if(det<.999999)throw new InvalidOperationException("UNVERIFIED_RULE: mirrored placement");
        }
        internal static List<Body2> World(IEnumerable<Body2> bodies, MathTransform transform)
        {
            var result=new List<Body2>();
            foreach(Body2 b in bodies)
            {
                Body2 c=AfjTemporaryBoolean.Copy(b);
                if(!c.ApplyTransform(transform))throw new InvalidOperationException("AFJ: world transform failed");
                AfjTemporaryBoolean.Validate(c);
                result.Add(c);
            }
            return result;
        }
        internal static double Overlap(IEnumerable<Body2> a, IEnumerable<Body2> b)
        {
            double total=0;
            foreach(Body2 x in a)foreach(Body2 y in b)
            {
                int code;
                total+=AfjTemporaryBoolean.Total(AfjTemporaryBoolean.Boolean(x, y, (int)swBodyOperationType_e.SWBODYINTERSECT, out code));
            }
            return total;
        }
        internal static double Gap(ModelDoc2 model, IEnumerable<Body2> a, IEnumerable<Body2> b)
        {
            double gap=Double.MaxValue;
            foreach(Body2 x in a)foreach(Body2 y in b)
            {
                object p, q;
                double d=model.ClosestDistance(x, y, out p, out q);
                if(d<0||!AfjTemporaryBoolean.Finite(d))throw new InvalidOperationException("AFJ: exact B-rep distance unavailable");
                gap=Math.Min(gap, d);
            }
            return gap;
        }
        internal static double Projection(Body2 body, double[] axis)
        {
            double x, y, z;
            if(!body.GetExtremePoint(axis[0], axis[1], axis[2], out x, out y, out z))throw new InvalidOperationException("UNVERIFIED_RULE: longitudinal extent unavailable");
            return x*axis[0]+y*axis[1]+z*axis[2];
        }
        internal static double Length(Body2 body, double[] axis)
        {
            double[] n=AfjParametricRules.Unit(axis);
            double result=Projection(body, n)+Projection(body, AfjParametricRules.Scale(n, -1));
            if(!AfjTemporaryBoolean.Finite(result)||result<=1e-6)throw new InvalidOperationException("UNVERIFIED_RULE: invalid longitudinal span");
            return result;
        }
        private static void SelectedEnd(AfjBooleanResult result, MathTransform transform, double[] origin, double[] outward)
        {
            double[] matrix=(double[])((MathTransform)transform.Inverse()).ArrayData;
            double[] p=AfjProfileAnalyzer.Transform(origin, matrix), q=AfjProfileAnalyzer.Transform(AfjParametricRules.Add(origin, outward), matrix), n=AfjParametricRules.Unit(AfjParametricRules.Sub(q, p));
            foreach(Body2 removed in result.Removed)
            {
                double minimum=-Projection(removed, AfjParametricRules.Scale(n, -1))-AfjParametricRules.Dot(p, n);
                if(minimum < -AfjParametricRules.AssemblyGap-1e-7)throw new InvalidOperationException("UNVERIFIED_RULE: removed material extends beyond selected end");
            }
        }
        internal static AfjJointSolution Solve(ISldWorks sw, ModelDoc2 assembly, Component2 ca, Component2 cb)
        {
            ModelDoc2 pa, pb;
            MathTransform ta, tb;
            Body2 a=NativeBody(ca, out pa, out ta), b=NativeBody(cb, out pb, out tb);
            AfjProfile pfa=AfjProfileAnalyzer.Analyze(pa, a), pfb=AfjProfileAnalyzer.Analyze(pb, b);
            return SolveBodies(sw, assembly, a, b, pfa, pfb, pa, pb, ta, tb);
        }
        internal static AfjJointSolution SolveBodies(ISldWorks sw, ModelDoc2 assembly, Body2 a, Body2 b, AfjProfile pfa, AfjProfile pfb, ModelDoc2 pa, ModelDoc2 pb, MathTransform ta, MathTransform tb)
        {
            // The current manufacturing policy is explicitly supplied by the user:
            // the shorter actual Frame covers. Base-extrusion stock lengths, names,
            // historical Cut tool coordinates and A/B order do not select a winner.
            Rigid(ta.ArrayData as double[]);
            Rigid(tb.ArrayData as double[]);
            double la=Length(a, pfa.Axis), lb=Length(b, pfb.Axis);
            if(Math.Abs(la-lb)<1e-6)throw new InvalidOperationException("UNVERIFIED_RULE: equal-length Frames require a consistent drawing cover side");
            bool coverA=la<lb;
            AfjParametricPlan generated=AfjJointRuleLibrary.Create(pfa, pfb, a, b, ta, tb, coverA);
            var result=new AfjJointSolution
            {
                Plan=generated.Plan, ReviewedIntent=generated, PartA=pa, PartB=pb, TransformA=ta, TransformB=tb, LengthA=la, LengthB=lb, CoverA=coverA, CoverageWarning=generated.CoverageWarning, ProfileGraphAJson=generated.Folded==null?null:(coverA?generated.Folded.Winner.GraphJson:generated.Folded.Loser.GraphJson), ProfileGraphBJson=generated.Folded==null?null:(coverA?generated.Folded.Loser.GraphJson:generated.Folded.Winner.GraphJson)
            }
            ;
            result.A=AfjTemporaryBoolean.Execute(sw, a, generated.Plan.A);
            result.B=AfjTemporaryBoolean.Execute(sw, b, generated.Plan.B);
            if(result.A.Kept.Count!=1||result.B.Kept.Count!=1)throw new InvalidOperationException("UNVERIFIED_RULE: each Frame must remain one connected solid");
            SelectedEnd(result.A, ta, generated.Folded==null?generated.JointOrigin:AfjFoldedButtRules.SelectedOrigin(generated,coverA), coverA?generated.OutWinner:generated.OutLoser);
            SelectedEnd(result.B, tb, generated.Folded==null?generated.JointOrigin:AfjFoldedButtRules.SelectedOrigin(generated,!coverA), coverA?generated.OutLoser:generated.OutWinner);
            AfjParametricRules.VerifyCoverage(sw, generated, ta, tb, coverA, result.A, result.B);
            var wa=World(result.A.Kept, ta);
            var wb=World(result.B.Kept, tb);
            result.BeforeOverlap=Overlap(World(new[]
            {
                a
            }
            , ta), World(new[]
            {
                b
            }
            , tb));
            result.AfterOverlap=Overlap(wa, wb);
            if(result.AfterOverlap>1e-12)throw new InvalidOperationException("UNVERIFIED_RULE: remaining solids interfere");
            result.AfterGap=Gap(assembly, wa, wb);
            if(Math.Abs(result.AfterGap-AfjParametricRules.AssemblyGap)>1e-6)throw new InvalidOperationException("UNVERIFIED_RULE: exact minimum assembly clearance is not 0.1 mm");
            AfjFoldedButtRules.MeasureLongCutExposure(sw, generated, result, wa, wb);
            return result;
        }
    }
}
namespace ADDIN.Commands.AutoFrameJoint
{
    internal sealed class AfjUSection
    {
        internal double[] Origin, Axis, WidthDirection, DepthDirection;
        internal double Width, Thickness, Radius, NearHeight, FarHeight, MinWidth, MaxWidth, MaxDepth;
        internal double NearTangent
        {
            get
            {
                return Thickness+Radius;
            }
        }
        internal double FarTangent
        {
            get
            {
                return Width-Thickness-Radius;
            }
        }
        internal bool NearReturn, FarReturn;
    }
    internal sealed class AfjParametricPlan
    {
        internal AfjJointPlan Plan;
        internal double[] JointOrigin, OutLoser, OutWinner, Depth;
        internal AfjUSection Loser, Winner;
        internal double[] SeamNear, SeamFar;
        internal string Derivation, CoverageWarning;
        internal AfjFoldedButtRules.Context Folded;
    }
    internal static class AfjParametricRules
    {
        internal const double AssemblyGap=AfjManufacturingConventions.AssemblyFitGap;
        internal const double ToolOverrun=AfjManufacturingConventions.CutterEnvelopeOverrun;
        internal static double Dot(double[] a, double[] b)
        {
            return a[0]*b[0]+a[1]*b[1]+a[2]*b[2];
        }
        internal static double[] Sub(double[] a, double[] b)
        {
            return new[]
            {
                a[0]-b[0], a[1]-b[1], a[2]-b[2]
            }
            ;
        }
        internal static double[] Add(double[] a, double[] b)
        {
            return new[]
            {
                a[0]+b[0], a[1]+b[1], a[2]+b[2]
            }
            ;
        }
        internal static double[] Scale(double[] a, double s)
        {
            return new[]
            {
                a[0]*s, a[1]*s, a[2]*s
            }
            ;
        }
        internal static double[] Unit(double[] a)
        {
            double l=Math.Sqrt(Dot(a, a));
            if(l<1e-9)throw new InvalidOperationException("UNVERIFIED_RULE: zero profile vector");
            return Scale(a, 1/l);
        }
        internal static double[] P(double[] line, int end)
        {
            return line.Skip(end*3).Take(3).ToArray();
        }
        static bool Same(double[] a, double[] b)
        {
            return Dot(Sub(a, b), Sub(a, b))<1e-14;
        }
        static double[] WorldPoint(double[] p, MathTransform t)
        {
            return AfjProfileAnalyzer.Transform(p, (double[])t.ArrayData);
        }
        static double[] WorldVector(double[] v, MathTransform t)
        {
            return Sub(WorldPoint(v, t), WorldPoint(new double[3], t));
        }
        static double[] OtherEnd(double[] l, double[] endpoint)
        {
            if(Same(P(l, 0), endpoint))return P(l, 1);
            if(Same(P(l, 1), endpoint))return P(l, 0);
            return null;
        }
        private static double[][] WebCandidates(AfjProfile profile)
        {
            if(profile==null||profile.Lines==null)
                throw new InvalidOperationException("UNVERIFIED_RULE: profile lines unavailable for Web selection");
            var candidates=new List<double[]>();
            foreach(double[] line in profile.Lines)
            {
                if(line==null||line.Length!=6||line.Any(value=>!AfjTemporaryBoolean.Finite(value)))
                    throw new InvalidOperationException("UNVERIFIED_RULE: malformed profile line in Web selection");
                double[] a=P(line,0),b=P(line,1),u=Unit(Sub(b,a));
                var first=profile.Lines.Where(x=>x!=line&&OtherEnd(x,a)!=null).ToArray();
                var last=profile.Lines.Where(x=>x!=line&&OtherEnd(x,b)!=null).ToArray();
                if(first.Length!=1||last.Length!=1)continue;
                double[] v=Unit(Sub(OtherEnd(first[0],a),a)),w=Unit(Sub(OtherEnd(last[0],b),b));
                if(Math.Abs(Dot(u,v))<1e-7&&Dot(v,w)>.999999)candidates.Add(line);
            }
            return candidates.ToArray();
        }
        internal static double[] WebLine(AfjProfile profile)
        {
            double[][] candidates=WebCandidates(profile);
            if(profile.SelectedWeb!=null)
            {
                if(!candidates.Any(line=>Object.ReferenceEquals(line,profile.SelectedWeb)))
                    throw new InvalidOperationException("UNVERIFIED_RULE: selected joint Web is not an actual qualifying profile line");
                return profile.SelectedWeb;
            }
            if(candidates.Length!=1)
                throw new InvalidOperationException("UNVERIFIED_RULE: pair context is required to select the Web between parallel outward Walls");
            return candidates[0];
        }
        private sealed class JointWebCandidate
        {
            internal double[] Line,Origin,Direction,Depth;
            internal int Index;
            internal double Width;
        }
        private sealed class JointWebSkin
        {
            internal double[] Point,Normal;
        }
        private static JointWebCandidate[] JointWebCandidates(AfjProfile profile,MathTransform transform,double[] mateAxis)
        {
            var result=new List<JointWebCandidate>();
            double[] ownAxis=Unit(WorldVector(profile.Axis,transform));
            foreach(double[] line in WebCandidates(profile))
            {
                double[] a=P(line,0),b=P(line,1),direction=Unit(WorldVector(Sub(b,a),transform));
                if(Math.Abs(Dot(direction,mateAxis))<.999999||Math.Abs(Dot(direction,ownAxis))>1e-7)continue;
                double[] wall=profile.Lines.Single(other=>other!=line&&OtherEnd(other,a)!=null);
                double[] depth=Unit(WorldVector(Sub(OtherEnd(wall,a),a),transform));
                if(Math.Abs(Dot(depth,ownAxis))>1e-7||Math.Abs(Dot(depth,direction))>1e-7)continue;
                result.Add(new JointWebCandidate{Line=line,Index=Array.IndexOf(profile.Lines,line),Origin=WorldPoint(a,transform),Direction=direction,Depth=depth,Width=Math.Sqrt(Dot(Sub(b,a),Sub(b,a)))});
            }
            return result.ToArray();
        }
        private static JointWebSkin[] JointWebSkins(Body2 body,MathTransform transform)
        {
            var raw=body.GetFaces() as Array;
            if(raw==null||raw.Length>50000)
                throw new InvalidOperationException("UNVERIFIED_RULE: bounded native skin inventory unavailable for joint Web selection");
            var result=new List<JointWebSkin>();
            foreach(Face2 face in raw.Cast<object>().OfType<Face2>())
            {
                var surface=face.GetSurface() as Surface;
                if(surface==null||!surface.IsPlane()||face.GetArea()<=1e-14)continue;
                double[] parameters=surface.PlaneParams as double[];
                if(parameters==null||parameters.Length<6||parameters.Any(value=>!AfjTemporaryBoolean.Finite(value)))continue;
                result.Add(new JointWebSkin{Normal=Unit(WorldVector(parameters.Take(3).ToArray(),transform)),Point=WorldPoint(parameters.Skip(3).Take(3).ToArray(),transform)});
            }
            return result.ToArray();
        }
        private static bool HasJointWebSkins(JointWebCandidate candidate,JointWebSkin[] skins,double thickness)
        {
            if(!AfjTemporaryBoolean.Finite(thickness)||thickness<=1e-7)return false;
            bool outside=false,inside=false;
            foreach(var skin in skins)
            {
                if(Math.Abs(Dot(skin.Normal,candidate.Depth))<.999999)continue;
                double support=Dot(Sub(skin.Point,candidate.Origin),candidate.Depth);
                if(Math.Abs(support)<1e-7)outside=true;
                if(Math.Abs(support-thickness)<1e-7)inside=true;
            }
            return outside&&inside;
        }
        private static void LabelSelectedWebRegions(AfjProfile profile)
        {
            int index=Array.IndexOf(profile.Lines,profile.SelectedWeb);
            if(index<0)throw new InvalidOperationException("UNVERIFIED_RULE: selected Web identity was lost");
            var distance=Enumerable.Repeat(99,profile.Lines.Length).ToArray();distance[index]=0;
            for(int pass=0;pass<profile.Lines.Length;pass++)
                for(int i=0;i<profile.Lines.Length;i++)for(int j=0;j<profile.Lines.Length;j++)
                    if(i!=j&&(OtherEnd(profile.Lines[i],P(profile.Lines[j],0))!=null||OtherEnd(profile.Lines[i],P(profile.Lines[j],1))!=null))
                        distance[j]=Math.Min(distance[j],distance[i]+1);
            profile.Regions=distance.Select(value=>value==0?"WEB":value==1?"WALL":"RETURN_LIP_OR_STEP").ToArray();
        }
        private static void SelectJointWebs(AfjProfile profileA,AfjProfile profileB,Body2 bodyA,Body2 bodyB,MathTransform ta,MathTransform tb)
        {
            // Resolve orientation before ProfileCenter/Outgoing/Section. A U
            // section may contain multiple lines with two outward Walls (2216
            // is an example); the longest line is not necessarily the joint Web.
            double[] axisA=Unit(WorldVector(profileA.Axis,ta)),axisB=Unit(WorldVector(profileB.Axis,tb));
            if(Math.Abs(Dot(axisA,axisB))>1e-7)
                throw new InvalidOperationException("UNVERIFIED_RULE: pair-context Web selection currently requires orthogonal native axes");
            var candidatesA=JointWebCandidates(profileA,ta,axisB);var candidatesB=JointWebCandidates(profileB,tb,axisA);
            var skinsA=JointWebSkins(bodyA,ta);var skinsB=JointWebSkins(bodyB,tb);
            var matches=new List<Tuple<JointWebCandidate,JointWebCandidate>>();
            foreach(var a in candidatesA)foreach(var b in candidatesB)
            {
                if(Dot(a.Depth,b.Depth)<.999999||Math.Abs(Dot(Sub(a.Origin,b.Origin),a.Depth))>1e-7)continue;
                if(!HasJointWebSkins(a,skinsA,profileA.Thickness)||!HasJointWebSkins(b,skinsB,profileB.Thickness))continue;
                matches.Add(Tuple.Create(a,b));
            }
            Trace.WriteLine("[AFJ-R53] JOINT_WEB_CANDIDATES A="+candidatesA.Length+" B="+candidatesB.Length+" compatible_pairs="+matches.Count);
            if(matches.Count!=1)
                throw new InvalidOperationException("UNVERIFIED_RULE: native joint context does not select one unique coplanar 0/t Web skin pair");
            profileA.SelectedWeb=matches[0].Item1.Line;profileB.SelectedWeb=matches[0].Item2.Line;
            LabelSelectedWebRegions(profileA);LabelSelectedWebRegions(profileB);
            Trace.WriteLine("[AFJ-R53] JOINT_WEB_SELECTED A_line="+matches[0].Item1.Index+" B_line="+matches[0].Item2.Index+" A_width_m="+matches[0].Item1.Width.ToString("R",CultureInfo.InvariantCulture)+" B_width_m="+matches[0].Item2.Width.ToString("R",CultureInfo.InvariantCulture)+" evidence=NATIVE_AXIS_COPLANAR_SKINS_0_T");
        }
        internal static AfjUSection Section(AfjProfile profile, MathTransform transform, double[] outgoingOther)
        {
            double[] web=WebLine(profile);
            double[] a=P(web, 0), b=P(web, 1);
            if(Dot(WorldVector(Sub(b, a), transform), outgoingOther)<0)
            {
                double[] temp=a;
                a=b;
                b=temp;
            }
            double[] width=Unit(Sub(b, a)), axis=Unit(profile.Axis);
            double[][][] paths=new double[2][][];
            for(int side=0;side<2;side++)
            {
                double[] point=side==0?a:b;
                var path=new List<double[]>();
                var used=new HashSet<double[]>
                {
                    web
                }
                ;
                while(true)
                {
                    var options=profile.Lines.Where(l=>!used.Contains(l)&&OtherEnd(l, point)!=null).ToList();
                    if(options.Count==0)break;
                    if(options.Count!=1)throw new InvalidOperationException("UNVERIFIED_RULE: branched profile");
                    double[] segment=options[0];
                    point=OtherEnd(segment, point);
                    path.Add(point);
                    used.Add(segment);
                    if(path.Count>2)throw new InvalidOperationException("UNVERIFIED_RULE: U miter has additional formed strips without qualified per-strip KEEP/CUT; no axial BUTT fallback");
                }
                paths[side]=path.ToArray();
            }
            if(paths[0].Length<1||paths[1].Length<1)throw new InvalidOperationException("UNVERIFIED_RULE: requires two formed Walls");
            double[] depth=Unit(Sub(paths[0][0], a));
            if(Math.Abs(Dot(depth, width))>1e-7||Math.Abs(Dot(depth, axis))>1e-7)throw new InvalidOperationException("UNVERIFIED_RULE: inclined Wall needs surface-coverage rule");
            double[] farWall=Sub(paths[1][0], b);
            if(Math.Abs(Dot(Unit(farWall), depth)-1)>1e-7)throw new InvalidOperationException("UNVERIFIED_RULE: nonparallel Walls");
            var s=new AfjUSection
            {
                Origin=WorldPoint(a, transform), Axis=WorldVector(axis, transform), WidthDirection=WorldVector(width, transform), DepthDirection=WorldVector(depth, transform), Width=Math.Sqrt(Dot(Sub(b, a), Sub(b, a))), Thickness=profile.Thickness, Radius=profile.Radius, NearHeight=Math.Sqrt(Dot(Sub(paths[0][0], a), Sub(paths[0][0], a))), FarHeight=Math.Sqrt(Dot(farWall, farWall)), MinWidth=0
            }
            ;
            s.MaxWidth=s.Width;
            s.MaxDepth=Math.Max(s.NearHeight, s.FarHeight);
            for(int side=0;side<2;side++)if(paths[side].Length==2)
            {
                double[] lip=Sub(paths[side][1], paths[side][0]);
                if(Math.Abs(Math.Abs(Dot(Unit(lip), width))-1)>1e-7)throw new InvalidOperationException("UNVERIFIED_RULE: inclined Return");
                double y=Dot(Sub(paths[side][1], a), width);
                if(side==0)
                {
                    s.NearReturn=true;
                    s.MinWidth=Math.Min(s.MinWidth, y);
                }
                else
                {
                    s.FarReturn=true;
                    s.MaxWidth=Math.Max(s.MaxWidth, y);
                }
                if(side==0&&y>1e-7||side==1&&y<s.Width-1e-7)throw new InvalidOperationException("UNVERIFIED_RULE: U miter inward Return has no qualified collision/coverage construction; no axial BUTT fallback");
            }
            s.MaxDepth=Math.Max(s.NearHeight, s.FarHeight+(s.FarReturn?s.Thickness:0));
            if(s.Width<2*(s.Thickness+s.Radius)||s.NearHeight<2*(s.Thickness+s.Radius)||s.FarHeight<2*(s.Thickness+s.Radius))throw new InvalidOperationException("UNVERIFIED_RULE: Bend zones overlap");
            return s;
        }
        static double[] ProfileCenter(AfjProfile p)
        {
            double[] web=WebLine(p);
            return Scale(Add(P(web, 0), P(web, 1)), .5);
        }
        internal static double[] Outgoing(AfjProfile p, Body2 body, MathTransform t, AfjProfile other, MathTransform ot)
        {
            double[] axis=Unit(WorldVector(p.Axis, t));
            double[] center=ProfileCenter(p), localAxis=Unit(p.Axis);
            // The base Sketch can be at a stock end, rather than its center.
            double middle=(AfjJointSolver.Projection(body, localAxis)-AfjJointSolver.Projection(body, Scale(localAxis, -1)))/2;
            center=Add(center, Scale(localAxis, middle-Dot(center, localAxis)));
            double[] o=WorldPoint(center, t), q=WorldPoint(ProfileCenter(other), ot);
            double distance=Dot(Sub(q, o), axis);
            if(Math.Abs(distance)<1e-4)throw new InvalidOperationException("UNVERIFIED_RULE: T/interior joint or ambiguous end");
            return Scale(axis, distance>0?1:-1);
        }
        internal static double[] Native(double[] p, MathTransform transform)
        {
            return WorldPoint(p, (MathTransform)transform.Inverse());
        }
        static double[] Common(double s, double q, double z, double[] o, double[] u, double[] v, double[] d)
        {
            return Add(o, Add(Scale(u, s), Add(Scale(v, q), Scale(d, z))));
        }
        static AfjToolDefinition Prism(List<double[]> polygon, double z0, double z1, MathTransform target, double[] o, double[] u, double[] v, double[] d)
        {
            double[][] points=polygon.Select(p=>Native(Common(p[0], p[1], z0, o, u, v, d), target)).ToArray();
            double[] normal=Unit(Sub(Native(Add(o, d), target), Native(o, target)));
            return new AfjToolDefinition
            {
                Polygon=points, Direction=normal, Length=z1-z0
            }
            ;
        }
        static AfjCutOperation Op(string id, AfjToolDefinition tool)
        {
            if (id.IndexOf("BEND", StringComparison.Ordinal) >= 0) tool.DepthPolicy = "BEND_NORMAL_RELIEF";
            var op=new AfjCutOperation
            {
                EvidenceId=id, NormalCutIntent=true, Kind=AfjOperationKind.Cut, RegionEvidence="Parametric Web tangent / Wall face / outer Return coverage derivation"
            }
            ;
            op.Tools.Add(tool);
            return op;
        }
        internal static double Extreme(Body2 body, MathTransform t, double[] worldDirection, double[] joint)
        {
            double[] n=Unit(Sub(Native(Add(joint, worldDirection), t), Native(joint, t)));
            double x, y, z;
            if(!body.GetExtremePoint(n[0], n[1], n[2], out x, out y, out z))throw new InvalidOperationException("UNVERIFIED_RULE: stock axial extent unavailable");
            return Dot(Sub(WorldPoint(new[]
            {
                x, y, z
            }
            , t), joint), worldDirection);
        }
        internal static Body2 IdealJointStock(ISldWorks app, AfjUSection section, MathTransform transform, double[] joint, double[] outgoing, double end)
        {
            double t=section.Thickness, r=section.Radius, w=section.Width, h=section.FarHeight;
            const double start=-.001;
            var points=new List<double[]>();
            var centers=new List<double[]>();
            Func<double, double, double[]> p=(y, z)=>Native(Add(joint, Add(Scale(outgoing, start), Add(Scale(section.WidthDirection, y), Scale(section.DepthDirection, z)))), transform);
            Action<double, double> point=(y, z)=>
            {
                points.Add(p(y, z));
                centers.Add(null);
            }
            ;
            Action<double, double, double, double> arc=(cy, cz, y, z)=>
            {
                centers[centers.Count-1]=p(cy, cz);
                point(y, z);
            }
            ;
            point(0, section.NearHeight);
            point(0, t+r);
            arc(t+r, t+r, t+r, 0);
            point(w-t-r, 0);
            arc(w-t-r, t+r, w, t+r);
            if(section.FarReturn)
            {
                point(w, h-r);
                arc(w+r, h-r, w+r, h);
                point(section.MaxWidth, h);
                point(section.MaxWidth, h+t);
                point(w+r, h+t);
                arc(w+r, h-r, w-t, h-r);
                point(w-t, t+r);
            }
            else
            {
                point(w, h);
                point(w-t, h);
                point(w-t, t+r);
            }
            arc(w-t-r, t+r, w-t-r, t);
            point(t+r, t);
            arc(t+r, t+r, t, t+r);
            point(t, section.NearHeight);
            double[] direction=Unit(Sub(Native(Add(joint, outgoing), transform), Native(joint, transform)));
            return AfjTemporaryBoolean.CreatePrism(app, new AfjToolDefinition
            {
                Polygon=points.ToArray(), ArcCenters=centers.ToArray(), Direction=direction, Length=end-start
            }
            );
        }
        internal static void VerifyCoverage(ISldWorks app, AfjParametricPlan generated, MathTransform ta, MathTransform tb, bool aCovers, AfjBooleanResult a, AfjBooleanResult b)
        {
            if (generated.Folded != null)
            { AfjFoldedButtRules.VerifyCoverage(app, generated, ta, tb, aCovers, a, b); return; }
            // Independent ideal formed stock is generated from the measured section.
            // Only the small joint band is checked. Existing holes in that band are
            // deliberately refused until a drawing-specific exception is verified.
            MathTransform tl=aCovers?tb:ta, tw=aCovers?ta:tb;
            Body2 il=IdealJointStock(app, generated.Loser, tl, generated.JointOrigin, generated.OutLoser, generated.Winner.MaxWidth+.001);
            Body2 iw=IdealJointStock(app, generated.Winner, tw, generated.JointOrigin, generated.OutWinner, generated.Loser.MaxWidth+.001);
            var expectedL=AfjTemporaryBoolean.Execute(app, il, aCovers?generated.Plan.B:generated.Plan.A);
            var expectedW=AfjTemporaryBoolean.Execute(app, iw, aCovers?generated.Plan.A:generated.Plan.B);
            AfjBooleanResult actualL=aCovers?b:a, actualW=aCovers?a:b;
            double missingL=AfjTemporaryBoolean.DifferenceVolume(expectedL.Kept[0], actualL.Kept[0]);
            double missingW=AfjTemporaryBoolean.DifferenceVolume(expectedW.Kept[0], actualW.Kept[0]);
            if(missingL>2e-12||missingW>2e-12)throw new InvalidOperationException("UNVERIFIED_RULE: REQUIRED_KEEP_MISSING; insufficient stock, prior incompatible trim or hole in joint coverage band; missing loser/cover mm3="+(missingL*1e9).ToString("R", System.Globalization.CultureInfo.InvariantCulture)+"/"+(missingW*1e9).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }
        static void VerifyPhysicalSkins(AfjUSection section, Body2 body, MathTransform transform)
        {
            var planes = new bool[6]; var bends = new bool[4];
            double t = section.Thickness, r = section.Radius;
            double[] expectedWidth = { 0, t, section.Width - t, section.Width };
            foreach (Face2 face in (Array)body.GetFaces())
            {
                Surface surface = face.GetSurface() as Surface;
                if (surface == null) continue;
                if (surface.IsPlane())
                {
                    double[] p = surface.PlaneParams as double[];
                    if (p == null || p.Length < 6) continue;
                    double[] normal = Unit(WorldVector(p.Take(3).ToArray(), transform));
                    double[] relative = Sub(WorldPoint(p.Skip(3).Take(3).ToArray(), transform), section.Origin);
                    if (Math.Abs(Dot(normal, section.DepthDirection)) > .999999)
                    {
                        double depth = Dot(relative, section.DepthDirection);
                        if (Math.Abs(depth) < 1e-7) planes[0] = true;
                        if (Math.Abs(depth - t) < 1e-7) planes[1] = true;
                    }
                    if (Math.Abs(Dot(normal, section.WidthDirection)) > .999999)
                    {
                        double width = Dot(relative, section.WidthDirection);
                        for (int i = 0; i < expectedWidth.Length; i++)
                            if (Math.Abs(width - expectedWidth[i]) < 1e-7) planes[2 + i] = true;
                    }
                }
                if (surface.IsCylinder())
                {
                    double[] p = surface.CylinderParams as double[];
                    if (p == null || p.Length < 7 || Math.Abs(Dot(Unit(WorldVector(p.Skip(3).Take(3).ToArray(), transform)), Unit(section.Axis))) < .999999) continue;
                    double[] center = Sub(WorldPoint(p.Take(3).ToArray(), transform), section.Origin);
                    double width = Dot(center, section.WidthDirection), depth = Dot(center, section.DepthDirection);
                    if (Math.Abs(depth - t - r) > 1e-7) continue;
                    int side = Math.Abs(width - t - r) < 1e-7 ? 0 : Math.Abs(width - section.Width + t + r) < 1e-7 ? 1 : -1;
                    int radius = Math.Abs(p[6] - r) < 1e-7 ? 0 : Math.Abs(p[6] - t - r) < 1e-7 ? 1 : -1;
                    if (side >= 0 && radius >= 0) bends[side * 2 + radius] = true;
                }
            }
            if (planes.Any(found => !found) || bends.Any(found => !found))
                throw new InvalidOperationException("UNVERIFIED_RULE: profile Sketch does not match measured Web/Wall/Bend B-rep skins");
            if (section.FarReturn)
                VerifyFarReturnSkins(section, body, transform);
            Trace.WriteLine("[AFJ-PROFILE] API/BREP skins verified: Web 0/t, Wall 0/t/W-t/W, four base Bend radius/center matches; t=" + t.ToString("R", CultureInfo.InvariantCulture) + " R=" + r.ToString("R", CultureInfo.InvariantCulture));
        }

        // Native base-Sketch connectivity selects the measurable U Web.
        // No bounding-box, name, image-derived dimensions or fold-count fallback.
        // This is a qualification boundary, not a claim that every U topology
        // already has a qualified material KEEP/CUT construction.
        private static void RequireMiterTopology(AfjProfile profile, string side)
        {
            double[][] chain = AfjFoldedButtRules.Ordered(profile);
            double[] web = WebLine(profile);
            int index = Array.FindIndex(chain, edge =>
                Same(P(edge, 0), P(web, 0)) && Same(P(edge, 1), P(web, 1)) ||
                Same(P(edge, 0), P(web, 1)) && Same(P(edge, 1), P(web, 0)));
            if (index < 0)
                throw new InvalidOperationException("UNVERIFIED_RULE: U miter Web missing from complete profile chain");
            int firstSide = index, secondSide = chain.Length - index - 1;
            Trace.WriteLine("[AFJ-R53] RULE_SELECTION frame=" + side +
                " family=U joint=ADJUSTED_BISECTOR strips=" + chain.Length +
                " side_strips=" + firstSide + "/" + secondSide +
                " automatic_butt=False");
            if (firstSide < 1 || secondSide < 1)
                throw new InvalidOperationException("UNVERIFIED_RULE: U miter requires a measured Wall on each Web end");
            if (firstSide > 2 || secondSide > 2)
                throw new InvalidOperationException("UNVERIFIED_RULE: U miter with multiple Return strips needs qualified per-strip KEEP/CUT; axial BUTT is not an allowed fallback");
        }

        private static void VerifyFarReturnSkins(AfjUSection section, Body2 body, MathTransform transform)
        {
            bool innerSkin = false, outerSkin = false, innerBend = false, outerBend = false;
            double expectedWidth = section.Width + section.Radius;
            double expectedDepth = section.FarHeight - section.Radius;
            foreach (Face2 face in (Array)body.GetFaces())
            {
                Surface surface = face.GetSurface() as Surface;
                if (surface == null) continue;
                if (surface.IsPlane())
                {
                    double[] parameters = surface.PlaneParams as double[];
                    if (parameters == null || parameters.Length < 6 || face.GetArea() <= 1e-14) continue;
                    double[] normal = Unit(WorldVector(parameters.Take(3).ToArray(), transform));
                    if (Math.Abs(Dot(normal, section.DepthDirection)) < .999999) continue;
                    double depth = Dot(Sub(WorldPoint(parameters.Skip(3).Take(3).ToArray(), transform), section.Origin), section.DepthDirection);
                    if (Math.Abs(depth - section.FarHeight) < 1e-7) innerSkin = true;
                    if (Math.Abs(depth - section.FarHeight - section.Thickness) < 1e-7) outerSkin = true;
                }
                if (surface.IsCylinder())
                {
                    double[] parameters = surface.CylinderParams as double[];
                    if (parameters == null || parameters.Length < 7 ||
                        Math.Abs(Dot(Unit(WorldVector(parameters.Skip(3).Take(3).ToArray(), transform)), Unit(section.Axis))) < .999999) continue;
                    double[] relative = Sub(WorldPoint(parameters.Take(3).ToArray(), transform), section.Origin);
                    if (Math.Abs(Dot(relative, section.WidthDirection) - expectedWidth) > 1e-7 ||
                        Math.Abs(Dot(relative, section.DepthDirection) - expectedDepth) > 1e-7) continue;
                    if (Math.Abs(parameters[6] - section.Radius) < 1e-7) innerBend = true;
                    if (Math.Abs(parameters[6] - section.Radius - section.Thickness) < 1e-7) outerBend = true;
                }
            }
            if (!innerSkin || !outerSkin || !innerBend || !outerBend)
                throw new InvalidOperationException("UNVERIFIED_RULE: outward U Return does not match measured planar skins and concentric Bend radii");
            Trace.WriteLine("[AFJ-R53] OUTWARD_RETURN_BREP_SKINS_QUALIFIED planes=2 bends=2");
        }

        internal static AfjParametricPlan Generate(AfjProfile profileA, AfjProfile profileB, Body2 bodyA, Body2 bodyB, MathTransform ta, MathTransform tb, bool aCovers, double webGap)
        {
            // AFJ-R53: more formed strips do not select another joint family.
            // A U Frame remains a U Frame. Never replace an unqualified miter
            // with an axial BUTT merely because a Return is present.
            SelectJointWebs(profileA,profileB,bodyA,bodyB,ta,tb);
            RequireMiterTopology(profileA, "A");
            RequireMiterTopology(profileB, "B");
            // The cover side is a manufacturing policy. It is not inferred from filenames,
            // fixture coordinates, stock length, A/B selection order or a universal KCC tag.
            AfjProfile pl=aCovers?profileB:profileA, pw=aCovers?profileA:profileB;
            Body2 bl=aCovers?bodyB:bodyA, bw=aCovers?bodyA:bodyB;
            MathTransform tl=aCovers?tb:ta, tw=aCovers?ta:tb;
            double[] ul=Outgoing(pl, bl, tl, pw, tw), uw=Outgoing(pw, bw, tw, pl, tl);
            if(Math.Abs(Dot(ul, uw))>1e-7)throw new InvalidOperationException("UNVERIFIED_RULE: only orthogonal axes have native evidence");
            AfjUSection l=Section(pl, tl, uw), w=Section(pw, tw, ul);
            // Each section is verified against its own thickness/radius before policy dispatch.
            if(Dot(l.WidthDirection, uw)<.999999||Dot(w.WidthDirection, ul)<.999999||Dot(l.DepthDirection, w.DepthDirection)<.999999)throw new InvalidOperationException("UNVERIFIED_RULE: incompatible folded skin orientation");
            VerifyPhysicalSkins(l, bl, tl);
            VerifyPhysicalSkins(w, bw, tw);
            // AFJ-R53: unequal t, R, width or formed-strip count never
            // authorize a square axial BUTT. Each skin was measured above;
            // the remaining miter checks either qualify this pair or refuse it.
            double[] d=Unit(l.DepthDirection), o=Add(l.Origin, Scale(ul, Dot(Sub(w.Origin, l.Origin), ul)));
            if(Math.Abs(Dot(Sub(w.Origin, o), d))>1e-7)throw new InvalidOperationException("UNVERIFIED_RULE: Web skins are not coplanar");
            if(l.NearReturn||w.NearReturn)throw new InvalidOperationException("UNVERIFIED_RULE: U miter near Return needs qualified collision/coverage construction; no axial BUTT fallback");
            double outerDepthL=l.FarHeight+(l.FarReturn?l.Thickness:0), outerDepthW=w.FarHeight+(w.FarReturn?w.Thickness:0);
            bool unequalHeights=Math.Abs(l.NearHeight-w.NearHeight)>1e-7||Math.Abs(outerDepthL-outerDepthW)>1e-7;
            if (unequalHeights)
            {
                // Extend the existing cover policy only where the covering
                // plain-U Walls reach all losing-wall cut edges. Return levels
                // and a lower covering Wall need a separate verified rule.
                if (l.FarReturn || w.FarReturn || w.NearHeight + 1e-7 < l.NearHeight ||
                    outerDepthW + 1e-7 < outerDepthL)
                    throw new InvalidOperationException("UNVERIFIED_RULE: U miter exposed-edge measurement or unequal Return-level KEEP/CUT is unqualified; lower cover does not select axial BUTT");
            }
            double sf=w.FarTangent, qf=l.FarTangent, sn=w.NearTangent, qn=qf-(sf-sn);
            bool unequalWidths=Math.Abs(l.Width-w.Width)>1e-7;
            if(unequalWidths)
            {
                // Preserve the geometric bisector through the two far Bend
                // tangencies. Wider cover moves the near endpoint along s;
                // wider loser moves it along q. Never stretch the seam slope
                // merely to keep the near endpoint at the old short-side datum.
                double tangentDelta=sf-qf;
                qn=Math.Max(l.NearTangent,qf-(sf-w.NearTangent));
                sn=qn+tangentDelta;
                Trace.WriteLine("[AFJ-R53] UNEQUAL_WIDTH_BISECTOR loser_width_m="+l.Width.ToString("R",CultureInfo.InvariantCulture)+" cover_width_m="+w.Width.ToString("R",CultureInfo.InvariantCulture)+" near_s_m="+sn.ToString("R",CultureInfo.InvariantCulture)+" near_q_m="+qn.ToString("R",CultureInfo.InvariantCulture)+" slope=1");
            }
            else if(qn<=l.NearTangent+1e-8)qn=l.Thickness;
            // Equal-width legacy inner-Wall clamping is preserved; unequal widths use the limiting native near tangent.
            if(qn>=qf-1e-6)throw new InvalidOperationException("UNVERIFIED_RULE: no positive Web seam span");
            double ds=(sf-sn)/(qf-qn), gapShift=webGap*Math.Sqrt(1+ds*ds);
            Func<double, double> seamS=q=>sn+(q-qn)*ds-gapShift;
            double m=Math.Max(.01, 5*Math.Max(l.Thickness+l.Radius, w.Thickness+w.Radius));
            double sMax=Math.Max(Extreme(bl, tl, ul, o), w.MaxWidth)+m, qMax=Math.Max(Extreme(bw, tw, uw, o), l.MaxWidth)+m;
            double qMin=l.MinWidth-m, sMin=w.MinWidth-m, near=l.NearTangent, far=l.FarTangent;
            double sStart=Math.Max(w.Thickness, seamS(near)), qHit=qn+(w.Thickness+gapShift-sn)/ds;
            var loserBoundary=new List<double[]>
            {
                new[]
                {
                    -AssemblyGap, qMin
                }
                , new[]
                {
                    -AssemblyGap, near
                }
                , new[]
                {
                    sStart, near
                }
            }
            ;
            if(qHit>near+1e-8&&qHit<far-1e-8)loserBoundary.Add(new[]
            {
                w.Thickness, qHit
            }
            );
            loserBoundary.Add(new[]
            {
                seamS(far), far
            }
            );
            double farWall=w.Width-w.Thickness-AssemblyGap;
            if(Math.Abs(farWall-seamS(far))>1e-9)loserBoundary.Add(new[]
            {
                farWall, far
            }
            );
            if(l.FarReturn)
            {
                loserBoundary.Add(new[]
                {
                    farWall, l.Width+AssemblyGap
                }
                );
                loserBoundary.Add(new[]
                {
                    w.MaxWidth, l.Width+AssemblyGap
                }
                );
                loserBoundary.Add(new[]
                {
                    w.MaxWidth, l.MaxWidth+m
                }
                );
            }
            else loserBoundary.Add(new[]
            {
                farWall, l.MaxWidth+m
            }
            );
            loserBoundary.Add(new[]
            {
                sMax, l.MaxWidth+m
            }
            );
            loserBoundary.Add(new[]
            {
                sMax, qMin
            }
            );
            var winnerBoundary=new List<double[]>
            {
                new[]
                {
                    sMin, l.Thickness
                }
                , new[]
                {
                    sn, l.Thickness
                }
            }
            ;
            if(qn>l.Thickness+1e-9)winnerBoundary.Add(new[]
            {
                sn, qn
            }
            );
            winnerBoundary.Add(new[]
            {
                sf, qf
            }
            );
            winnerBoundary.Add(new[]
            {
                w.Width-w.Thickness, qf
            }
            );
            winnerBoundary.Add(new[]
            {
                w.Width-w.Thickness, l.Width
            }
            );
            winnerBoundary.Add(new[]
            {
                w.MaxWidth+m, l.Width
            }
            );
            winnerBoundary.Add(new[]
            {
                w.MaxWidth+m, qMax
            }
            );
            winnerBoundary.Add(new[]
            {
                sMin, qMax
            }
            );
            var lp=new AfjCutPlan();
            var wp=new AfjCutPlan();
            lp.Operations.Add(Op("U_RULE:LOSER_STEPPED_WEB_WALL_RETURN", Prism(loserBoundary, -ToolOverrun, l.MaxDepth+ToolOverrun, tl, o, ul, uw, d)));
            wp.Operations.Add(Op("U_RULE:COVER_WEB_WALL", Prism(winnerBoundary, -ToolOverrun, w.MaxDepth+ToolOverrun, tw, o, ul, uw, d)));
            // NormalCut correction follows the actual inner Wall plane and the Bend
            // tangent depth. No copied relief B-rep, saved box or fixture coordinates.
            var cap=new List<double[]>
            {
                new[]
                {
                    w.Width-w.Thickness, qf
                }
                , new[]
                {
                    w.MaxWidth+m, qf
                }
                , new[]
                {
                    w.MaxWidth+m, l.Width
                }
                , new[]
                {
                    w.Width-w.Thickness, l.Width
                }
            }
            ;
            wp.Operations.Add(Op("U_RULE:NORMAL_BEND_CAP", Prism(cap, -ToolOverrun, w.Thickness+w.Radius, tw, o, ul, uw, d)));
            if(webGap>0)
            {
                // The seam endpoints are contacts between curved Bend zones. Offsetting
                // only the straight Web line leaves a tangent-point contact at each end.
                // Clip those end caps on the losing member through both Bend tangent
                // depths; deeper Wall faces keep their independently defined 0.1 mm gap.
                double capDepth=Math.Max(l.Thickness+l.Radius, w.Thickness+w.Radius)+webGap;
                var farGuard=new List<double[]>
                {
                    new[]
                    {
                        sf-gapShift, qf-webGap
                    }
                    , new[]
                    {
                        sMax, qf-webGap
                    }
                    , new[]
                    {
                        sMax, l.MaxWidth+m
                    }
                    , new[]
                    {
                        sf-gapShift, l.MaxWidth+m
                    }
                }
                ;
                var nearGuard=new List<double[]>
                {
                    new[]
                    {
                        -AssemblyGap, qMin
                    }
                    , new[]
                    {
                        sMax, qMin
                    }
                    , new[]
                    {
                        sMax, l.Thickness+webGap
                    }
                    , new[]
                    {
                        -AssemblyGap, l.Thickness+webGap
                    }
                }
                ;
                lp.Operations.Add(Op("U_RULE:FAR_BEND_CLEARANCE", Prism(farGuard, -ToolOverrun, capDepth, tl, o, ul, uw, d)));
                lp.Operations.Add(Op("U_RULE:NEAR_BEND_CLEARANCE", Prism(nearGuard, -ToolOverrun, capDepth, tl, o, ul, uw, d)));
            }
            var result=new AfjParametricPlan
            {
                Plan=new AfjJointPlan
                {
                    RuleId=unequalHeights?"ORTHOGONAL_U_TALLER_COVER":"ORTHOGONAL_U_COVER", A=aCovers?wp:lp, B=aCovers?lp:wp
                }
                , Loser=l, Winner=w, JointOrigin=o, OutLoser=ul, OutWinner=uw, Depth=d, SeamNear=new[]
                {
                    sn, qn
                }
                , SeamFar=new[]
                {
                    sf, qf
                }
                , Derivation=(unequalWidths?"Unequal-width geometric bisector through far Bend tangencies; near endpoint follows actual near tangent of the limiting Web; ":"Common seam from Bend tangencies; near seam constrained to inner Wall; ")+"Wall assembly gap=0.1 mm; outer Return coverage; analytic NormalCut cap; web gap="+webGap.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            }
            ;
            return result;
        }
    }
}

namespace ADDIN.Commands.AutoFrameJoint
{
    internal sealed class AfjBooleanException : InvalidOperationException
    {
        internal readonly int OperationCode;
        internal readonly string OperationName, ErrorName;
        internal AfjBooleanException(string operation, int code, string name)
            : base("AFJ: Boolean " + operation + " code=" + code + " name=" + name)
        { OperationName = operation; OperationCode = code; ErrorName = name; }
    }

    internal static class AfjManufacturingConventions
    {
        // Independent manufacturing meanings even when the numeric values agree.
        internal const double SheetCutOverrun = .0001;
        internal const double AssemblyFitGap = .0001;
        internal const double CutterEnvelopeOverrun = .0001;
        internal const double CutterEntryClearance = .00001;
    }

    internal static class AfjSheetCutTools
    {
        // This factory is for a verified single planar sheet, not an entire
        // formed profile. The caller supplies API thickness and both B-rep
        // skins. NormalCut across a Bend needs a separate verified relief plan.
        internal static AfjCutOperation BlindBetweenSkins(string evidenceId,
            double[][] boundaryOnOuterSkin, double[] outerSkin, double[] innerSkin,
            double[] intoSheet, double apiThickness, bool normalCut, bool crossesBend)
        {
            if (crossesBend)
                throw new InvalidOperationException("UNVERIFIED_RULE: single-sheet Blind cannot replace NormalCut Bend relief");
            if (String.IsNullOrEmpty(evidenceId) || boundaryOnOuterSkin == null ||
                boundaryOnOuterSkin.Length < 3 || !AfjTemporaryBoolean.Finite(apiThickness) || apiThickness <= 0)
                throw new InvalidOperationException("UNVERIFIED_RULE: missing single-sheet API/skin evidence");
            double[] n = AfjParametricRules.Unit(intoSheet);
            double[] delta = AfjParametricRules.Sub(innerSkin, outerSkin);
            double separation = AfjParametricRules.Dot(delta, n);
            if (Math.Abs(separation - apiThickness) > 1e-7 ||
                Math.Sqrt(AfjParametricRules.Dot(AfjParametricRules.Sub(delta, AfjParametricRules.Scale(n, separation)),
                    AfjParametricRules.Sub(delta, AfjParametricRules.Scale(n, separation)))) > 1e-7)
                throw new InvalidOperationException("UNVERIFIED_RULE: sheet skin direction/separation does not match API thickness");
            foreach (double[] point in boundaryOnOuterSkin)
                if (Math.Abs(AfjParametricRules.Dot(AfjParametricRules.Sub(point, outerSkin), n)) > 1e-7)
                    throw new InvalidOperationException("UNVERIFIED_RULE: sheet tool boundary is not on the verified outer skin");
            double nominal = apiThickness + AfjManufacturingConventions.SheetCutOverrun;
            var spec = new AfjToolDefinition {
                Polygon = boundaryOnOuterSkin.Select(point => AfjParametricRules.Sub(point,
                    AfjParametricRules.Scale(n, AfjManufacturingConventions.CutterEntryClearance))).ToArray(),
                Direction = n, Length = nominal + AfjManufacturingConventions.CutterEntryClearance,
                NominalSheetDepth = nominal, ApiSheetThickness = apiThickness,
                DepthPolicy = "SINGLE_SHEET_BLIND_T_PLUS_OVERRUN"
            };
            var result = new AfjCutOperation { EvidenceId = evidenceId, Kind = AfjOperationKind.Cut,
                RegionEvidence = "API thickness + two B-rep skins; single planar layer",
                NormalCutIntent = normalCut };
            result.Tools.Add(spec);
            return result;
        }
    }

}

namespace ADDIN.Commands.AutoFrameJoint
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using SolidWorks.Interop.sldworks;
    using SolidWorks.Interop.swconst;
    internal sealed class AfjApplyTicket
    {
        internal Component2 FrameA, FrameB;
        internal AfjJointSolution Preview;
        internal Body2 BeforeA, BeforeB;
        internal Func<bool> IsSourceCurrent;
        internal bool Consumed;
    }
    internal sealed class AfjNativeCutEvidence
    {
        public string Frame, EvidenceId, FeatureName, FeatureType, UnderlyingType, DepthPolicy;
        public int EndCondition;
        public bool NormalCut, FlipSideToCut, ReverseDirection;
        public double DepthMetres, RemovedCubicMetres, SymmetricDifferenceCubicMetres;
        public string NativeRepresentation = "FIXED_PLANE_PRISM";
        public bool EndFaceSketchEvidenceAvailable, EndFaceSketchLaterFeaturesVerified;
        public double PlanCutterLengthMetres, NativeActualDepthMetres;
        public string SketchName, SketchSupport;
        public int SketchReferenceType = -1, SketchConstrainedStatus = -1, SketchRelationCount = -1;
        public int SketchDrivingDimensions = -1, NewReferencePlanes = -1;
        public int NativeEndCapFaces, NativeEndBoundaryEdges, NativeEndArcs, NativeVertexRelations, NativeTangentRelations, NativeClosureRelations;
        public string SketchReferenceQualification;
        public int SketchObservedReferenceType = -1, SketchSupportOwnerId = -1, SketchFacePersistResolveState = -1;
        public bool SketchObservedReferenceIsFace, SketchSupportOwnerInParents, SketchPlaneVerified;

    }
    internal sealed class AfjNativeApplyReport
    {
        public string Status = "NOT_STARTED", Error;
        public bool RollbackVerified, SaveApiCalled = false;
        public double GapMetres, InterferenceCubicMetres;
        public readonly List<AfjNativeCutEvidence> Cuts = new List<AfjNativeCutEvidence>();
        public readonly List<string> Rollback = new List<string>();
    }

    /// <summary>
    /// Native Cut-Extrude from an already reviewed temporary plan. No Save,
    /// imported replacement body, assembly feature, build or deployment.
    /// The caller must run after the native PropertyManager has closed.
    /// </summary>
    internal static class AfjNativeCutCommit
    {
        // R53 routes supported U intent to the existing-face writer. Unsupported
        // topology/constraint recipes are rejected by ValidateIntent BEFORE mutation.
        // This is source integration; it does not prove the installed DLL was rebuilt.
        private static bool ConstrainedURouteQualified { get { return true; } }

        private sealed class PartEdit
        {
            internal ModelDoc2 Part;
            internal Body2 Before;
            internal string[] Tree, AfterTree;
            internal int AfterStamp;
            internal bool SheetMetal, Recording, GroupFinished, Started;
            internal string Prefix;
            internal readonly HashSet<int> OwnedIds = new HashSet<int>();
            internal readonly List<AfjOwnedEndSketch> EndFaceSketches = new List<AfjOwnedEndSketch>();
        }

        private static double[] Cross(double[] a, double[] b)
        { return new[] { a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0] }; }
        internal static bool ReverseForAlignment(double alignment)
        {
            if (!AfjTemporaryBoolean.Finite(alignment) || Math.Abs(Math.Abs(alignment)-1)>1e-7)
                throw new InvalidOperationException("APPLY: cut axis is not normal to native sketch");
            return alignment>0; // Native cut defaults to -sketch normal.
        }
        internal static bool UseNativeNormalCut(AfjCutOperation operation, AfjToolDefinition tool)
        { return tool.DepthPolicy=="SINGLE_SHEET_BLIND_T_PLUS_OVERRUN" && operation.NormalCutIntent; }
        private static double[] Point(ISldWorks app, double[] point, MathTransform transform)
        {
            var utility = app.GetMathUtility() as MathUtility;
            var p = utility.CreatePoint(point) as MathPoint;
            var transformed = p.MultiplyTransform(transform) as MathPoint;
            var result = transformed.ArrayData as double[];
            if (result == null || result.Length != 3 || result.Any(x => !AfjTemporaryBoolean.Finite(x)))
                throw new InvalidOperationException("APPLY: invalid sketch transform");
            return result;
        }
        private static bool Same(ISldWorks app, object a, object b)
        { return a != null && b != null && app.IsSame(a, b) == (int)swObjectEquality.swObjectSame; }
        internal static bool SameReferencePath(string a,string b)
        {
            if(String.IsNullOrWhiteSpace(a)||String.IsNullOrWhiteSpace(b)||!System.IO.Path.IsPathRooted(a)||!System.IO.Path.IsPathRooted(b))
                throw new InvalidOperationException("APPLY: absolute component reference path required");
            return String.Equals(System.IO.Path.GetFullPath(a),System.IO.Path.GetFullPath(b),StringComparison.OrdinalIgnoreCase);
        }
        private static void Activate(ISldWorks app, ModelDoc2 part)
        {
            if (Same(app, app.ActiveDoc, part)) return;
            int errors = 0;
            var active = app.ActivateDoc3(part.GetTitle(), false,
                (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref errors) as ModelDoc2;
            Trace.WriteLine("[AFJ-APPLY] ACTIVATE status="+errors+" identity="+Same(app,active,part));
            if ((errors & ~(int)swActivateDocError_e.swDocNeedsRebuildWarning) != 0 || !Same(app, active, part))
                throw new InvalidOperationException("APPLY: cannot activate the bound document, error=" + errors);
        }
        internal static Body2 CurrentBody(ModelDoc2 part)
        {
            var p = part as PartDoc;
            Array bodies = p == null ? null : p.GetBodies2((int)swBodyType_e.swSolidBody, false) as Array;
            if (bodies == null || bodies.Length != 1)
                throw new InvalidOperationException("APPLY: exactly one current solid is required");
            var native = bodies.GetValue(0) as Body2;
            AfjTemporaryBoolean.Validate(AfjTemporaryBoolean.Copy(native));
            return native;
        }
        private static string[] Tree(ModelDoc2 part)
        {
            var rows = new List<string>();
            for (Feature f = part.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature)
            {
                if (rows.Count > 10000) throw new InvalidOperationException("APPLY: feature traversal exceeded limit");
                rows.Add(f.GetTypeName2() + "|" + f.Name);
            }
            return rows.ToArray();
        }
        internal static double SymmetricDifference(Body2 a, Body2 b)
        { return AfjTemporaryBoolean.DifferenceVolume(a, b) + AfjTemporaryBoolean.DifferenceVolume(b, a); }
        private static double VerifyBody(Body2 current, Body2 expected)
        {
            Body2 actual = AfjTemporaryBoolean.Copy(current);
            AfjTemporaryBoolean.Validate(actual);
            double tolerance = Math.Max(1e-12, AfjTemporaryBoolean.Volume(expected) * 1e-8);
            double difference = SymmetricDifference(actual, expected);
            if (!AfjTemporaryBoolean.Finite(difference) || difference > tolerance)
                throw new InvalidOperationException("APPLY: native Cut differs from reviewed B-rep, delta_m3=" + difference.ToString("R", CultureInfo.InvariantCulture));
            return difference;
        }
        private static void PreflightPart(ISldWorks app, ModelDoc2 assembly, Component2 component, ModelDoc2 part)
        {
            if (part == null || part.GetType() != (int)swDocumentTypes_e.swDocPART || part.IsOpenedReadOnly() || part.IsOpenedViewOnly())
                throw new InvalidOperationException("APPLY_READ_ONLY: writable resolved Part required");
            if (part.GetActiveSketch2() != null)
                throw new InvalidOperationException("APPLY: finish the active sketch before applying cuts");
            var cfg = part.ConfigurationManager.ActiveConfiguration;
            if (cfg == null || cfg.Name != component.ReferencedConfiguration)
                throw new InvalidOperationException("APPLY: occurrence/configuration changed");
            Array names = part.GetConfigurationNames() as Array;
            if (names == null) throw new InvalidOperationException("APPLY: configuration inventory unavailable");
            foreach (object name in names)
            {
                var other = part.GetConfigurationByName((string)name) as Configuration;
                if (other == null || (!other.IsDerived() && other.Name != cfg.Name))
                    throw new InvalidOperationException("UNVERIFIED_RULE: multiple independent Part configurations need an explicit cut scope");
            }
            var asm = assembly as AssemblyDoc;
            if (asm == null || !Same(app, asm.GetEditTarget(), assembly))
                throw new InvalidOperationException("APPLY: exit in-context Part edit before applying");
            Array components = asm.GetComponents(false) as Array;
            if (components == null) throw new InvalidOperationException("APPLY: component inventory unavailable");
            int uses=0;
            string targetPath=part.GetPathName();
            foreach(Component2 c in components.Cast<object>().OfType<Component2>())
            {
                var referenced=c.GetModelDoc2() as ModelDoc2;
                string path=c.GetPathName();
                bool samePath=SameReferencePath(path,targetPath);
                if(referenced==null&&String.Equals(System.IO.Path.GetExtension(path),".SLDASM",StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("SHARED_PART_UNVERIFIED: resolve subassemblies before qualifying Part reference scope");
                bool ambiguous=referenced==null&&String.Equals(System.IO.Path.GetFileName(path),System.IO.Path.GetFileName(targetPath),StringComparison.OrdinalIgnoreCase);
                if(Same(app,referenced,part)||samePath||ambiguous)uses++;
            }
            if (uses != 1)
                throw new InvalidOperationException("SHARED_PART: selected Part has multiple occurrences; make the target independent before applying");
        }
        private static Feature FindSpecific(ISldWorks app, ModelDoc2 part, object specific)
        {
            for (Feature f = part.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature)
            {
                if (Same(app, f, specific) || Same(app, f.GetSpecificFeature2(), specific)) return f;
            }
            throw new InvalidOperationException("APPLY: created plane/sketch is not in the FeatureManager");
        }
        private static Feature Own(PartEdit edit, Feature feature, string suffix)
        {
            if (feature == null) throw new InvalidOperationException("APPLY: feature creation returned null");
            edit.OwnedIds.Add(feature.GetID());
            string name = edit.Prefix + suffix;
            feature.Name = name;
            if (feature.Name != name) throw new InvalidOperationException("APPLY: cannot name the owned feature");
            return feature;
        }
        private static void EndSketch(ISldWorks app, ModelDoc2 part, Sketch sketch)
        {
            if (Same(app, part.GetActiveSketch2(), sketch)) part.SketchManager.InsertSketch(true);
        }
        // AFJ-END-FACE-WRITER-20261022

        private sealed class AfjOwnedEndSketch
        {
            internal Feature Feature;
            internal Sketch Sketch;
            internal int ReferenceType;
            internal AfjEndFaceSectionEvidence Evidence;
            internal Face2 OriginalFace;
            internal byte[] FaceToken,OwnerToken;
            internal int OwnerId;
            internal string OwnerName,OwnerType;
            internal double[] Normal;
            internal double Support;
        }
        private static byte[] EndPersistToken(ModelDoc2 part,object entity)
        {
            object raw=part.Extension.GetPersistReference3(entity);
            byte[] bytes=raw as byte[];
            if(bytes!=null)return bytes;
            Array values=raw as Array;
            return values==null?null:values.Cast<object>().Select(Convert.ToByte).ToArray();
        }
        private static object EndResolve(ModelDoc2 part,byte[] token,out int state)
        {
            state=-1;
            if(token==null||token.Length==0)return null;
            return part.Extension.GetObjectByPersistReference3(token,out state);
        }
        private static bool EndSketchPlane(ISldWorks app,AfjOwnedEndSketch owned,out double support,out double alignment)
        {
            MathTransform inverse=owned.Sketch.ModelToSketchTransform.Inverse() as MathTransform;
            if(inverse==null){support=Double.NaN;alignment=Double.NaN;return false;}
            double[] o=Point(app,new double[3],inverse);
            double[] normal=AfjParametricRules.Unit(AfjParametricRules.Sub(Point(app,new[]{0d,0d,1d},inverse),o));
            alignment=AfjParametricRules.Dot(normal,owned.Normal);support=AfjParametricRules.Dot(o,owned.Normal);
            return Math.Abs(Math.Abs(alignment)-1)<1e-7&&Math.Abs(support-owned.Support)<1e-8;
        }
        private static bool EndReferenceFacePlane(Face2 face,AfjOwnedEndSketch owned,out bool readable,out int ownerId,out double support)
        {
            readable=false;ownerId=-1;support=Double.NaN;
            if(face==null)return false;
            Surface surface=face.GetSurface() as Surface;
            if(surface==null||!surface.IsPlane()){readable=true;return false;}
            double[] p=surface.PlaneParams as double[];
            if(p==null||p.Length!=6)return false;
            readable=true;support=AfjParametricRules.Dot(p.Skip(3).Take(3).ToArray(),owned.Normal);
            Feature owner=face.GetFeature() as Feature;if(owner!=null)ownerId=owner.GetID();
            return Math.Abs(Math.Abs(AfjParametricRules.Dot(AfjParametricRules.Unit(p.Take(3).ToArray()),owned.Normal))-1)<1e-7&&
                Math.Abs(support-owned.Support)<1e-8&&ownerId==owned.OwnerId;
        }
        private static AfjOwnedEndSketch CaptureEndSketch(ISldWorks app,PartEdit edit,Feature profile,Sketch sketch,Face2 face,
                                                        double[] normal,double support,AfjEndFaceSectionEvidence evidence)
        {
            Feature owner=face.GetFeature() as Feature;
            if(owner==null||edit.OwnedIds.Contains(owner.GetID()))
                throw new InvalidOperationException("END_SECTION: original support Face feature ownership unavailable");
            var owned=new AfjOwnedEndSketch{Feature=profile,Sketch=sketch,ReferenceType=evidence.SketchReferenceType,Evidence=evidence,
                OriginalFace=face,FaceToken=EndPersistToken(edit.Part,face),OwnerToken=EndPersistToken(edit.Part,owner),
                OwnerId=owner.GetID(),OwnerName=owner.Name,OwnerType=owner.GetTypeName2(),Normal=normal,Support=support};
            Trace.WriteLine("[AFJ-END-REF] capture sketch="+profile.Name+" owner_id="+owned.OwnerId+" owner_type="+owned.OwnerType+
                " owner="+owned.OwnerName+" face_persist_token_length="+(owned.FaceToken==null?-1:owned.FaceToken.Length)+
                " owner_persist_token_length="+(owned.OwnerToken==null?-1:owned.OwnerToken.Length)+
                " support="+support.ToString("R",CultureInfo.InvariantCulture));
            return owned;
        }
        private static void VerifyEndFaceSketches(ISldWorks app,PartEdit edit,string stage)
        {
            foreach(var owned in edit.EndFaceSketches)
            {
                if(owned.Feature==null||owned.Sketch==null||!edit.OwnedIds.Contains(owned.Feature.GetID())||
                   !Same(app,owned.Feature.GetSpecificFeature2(),owned.Sketch))
                    throw new InvalidOperationException("END_SECTION: owned sketch identity missing at "+stage);
                int referenceType=0;object reference=owned.Sketch.GetReferenceEntity(ref referenceType);
                Face2 observedFace=reference as Face2;
                int status=owned.Sketch.GetConstrainedStatus();
                int dangling=owned.Sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swDangling);
                int over=owned.Sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swOverDefining);
                int dimensions=0;
                for(DisplayDimension d=owned.Feature.GetFirstDisplayDimension() as DisplayDimension;d!=null;d=owned.Feature.GetNextDisplayDimension(d) as DisplayDimension)
                {if(++dimensions>256)throw new InvalidOperationException("END_SECTION: sketch dimension traversal exceeded limit");}
                Array rawParents=owned.Feature.GetParents() as Array;
                Feature[] parents=rawParents==null?new Feature[0]:rawParents.Cast<object>().OfType<Feature>().ToArray();
                bool ownerParent=parents.Any(f=>f.GetID()==owned.OwnerId&&f.GetTypeName2()==owned.OwnerType);
                bool ownerInTree=false;
                for(Feature f=edit.Part.FirstFeature() as Feature;f!=null;f=f.GetNextFeature() as Feature)
                    if(f.GetID()==owned.OwnerId&&f.GetTypeName2()==owned.OwnerType)ownerInTree=true;
                int faceState=-1,ownerState=-1;
                object resolvedFace=null,resolvedOwner=null;
                string resolutionError="";
                try{resolvedFace=EndResolve(edit.Part,owned.FaceToken,out faceState);resolvedOwner=EndResolve(edit.Part,owned.OwnerToken,out ownerState);}
                catch(Exception ex){resolutionError="HRESULT=0x"+ex.HResult.ToString("X8")+" "+ex.Message;}
                bool ownerTokenVerified=resolvedOwner is Feature&&ownerState==0&&((Feature)resolvedOwner).GetID()==owned.OwnerId;
                bool observedReadable=false,persistReadable=false;
                int observedOwner=-1,persistOwner=-1;double observedSupport=Double.NaN,persistSupport=Double.NaN;
                bool observedPlane=false,persistPlane=false;
                string faceError="";
                try{observedPlane=EndReferenceFacePlane(observedFace,owned,out observedReadable,out observedOwner,out observedSupport);}
                catch(Exception ex){faceError="observed HRESULT=0x"+ex.HResult.ToString("X8")+" "+ex.Message;}
                try{persistPlane=faceState==0&&EndReferenceFacePlane(resolvedFace as Face2,owned,out persistReadable,out persistOwner,out persistSupport);}
                catch(Exception ex){faceError+=" persist HRESULT=0x"+ex.HResult.ToString("X8")+" "+ex.Message;}
                double sketchSupport,sketchAlignment;bool sketchPlane=EndSketchPlane(app,owned,out sketchSupport,out sketchAlignment);
                int observedOriginalEquality=-1,observedPersistEquality=-1;
                if(reference!=null&&owned.OriginalFace!=null)
                    try{observedOriginalEquality=app.IsSame(reference,owned.OriginalFace);}catch(Exception ex){faceError+=" same_original="+ex.Message;}
                if(reference!=null&&resolvedFace!=null)
                    try{observedPersistEquality=app.IsSame(reference,resolvedFace);}catch(Exception ex){faceError+=" same_persist="+ex.Message;}
                string qualification="UNVERIFIED";
                // A readable current reference on a DIFFERENT plane/owner is a
                // mismatch, never grounds for the historical-face fallback.
                bool explicitMismatch=observedReadable&&!observedPlane;
                bool historyProof=owned.Evidence.SketchReferenceVerified&&owned.Evidence.PostCloseVerified&&sketchPlane&&
                    ownerParent&&ownerInTree&&(owned.OwnerToken==null||owned.OwnerToken.Length==0||ownerTokenVerified);
                if(!explicitMismatch&&historyProof)
                {
                    if(observedPlane)qualification="NATIVE_REFERENCE_FACE_PLANE_OWNER_PARENT";
                    else if(persistPlane)qualification="PERSISTENT_FACE_PLANE_OWNER_PARENT";
                    else if(reference==null||(!observedReadable&&!(reference is RefPlane)))
                        qualification="HISTORY_FACE_PARENT_AND_UNCHANGED_SKETCH_PLANE";
                }
                string parentsText=String.Join(";",parents.Select(f=>f.GetID()+":"+f.GetTypeName2()+":"+f.Name));
                Trace.WriteLine("[AFJ-END-REF] stage="+stage+" sketch="+owned.Feature.Name+" expected_ref_type="+owned.ReferenceType+
                    " actual_ref_type="+referenceType+" runtime="+(reference==null?"NULL":reference.GetType().FullName)+
                    " is_face="+(reference is Face2)+" is_feature="+(reference is Feature)+" is_ref_plane="+(reference is RefPlane)+
                    " same_original="+observedOriginalEquality+" same_persist="+observedPersistEquality+
                    " original_owner="+owned.OwnerId+" owner_parent="+ownerParent+" owner_tree="+ownerInTree+" parents="+parentsText+
                    " face_persist_state="+faceState+" owner_persist_state="+ownerState+" owner_token_verified="+ownerTokenVerified+
                    " observed_plane="+observedPlane+" observed_owner="+observedOwner+" observed_support="+observedSupport.ToString("R",CultureInfo.InvariantCulture)+
                    " persist_plane="+persistPlane+" persist_owner="+persistOwner+" persist_support="+persistSupport.ToString("R",CultureInfo.InvariantCulture)+
                    " sketch_plane="+sketchPlane+" sketch_support="+sketchSupport.ToString("R",CultureInfo.InvariantCulture)+
                    " sketch_alignment="+sketchAlignment.ToString("R",CultureInfo.InvariantCulture)+" qualification="+qualification+
                    " status="+status+" dangling="+dangling+" over="+over+" dimensions="+dimensions+
                    " resolve_error="+resolutionError+" face_error="+faceError);
                owned.Evidence.ReferenceQualification=qualification;owned.Evidence.ObservedReferenceType=referenceType;
                owned.Evidence.ObservedReferenceIsFace=reference is Face2;owned.Evidence.OwnerFeatureId=owned.OwnerId;
                owned.Evidence.OwnerInParents=ownerParent;owned.Evidence.FacePersistState=faceState;owned.Evidence.SketchPlaneVerified=sketchPlane;
                if(qualification=="UNVERIFIED"||status!=(int)swConstrainedStatus_e.swFullyConstrained||
                   dangling!=0||over!=0||dimensions!=0)
                    throw new InvalidOperationException("END_SECTION_UNVERIFIED: native Frame references/constraints failed after "+stage+
                        "; qualification="+qualification+" expected_ref_type="+owned.ReferenceType+" actual_ref_type="+referenceType+
                        " is_face="+(reference is Face2)+" owner_parent="+ownerParent+" sketch_plane="+sketchPlane+
                        " status="+status+" dangling="+dangling+" over="+over+" dimensions="+dimensions);
                owned.Evidence.SketchConstrainedStatus=status;
                owned.Evidence.SketchRelationCount=owned.Sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swAll);
                owned.Evidence.DrivingDimensions=dimensions;
                owned.Evidence.LaterFeaturesVerified=true;
                Trace.WriteLine("[AFJ-END-FACE] stage="+stage+" sketch="+owned.Feature.Name+" status="+status+
                    " dangling="+dangling+" over="+over+" sketch_dimensions="+dimensions+" reference_qualification="+qualification);
            }
        }
// Native end-section writer for the qualified folded axial-butt rule.
// Creates a Sketch on the original end Face with direct geometric relations.
// Per-step/final B-rep, constraint and rollback checks remain mandatory.
private sealed class AfjEndFaceSectionEvidence
{
    internal string Representation="FACE_ATTACHED_NATIVE_END_SECTION";
    internal string SketchName,SketchSupport="EXISTING_SELECTED_END_FACE";
    internal string ReferenceQualification;
    internal int ObservedReferenceType,OwnerFeatureId,FacePersistState;
    internal bool ObservedReferenceIsFace,OwnerInParents,SketchPlaneVerified;
    internal double PlanCutterLengthMetres,ActualBlindDepthMetres,EndSupport,CutSupport;
    internal int CapFaceCount,BoundaryEdgeCount,ArcCount,VertexRelations,TangentRelations,ClosureRelations;
    internal int SketchReferenceType,SketchConstrainedStatus,SketchRelationCount,NewPlanes,DrivingDimensions=0;
    internal bool SketchReferenceVerified,PostCloseVerified,LaterFeaturesVerified;
}
private sealed class AfjEndBoundaryEdge
{
    internal Edge Native;
    internal Vertex StartVertex,EndVertex;
    internal double[] A,B,Center;
    internal double Radius;
    internal int From,To;
    internal bool Arc;
}
private static double EndDot(double[] a,double[] b){return AfjParametricRules.Dot(a,b);}
private static double EndDistance(double[] a,double[] b)
{return Math.Sqrt(EndDot(AfjParametricRules.Sub(a,b),AfjParametricRules.Sub(a,b)));}
private static int EndPlaneCount(ModelDoc2 part)
{int n=0;for(Feature f=part.FirstFeature() as Feature;f!=null;f=f.GetNextFeature() as Feature)if(f.GetTypeName2()=="RefPlane")n++;return n;}
private static void EndRelation(Sketch sketch,int kind,params object[] entities)
{
    var wrapped=entities.Select(x=>new System.Runtime.InteropServices.DispatchWrapper(x)).ToArray();
    if(sketch.RelationManager.AddRelation(wrapped,kind)==null)
        throw new InvalidOperationException("END_SECTION: native geometric relation failed, kind="+kind);
}
private static SketchPoint EndSegmentStart(SketchSegment segment)
{
    SketchLine line=segment as SketchLine;if(line!=null)return line.GetStartPoint2() as SketchPoint;
    SketchArc arc=segment as SketchArc;if(arc!=null)return arc.GetStartPoint2() as SketchPoint;
    throw new InvalidOperationException("END_SECTION: unsupported native sketch segment");
}
private static SketchPoint EndSegmentEnd(SketchSegment segment)
{
    SketchLine line=segment as SketchLine;if(line!=null)return line.GetEndPoint2() as SketchPoint;
    SketchArc arc=segment as SketchArc;if(arc!=null)return arc.GetEndPoint2() as SketchPoint;
    throw new InvalidOperationException("END_SECTION: unsupported native sketch segment");
}
private static List<AfjEndBoundaryEdge> EndContour(ISldWorks app,Body2 body,double[] outward,double support,out Face2 selectedFace,out int faceCount)
{
    const double tolerance=1e-8;
    var caps=new List<Face2>();
    foreach(Face2 face in (Array)body.GetFaces())
    {
        Surface surface=face.GetSurface() as Surface;if(surface==null||!surface.IsPlane())continue;
        double[] p=surface.PlaneParams as double[];
        if(p==null||p.Length!=6)continue;
        if(Math.Abs(Math.Abs(EndDot(AfjParametricRules.Unit(p.Take(3).ToArray()),outward))-1)>1e-7)continue;
        if(Math.Abs(EndDot(p.Skip(3).Take(3).ToArray(),outward)-support)<tolerance)caps.Add(face);
    }
    if(caps.Count==0)throw new InvalidOperationException("END_SECTION_UNVERIFIED: no actual planar cap at selected axial extreme");
    selectedFace=caps.OrderByDescending(x=>x.GetArea()).First();faceCount=caps.Count;
    var edges=new List<AfjEndBoundaryEdge>();var seen=new List<Edge>();
    foreach(Face2 cap in caps)foreach(Edge edge in (Array)cap.GetEdges())
    {
        if(seen.Any(x=>Same(app,x,edge)))continue;seen.Add(edge);
        Array adjacent=edge.GetTwoAdjacentFaces2() as Array;
        if(adjacent==null)throw new InvalidOperationException("END_SECTION: native edge adjacency unavailable");
        int capUses=adjacent.Cast<object>().OfType<Face2>().Count(f=>caps.Any(c=>Same(app,c,f)));
        if(capUses==2)continue; // Internal boundary between coplanar cap patches.
        if(capUses!=1)throw new InvalidOperationException("END_SECTION: ambiguous cap-edge adjacency");
        Vertex a=edge.GetStartVertex() as Vertex,b=edge.GetEndVertex() as Vertex;
        if(a==null||b==null)throw new InvalidOperationException("END_SECTION_UNVERIFIED: full circle or missing native end vertex");
        double[] pa=a.GetPoint() as double[],pb=b.GetPoint() as double[];
        if(pa==null||pb==null||pa.Length!=3||pb.Length!=3||pa.Concat(pb).Any(x=>!AfjTemporaryBoolean.Finite(x))||EndDistance(pa,pb)<1e-10)
            throw new InvalidOperationException("END_SECTION: degenerate native edge");
        if(Math.Abs(EndDot(pa,outward)-support)>tolerance||Math.Abs(EndDot(pb,outward)-support)>tolerance)
            throw new InvalidOperationException("END_SECTION: edge is not on selected cap plane");
        Curve curve=edge.GetCurve() as Curve;
        if(curve==null||(!curve.IsLine()&&!curve.IsCircle()))throw new InvalidOperationException("END_SECTION_UNVERIFIED: only exact native lines/circular arcs are qualified");
        var item=new AfjEndBoundaryEdge{Native=edge,StartVertex=a,EndVertex=b,A=pa,B=pb,Arc=curve.IsCircle()};
        if(item.Arc)
        {
            double[] circle=curve.CircleParams as double[];
            if(circle==null||circle.Length!=7||circle.Any(x=>!AfjTemporaryBoolean.Finite(x)))throw new InvalidOperationException("END_SECTION: invalid native circle");
            item.Center=circle.Take(3).ToArray();item.Radius=circle[6];
            var parameters=edge.GetCurveParams3();
            if(parameters==null||item.Radius<=0)throw new InvalidOperationException("END_SECTION: native circular edge trim unavailable");
            double angle=Math.Abs(curve.GetLength3(parameters.UMinValue,parameters.UMaxValue)/item.Radius);
            if(angle<1e-7||angle>=Math.PI-1e-7)throw new InvalidOperationException("END_SECTION_UNVERIFIED: only minor circular bends below 180 degrees are qualified");
            if(Math.Abs(Math.Abs(EndDot(AfjParametricRules.Unit(circle.Skip(3).Take(3).ToArray()),outward))-1)>1e-7||
               Math.Abs(EndDot(item.Center,outward)-support)>tolerance)
                throw new InvalidOperationException("END_SECTION: circular arc normal/center does not match cap plane");
        }
        edges.Add(item);
    }
    if(edges.Count<3||edges.Count>256)throw new InvalidOperationException("END_SECTION_UNVERIFIED: unsupported end contour size");
    var nodes=new List<double[]>();
    Func<double[],int> node=p=>
    {
        int[] hits=Enumerable.Range(0,nodes.Count).Where(i=>EndDistance(p,nodes[i])<tolerance).ToArray();
        if(hits.Length>1)throw new InvalidOperationException("END_SECTION: ambiguous endpoint snap");
        if(hits.Length==1)return hits[0];nodes.Add(p);return nodes.Count-1;
    };
    foreach(var edge in edges){edge.From=node(edge.A);edge.To=node(edge.B);if(edge.From==edge.To)throw new InvalidOperationException("END_SECTION: collapsed edge");}
    var incidence=Enumerable.Range(0,nodes.Count).Select(i=>new List<AfjEndBoundaryEdge>()).ToArray();
    foreach(var edge in edges){incidence[edge.From].Add(edge);incidence[edge.To].Add(edge);}
    if(incidence.Any(x=>x.Count!=2))throw new InvalidOperationException("END_SECTION_UNVERIFIED: cap does not have a simple closed boundary");
    var ordered=new List<AfjEndBoundaryEdge>();var used=new HashSet<AfjEndBoundaryEdge>();int start=edges[0].From,current=start;
    while(ordered.Count<edges.Count)
    {
        var next=incidence[current].FirstOrDefault(x=>!used.Contains(x));if(next==null)break;used.Add(next);
        bool forward=next.From==current;
        ordered.Add(new AfjEndBoundaryEdge{Native=next.Native,StartVertex=forward?next.StartVertex:next.EndVertex,
            EndVertex=forward?next.EndVertex:next.StartVertex,A=forward?next.A:next.B,B=forward?next.B:next.A,
            Center=next.Center,Radius=next.Radius,Arc=next.Arc});
        current=forward?next.To:next.From;
    }
    if(ordered.Count!=edges.Count||current!=start)
        throw new InvalidOperationException("END_SECTION_UNVERIFIED: disconnected/multiple end loops require separate qualification");
    return ordered;
}
private static Feature WriteEndFaceSection(ISldWorks app,PartEdit edit,AfjToolDefinition planTool,string suffix,
                                          out bool reverse,out double actualDepth,out AfjEndFaceSectionEvidence evidence)
{
    ModelDoc2 part=edit.Part;Body2 source=CurrentBody(part);
    double[] outgoing=AfjParametricRules.Unit(planTool.Direction),into=AfjParametricRules.Scale(outgoing,-1);
    double end=AfjJointSolver.Projection(source,outgoing),cut=EndDot(planTool.Polygon[0],outgoing);
    actualDepth=end-cut;
    if(!AfjTemporaryBoolean.Finite(actualDepth)||actualDepth<=1e-8||actualDepth>planTool.Length+1e-7)
        throw new InvalidOperationException("END_SECTION_UNVERIFIED: end-to-cut depth is outside the reviewed axial tool");
    foreach(double[] p in planTool.Polygon)
        if(Math.Abs(EndDot(p,outgoing)-cut)>1e-8)throw new InvalidOperationException("END_SECTION: reviewed cut plane is inconsistent");
    Face2 supportFace;int capCount;var contour=EndContour(app,source,outgoing,end,out supportFace,out capCount);
    evidence=new AfjEndFaceSectionEvidence{PlanCutterLengthMetres=planTool.Length,ActualBlindDepthMetres=actualDepth,
        EndSupport=end,CutSupport=cut,CapFaceCount=capCount,BoundaryEdgeCount=contour.Count,ArcCount=contour.Count(x=>x.Arc)};
    int beforePlanes=EndPlaneCount(part);part.ClearSelection2(true);
    if(!((IEntity)supportFace).Select4(false,null))throw new InvalidOperationException("END_SECTION: cannot select original cap face");
    Sketch sketch=null;Feature profile=null;bool oldAddToDb=false,changedAddToDb=false;
    try
    {
        part.SketchManager.InsertSketch(true);sketch=part.GetActiveSketch2() as Sketch;
        if(sketch==null||sketch.Is3D())throw new InvalidOperationException("END_SECTION: expected a native 2D face sketch");
        profile=Own(edit,FindSpecific(app,part,sketch),suffix+"_Profile");evidence.SketchName=profile.Name;
        int refType=0;object reference=sketch.GetReferenceEntity(ref refType);
        evidence.SketchReferenceType=refType;evidence.SketchReferenceVerified=reference is Face2&&Same(app,reference,supportFace);
        if(!evidence.SketchReferenceVerified)throw new InvalidOperationException("END_SECTION: sketch is not attached to the original end Face");
        MathTransform transform=sketch.ModelToSketchTransform,inverse=transform.Inverse() as MathTransform;
        if(inverse==null)throw new InvalidOperationException("END_SECTION: native sketch transform unavailable");
        double[] origin=Point(app,new double[3],inverse),normal=AfjParametricRules.Unit(AfjParametricRules.Sub(Point(app,new[]{0d,0d,1d},inverse),origin));
        reverse=ReverseForAlignment(EndDot(normal,into));
        var segments=new List<SketchSegment>();var local=new List<double[]>();
        oldAddToDb=part.SketchManager.AddToDB;changedAddToDb=true;part.SketchManager.AddToDB=true;
        foreach(var edge in contour)
        {
            double[] a=Point(app,edge.A,transform),b=Point(app,edge.B,transform);local.Add(a);
            if(Math.Abs(a[2])>1e-8||Math.Abs(b[2])>1e-8)throw new InvalidOperationException("END_SECTION: native end vertex is off support plane");
            SketchSegment segment;
            if(edge.Arc)
            {
                double[] c=Point(app,edge.Center,transform);
                double cross=(a[0]-c[0])*(b[1]-c[1])-(a[1]-c[1])*(b[0]-c[0]);
                if(Math.Abs(c[2])>1e-8||Math.Abs(cross)<1e-14)throw new InvalidOperationException("END_SECTION: invalid native minor arc orientation");
                segment=part.SketchManager.CreateArc(c[0],c[1],0,a[0],a[1],0,b[0],b[1],0,(short)(cross>0?1:-1));
            }
            else segment=part.SketchManager.CreateLine(a[0],a[1],0,b[0],b[1],0);
            if(segment==null)throw new InvalidOperationException("END_SECTION: native sketch entity null");segments.Add(segment);
        }
        part.SketchManager.AddToDB=oldAddToDb;changedAddToDb=false;
        for(int i=0;i<segments.Count;i++)
        {
            SketchPoint a=EndSegmentEnd(segments[i]),b=EndSegmentStart(segments[(i+1)%segments.Count]);
            if(!Same(app,a,b)){EndRelation(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,a,b);evidence.ClosureRelations++;}
        }
        for(int i=0;i<segments.Count;i++)
        {
            // Each unique contour vertex is associated to the original Frame;
            // shared endpoint closure handles the preceding segment endpoint.
            EndRelation(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,EndSegmentStart(segments[i]),contour[i].StartVertex);
            evidence.VertexRelations++;
        }
        for(int i=0;i<segments.Count;i++)
        {
            if(!contour[i].Arc)continue;
            int previous=(i+segments.Count-1)%segments.Count,next=(i+1)%segments.Count;
            int line=!contour[previous].Arc?previous:!contour[next].Arc?next:-1;
            if(line<0)throw new InvalidOperationException("END_SECTION_UNVERIFIED: arc has no straight native tangent neighbor");
            double[] tangent=AfjParametricRules.Unit(AfjParametricRules.Sub(contour[line].B,contour[line].A));
            double[] radius=AfjParametricRules.Unit(AfjParametricRules.Sub(line==previous?contour[i].A:contour[i].B,contour[i].Center));
            if(Math.Abs(EndDot(tangent,radius))>1e-7)throw new InvalidOperationException("END_SECTION: neighboring native edge is not tangent to Bend");
            EndRelation(sketch,(int)swConstraintType_e.swConstraintType_TANGENT,segments[i],segments[line]);evidence.TangentRelations++;
        }
        for(int i=0;i<segments.Count;i++)
        {
            SketchPoint p=EndSegmentStart(segments[i]);
            if(Math.Abs(p.X-local[i][0])>1e-8||Math.Abs(p.Y-local[i][1])>1e-8||Math.Abs(p.Z)>1e-8)
                throw new InvalidOperationException("END_SECTION: native relations changed reviewed section");
        }
        evidence.SketchConstrainedStatus=sketch.GetConstrainedStatus();
        evidence.SketchRelationCount=sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swAll);
        if(evidence.SketchConstrainedStatus!=(int)swConstrainedStatus_e.swFullyConstrained||
           sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swDangling)!=0||
           sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swOverDefining)!=0)
            throw new InvalidOperationException("END_SECTION_UNVERIFIED: direct Frame relations do not fully define this end section");
    }
    finally
    {
        try{if(changedAddToDb)part.SketchManager.AddToDB=oldAddToDb;}
        finally{if(sketch!=null)EndSketch(app,part,sketch);}
    }
    int closedType=0;object closedReference=sketch.GetReferenceEntity(ref closedType);
    evidence.PostCloseVerified=closedType==evidence.SketchReferenceType&&closedReference is Face2&&Same(app,closedReference,supportFace)&&
        sketch.GetConstrainedStatus()==(int)swConstrainedStatus_e.swFullyConstrained&&
        sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swDangling)==0;
    if(!evidence.PostCloseVerified)throw new InvalidOperationException("END_SECTION: attachment/relations did not survive sketch close");
    edit.EndFaceSketches.Add(CaptureEndSketch(app,edit,profile,sketch,supportFace,outgoing,end,evidence));
    part.ClearSelection2(true);if(!profile.Select2(false,0))throw new InvalidOperationException("END_SECTION: cannot select the owned native profile");
    Feature feature=part.FeatureManager.FeatureCut4(true,false,reverse,(int)swEndConditions_e.swEndCondBlind,(int)swEndConditions_e.swEndCondBlind,
        actualDepth,actualDepth,false,false,false,false,0,0,false,false,false,false,false,true,true,false,false,false,
        (int)swStartConditions_e.swStartSketchPlane,0,false,false);
    feature=Own(edit,feature,suffix+"_Cut");bool warning;int error=feature.GetErrorCode2(out warning);
    var data=feature.GetDefinition() as ExtrudeFeatureData2;
    string kind=feature.GetTypeName2();if(kind=="ICE")kind=feature.GetTypeName();
    evidence.NewPlanes=EndPlaneCount(part)-beforePlanes;
    if(error!=0||kind!="Cut"||data==null||data.GetEndCondition(true)!=(int)swEndConditions_e.swEndCondBlind||
       Math.Abs(data.GetDepth(true)-actualDepth)>1e-8||data.NormalCut||data.FlipSideToCut||data.ReverseDirection!=reverse||
       data.FromType!=(int)swStartConditions_e.swStartSketchPlane||evidence.NewPlanes!=0)
        throw new InvalidOperationException("END_SECTION: native Cut definition is not the qualified end-section representation");
    Trace.WriteLine("[AFJ-END-FACE] representation="+evidence.Representation+" cap_faces="+evidence.CapFaceCount+
        " boundary_edges="+evidence.BoundaryEdgeCount+" native_vertex_relations="+evidence.VertexRelations+
        " tangent_relations="+evidence.TangentRelations+" sketch_status="+evidence.SketchConstrainedStatus+
        " sketch_dims=0 new_planes="+evidence.NewPlanes+" plan_length_m="+planTool.Length.ToString("R",CultureInfo.InvariantCulture)+
        " actual_blind_depth_m="+actualDepth.ToString("R",CultureInfo.InvariantCulture));
    VerifyEndFaceSketches(app,edit,"post_cut");
    // Caller MUST compare the resulting Body2 to the existing temporary-tool
    // target before accepting. A partial cap must not silently become full stock.
    return feature;
}

        private static Feature WriteTool(ISldWorks app, PartEdit edit, AfjToolDefinition spec,
                                         bool normalCut, string suffix, out bool reverse)
        {
            // No fixed-plane/free-polygon fallback is allowed, even when a new
            // rule has not yet supplied its native geometric relation mapping.
            reverse=false;
            throw new InvalidOperationException("UNVERIFIED_NATIVE_SKETCH: no qualified existing-Face geometric relation recipe for this operation; no new Plane or Cut was created");
        }
        private static void ApplyPart(ISldWorks app, PartEdit edit, AfjCutPlan plan,
                                      Body2 expected, string frame, AfjNativeApplyReport report)
        {
            Activate(app, edit.Part);
            VerifyBody(CurrentBody(edit.Part), edit.Before);
            edit.Part.Extension.StartRecordingUndoObject();
            edit.Started = true; edit.Recording = true;
            try
            {
                int index = 0;
                foreach (AfjCutOperation operation in plan.Operations)
                foreach (AfjToolDefinition tool in operation.Tools)
                {
                    var step = new AfjCutPlan();
                    var cutOperation = new AfjCutOperation { Kind=operation.Kind, EvidenceId=operation.EvidenceId };
                    cutOperation.Tools.Add(tool); step.Operations.Add(cutOperation);
                    var target = AfjTemporaryBoolean.Execute(app, AfjTemporaryBoolean.Copy(CurrentBody(edit.Part)), step);
                    if (target.Kept.Count != 1) throw new InvalidOperationException("UNVERIFIED_RULE: intermediate Cut would split the Frame");
                    if (target.RemovedVolume <= 1e-13) continue; // Avoid native empty cuts.
                    bool endSection=operation.EvidenceId.StartsWith("FOLDED_RULE:",StringComparison.Ordinal);
                    bool normal=endSection?false:UseNativeNormalCut(operation,tool);
                    bool reverse;
                    double nativeDepth=tool.Length;
                    AfjEndFaceSectionEvidence endSketch=null;
                    string suffix="_"+(++index).ToString("00",CultureInfo.InvariantCulture);
                    Feature feature=endSection?
                        WriteEndFaceSection(app,edit,tool,suffix,out reverse,out nativeDepth,out endSketch):
                        WriteTool(app,edit,tool,normal,suffix,out reverse);
                    Body2 current = CurrentBody(edit.Part);
                    if (current.IsSheetMetal() != edit.SheetMetal) throw new InvalidOperationException("APPLY: sheet metal body identity lost");
                    double delta = VerifyBody(current, target.Kept.Single());
                    var data = feature.GetDefinition() as ExtrudeFeatureData2;
                    if (data == null || data.GetEndCondition(true) != (int)swEndConditions_e.swEndCondBlind ||
                        Math.Abs(data.GetDepth(true)-nativeDepth)>1e-8 || data.NormalCut != normal || data.FlipSideToCut || data.ReverseDirection != reverse)
                        throw new InvalidOperationException("APPLY: native Cut definition differs from plan");
                    var evidence = new AfjNativeCutEvidence { Frame=frame, EvidenceId=operation.EvidenceId,
                        FeatureName=feature.Name, FeatureType=feature.GetTypeName2(), UnderlyingType=feature.GetTypeName2()=="ICE"?feature.GetTypeName():feature.GetTypeName2(), DepthPolicy=tool.DepthPolicy,
                        EndCondition=data.GetEndCondition(true), DepthMetres=data.GetDepth(true), NormalCut=data.NormalCut,
                        FlipSideToCut=data.FlipSideToCut, ReverseDirection=data.ReverseDirection,
                        RemovedCubicMetres=target.RemovedVolume, SymmetricDifferenceCubicMetres=delta };
                    evidence.PlanCutterLengthMetres=tool.Length;evidence.NativeActualDepthMetres=nativeDepth;
                    if(endSection)
                    {
                        if(endSketch==null||!endSketch.PostCloseVerified||!endSketch.LaterFeaturesVerified)
                            throw new InvalidOperationException("END_SECTION: missing post-Cut native sketch proof");
                        evidence.NativeRepresentation=endSketch.Representation;evidence.EndFaceSketchEvidenceAvailable=true;
                        evidence.EndFaceSketchLaterFeaturesVerified=endSketch.LaterFeaturesVerified;
                        evidence.SketchName=endSketch.SketchName;evidence.SketchSupport=endSketch.SketchSupport;
                        evidence.SketchReferenceType=endSketch.SketchReferenceType;evidence.SketchConstrainedStatus=endSketch.SketchConstrainedStatus;
                        evidence.SketchRelationCount=endSketch.SketchRelationCount;evidence.SketchDrivingDimensions=endSketch.DrivingDimensions;
                        evidence.NewReferencePlanes=endSketch.NewPlanes;evidence.NativeEndCapFaces=endSketch.CapFaceCount;
                        evidence.NativeEndBoundaryEdges=endSketch.BoundaryEdgeCount;evidence.NativeEndArcs=endSketch.ArcCount;
                        evidence.NativeVertexRelations=endSketch.VertexRelations;evidence.NativeTangentRelations=endSketch.TangentRelations;
                        evidence.NativeClosureRelations=endSketch.ClosureRelations;
                        evidence.SketchReferenceQualification=endSketch.ReferenceQualification;
                        evidence.SketchObservedReferenceType=endSketch.ObservedReferenceType;evidence.SketchObservedReferenceIsFace=endSketch.ObservedReferenceIsFace;
                        evidence.SketchSupportOwnerId=endSketch.OwnerFeatureId;evidence.SketchSupportOwnerInParents=endSketch.OwnerInParents;
                        evidence.SketchFacePersistResolveState=endSketch.FacePersistState;evidence.SketchPlaneVerified=endSketch.SketchPlaneVerified;
                    }
                    report.Cuts.Add(evidence);
                    Trace.WriteLine("[AFJ-APPLY] CUT_VERIFIED frame=" + frame + " id=" + operation.EvidenceId + " feature=" + feature.Name + " delta_m3=" + delta.ToString("R",CultureInfo.InvariantCulture));
                }
                VerifyBody(CurrentBody(edit.Part), expected);
                VerifyEndFaceSketches(app,edit,"part_final");
            }
            finally
            {
                edit.GroupFinished = edit.Part.Extension.FinishRecordingUndoObject2("Auto Frame Joint " + frame, false);
                edit.Recording = !edit.GroupFinished;
                edit.AfterTree = Tree(edit.Part);
                edit.AfterStamp = edit.Part.GetUpdateStamp();
            }
            if (!edit.GroupFinished) throw new InvalidOperationException("APPLY: native undo group could not be finalized");
        }
        private static bool Rollback(ISldWorks app, PartEdit edit, AfjNativeApplyReport report)
        {
            if (!edit.Started) return true;
            try
            {
                Activate(app, edit.Part);
                if (edit.Recording)
                {
                    edit.GroupFinished = edit.Part.Extension.FinishRecordingUndoObject2("Auto Frame Joint failed", false);
                    edit.Recording = !edit.GroupFinished;
                }
                if(edit.Recording)throw new InvalidOperationException("Native Undo recording could not be closed; rollback is unverified");
                string[] currentTree = Tree(edit.Part);
                if (currentTree.SequenceEqual(edit.Tree))
                {
                    VerifyBody(CurrentBody(edit.Part), edit.Before);
                    report.Rollback.Add(edit.Prefix + ": UNCHANGED_NO_UNDO");
                    return true; // Never undo an unrelated item for an empty API group.
                }
                if (edit.AfterTree==null || !currentTree.SequenceEqual(edit.AfterTree) || edit.Part.GetUpdateStamp()!=edit.AfterStamp)
                    throw new InvalidOperationException("Part changed outside the recorded AFJ transaction; automatic Undo refused");
                // Native testing showed that a single EditUndo2 does not undo all
                // plane/sketch/cut steps across sketch edit boundaries. Remove
                // only the exact newly created feature IDs, never an earlier
                // user Undo item or an original feature.
                var currentFeatures=new List<Feature>();
                for(Feature f=edit.Part.FirstFeature() as Feature;f!=null;f=f.GetNextFeature() as Feature)currentFeatures.Add(f);
                var originalRows=currentFeatures.Where(f=>!edit.OwnedIds.Contains(f.GetID())).Select(f=>f.GetTypeName2()+"|"+f.Name).ToArray();
                if(!originalRows.SequenceEqual(edit.Tree))throw new InvalidOperationException("Original feature history changed; owned-feature rollback refused");
                int remaining=currentFeatures.Count(f=>edit.OwnedIds.Contains(f.GetID()));
                for(int attempt=0;remaining>0&&attempt<edit.OwnedIds.Count;attempt++)
                {
                    // Deleting a Cut can also delete its absorbed sketch. Read
                    // live IDs again instead of using now-disconnected RCWs.
                    Feature f=null;
                    for(Feature live=edit.Part.FirstFeature() as Feature;live!=null;live=live.GetNextFeature() as Feature)
                        if(edit.OwnedIds.Contains(live.GetID()))f=live;
                    if(f==null)break;
                    string featureName=f.Name;
                    string type=f.GetTypeName2();
                    string underlying=type=="ICE"?f.GetTypeName():type;
                    edit.Part.ClearSelection2(true);
                    if(!f.Select2(false,0))throw new InvalidOperationException("Cannot select owned rollback feature "+featureName);
                    int options=underlying=="Cut"?(int)swDeleteSelectionOptions_e.swDelete_Absorbed:0;
                    if(!edit.Part.Extension.DeleteSelection2(options))throw new InvalidOperationException("Cannot remove owned rollback feature "+featureName);
                    Trace.WriteLine("[AFJ-APPLY] REMOVE_OWNED "+featureName);
                    int next=0;
                    for(Feature live=edit.Part.FirstFeature() as Feature;live!=null;live=live.GetNextFeature() as Feature)
                        if(edit.OwnedIds.Contains(live.GetID()))next++;
                    if(next>=remaining)throw new InvalidOperationException("Owned-feature removal made no progress");
                    remaining=next;
                }
                if(remaining!=0)throw new InvalidOperationException("Owned rollback feature limit exceeded");
                VerifyBody(CurrentBody(edit.Part), edit.Before);
                var restoredTree=Tree(edit.Part);
                if (!restoredTree.SequenceEqual(edit.Tree))
                {
                    Trace.WriteLine("[AFJ-APPLY] TREE_BEFORE "+String.Join(";",edit.Tree));
                    Trace.WriteLine("[AFJ-APPLY] TREE_AFTER_UNDO "+String.Join(";",restoredTree));
                    throw new InvalidOperationException("Feature tree differs after undo");
                }
                report.Rollback.Add(edit.Prefix + ": OWNED_FEATURES_REMOVED_GEOMETRY_AND_TREE_RESTORED");
                return true;
            }
            catch (Exception ex)
            {
                report.Rollback.Add(edit.Prefix + ": ROLLBACK_UNVERIFIED " + ex.Message);
                Trace.WriteLine("[AFJ-APPLY] ROLLBACK_FAILED " + ex);
                return false;
            }
        }
        internal static bool TryApply(ISldWorks app, ModelDoc2 assembly, AfjApplyTicket ticket,
                                      out AfjNativeApplyReport report, out string message)
        {
            report = new AfjNativeApplyReport();
            Trace.WriteLine("[AFJ-R53] APPLY_ENTRY writer=EXISTING_FACE_LINKED_SKETCH plane_fallback=DISABLED");
            message = "Chưa áp dụng Cut.";
            PartEdit a = null, b = null;
            AfjNativeUTransaction constrainedU = null;
            bool apiAllowed = false;
            try
            {
                if (System.Threading.Thread.CurrentThread.GetApartmentState() != System.Threading.ApartmentState.STA)
                    throw new InvalidOperationException("APPLY: SOLIDWORKS STA required");
                if (app==null || assembly==null) throw new InvalidOperationException("APPLY: bound application/document required");
                apiAllowed = true;
                if (ticket == null || ticket.Consumed || ticket.Preview == null || ticket.IsSourceCurrent == null || !ticket.IsSourceCurrent())
                    throw new InvalidOperationException("PREVIEW_STALE: hãy tạo lại Preview đúng hai Frame trước khi OK");
                ticket.Consumed = true;
                var result = ticket.Preview;
                if (result.Plan==null || (result.Plan.RuleId!="ORTHOGONAL_U_COVER" && result.Plan.RuleId!="ORTHOGONAL_U_TALLER_COVER" && result.Plan.RuleId!=AfjFoldedButtRules.RuleId))
                    throw new InvalidOperationException("UNVERIFIED_RULE: native Cut writer has no qualified rule mapping");
                bool uRule = result.Plan.RuleId == "ORTHOGONAL_U_COVER" || result.Plan.RuleId == "ORTHOGONAL_U_TALLER_COVER";
                // The existing U rule never falls through to the free-Sketch/plane writer.
                // The linked writer validates the retained intent, original native datums
                // and source state before creating any Sketch. No free-plane fallback.
                if (uRule && !ConstrainedURouteQualified)
                    throw new InvalidOperationException("UNVERIFIED_APPLY: constrained U writer qualification is pending; no native Cut was created");
                if (uRule && (result.ReviewedIntent == null || !Object.ReferenceEquals(result.ReviewedIntent.Plan, result.Plan)))
                    throw new InvalidOperationException("UNVERIFIED_APPLY: exact reviewed U intent is unavailable; create a new Preview");
                var activeAssembly = app.ActiveDoc as ModelDoc2;
                Trace.WriteLine("[AFJ-APPLY] PREFLIGHT active=" + (activeAssembly==null?"NONE":activeAssembly.GetTitle()) + " bound=" + assembly.GetTitle() +
                    " assembly_equality=" + app.IsSame(activeAssembly,assembly) + " part_equality=" + app.IsSame(result.PartA,result.PartB));
                if (!Same(app, activeAssembly, assembly))
                    throw new InvalidOperationException("APPLY: the reviewed Assembly is no longer active");
                if (Same(app, result.PartA, result.PartB))
                    throw new InvalidOperationException("APPLY: two independent Part documents are required");
                foreach (AfjCutOperation operation in result.Plan.A.Operations.Concat(result.Plan.B.Operations))
                    if (operation.Kind != AfjOperationKind.Cut || operation.Tools.Count == 0 || String.IsNullOrEmpty(operation.EvidenceId))
                        throw new InvalidOperationException("UNVERIFIED_RULE: native writer only supports verified subtractive prism plans");
                PreflightPart(app, assembly, ticket.FrameA, result.PartA);
                PreflightPart(app, assembly, ticket.FrameB, result.PartB);
                string run = "AFJ_" + Guid.NewGuid().ToString("N").Substring(0,8);
                if (!ticket.IsSourceCurrent()) throw new InvalidOperationException("PREVIEW_STALE: source changed before mutation");
                if (uRule)
                {
                    // Assign the transaction before Apply: even the first native Sketch
                    // can mutate history and fail before a closed Sketch is returned.
                    constrainedU = new AfjNativeUTransaction(app, assembly, ticket,
                        result.ReviewedIntent, report, run, phase => Trace.WriteLine("[AFJ-APPLY] U " + phase));
                    constrainedU.Apply();
                }
                else
                {
                    // Preserve the existing qualified folded writer and its rollback.
                    a = new PartEdit { Part=result.PartA, Before=ticket.BeforeA, Tree=Tree(result.PartA), SheetMetal=CurrentBody(result.PartA).IsSheetMetal(), Prefix=run+"_A" };
                    b = new PartEdit { Part=result.PartB, Before=ticket.BeforeB, Tree=Tree(result.PartB), SheetMetal=CurrentBody(result.PartB).IsSheetMetal(), Prefix=run+"_B" };
                    ApplyPart(app, a, result.Plan.A, result.A.Kept.Single(), "A", report);
                    ApplyPart(app, b, result.Plan.B, result.B.Kept.Single(), "B", report);
                }
                Activate(app, assembly);
                double[] actualA = ticket.FrameA.GetTotalTransform(false).ArrayData as double[];
                double[] actualB = ticket.FrameB.GetTotalTransform(false).ArrayData as double[];
                double[] oldA = result.TransformA.ArrayData as double[], oldB = result.TransformB.ArrayData as double[];
                if (actualA==null || actualB==null || oldA==null || oldB==null || actualA.Length!=16 || actualB.Length!=16 || oldA.Length!=16 || oldB.Length!=16 ||
                    actualA.Where((x,i)=>!AfjTemporaryBoolean.Finite(x)||Math.Abs(x-oldA[i])>1e-10).Any() ||
                    actualB.Where((x,i)=>!AfjTemporaryBoolean.Finite(x)||Math.Abs(x-oldB[i])>1e-10).Any())
                    throw new InvalidOperationException("APPLY: component placement changed from reviewed Preview");
                VerifyBody(CurrentBody(result.PartA),result.A.Kept.Single());
                VerifyBody(CurrentBody(result.PartB),result.B.Kept.Single());
                if (constrainedU == null)
                {
                    VerifyEndFaceSketches(app,a,"both_parts_final");
                    VerifyEndFaceSketches(app,b,"both_parts_final");
                }
                else constrainedU.CheckSketches("TRY_APPLY_FINAL_BODY_AND_PLACEMENT");
                var wa = AfjJointSolver.World(new[]{AfjTemporaryBoolean.Copy(CurrentBody(result.PartA))}, result.TransformA);
                var wb = AfjJointSolver.World(new[]{AfjTemporaryBoolean.Copy(CurrentBody(result.PartB))}, result.TransformB);
                report.InterferenceCubicMetres = AfjJointSolver.Overlap(wa, wb);
                report.GapMetres = AfjJointSolver.Gap(assembly, wa, wb);
                if (report.InterferenceCubicMetres>1e-12 || Math.Abs(report.GapMetres-AfjManufacturingConventions.AssemblyFitGap)>1e-6)
                    throw new InvalidOperationException("APPLY: native pair failed overlap/fit-gap validation");
                if (constrainedU != null)
                {
                    if (!Same(app, app.ActiveDoc, assembly) ||
                        !Same(app, ((AssemblyDoc)assembly).GetEditTarget(), assembly) || assembly.GetActiveSketch2()!=null)
                        throw new InvalidOperationException("APPLY: Assembly edit context was not restored before commit");
                    constrainedU.Commit();
                }
                report.Status = "APPLY_VERIFIED";
                message = "Đã tạo " + report.Cuts.Count + " Cut-Extrude trên hai Frame, khớp Preview. Hãy kiểm tra và lưu Part khi muốn giữ thay đổi.";
                return true;
            }
            catch (Exception ex)
            {
                report.Error = "HRESULT=0x" + ex.HResult.ToString("X8") + " " + ex;
                Trace.WriteLine("[AFJ-APPLY] " + report.Error);
                if (constrainedU != null)
                {
                    // Exactly one global rollback owns this branch. Never also invoke
                    // independent per-Part rollback across the linked Sketch graph.
                    try { report.RollbackVerified = constrainedU.Rollback().Verified; }
                    catch (Exception rollbackError)
                    {
                        report.RollbackVerified = false;
                        report.Rollback.Add("CONSTRAINED_U_GLOBAL_JOURNAL ROLLBACK_UNVERIFIED " + rollbackError);
                        Trace.WriteLine("[AFJ-APPLY] U ROLLBACK_FAILED " + rollbackError);
                    }
                }
                else
                {
                    bool rb = b == null || Rollback(app,b,report);
                    bool ra = a == null || Rollback(app,a,report);
                    report.RollbackVerified = rb && ra;
                }
                report.Status = report.RollbackVerified ? "REJECTED_OR_ROLLED_BACK" : "ROLLBACK_UNVERIFIED";
                message = report.RollbackVerified ? "Chưa áp dụng Cut; hình học hai Part được giữ hoặc hoàn tác. " + ex.Message
                    : "Cut lỗi và chưa xác minh hoàn tác đầy đủ. Không lưu Part; xem log AFJ-APPLY và Undo thủ công.";
                return false;
            }
            finally
            {
                try { if (apiAllowed) { Activate(app, assembly); assembly.GraphicsRedraw2(); } }
                catch (Exception ex) { Trace.WriteLine("[AFJ-APPLY] Assembly restore failed " + ex); }
            }
        }
    }
}


namespace ADDIN.Commands.AutoFrameJoint
{
    // AFJ-FOLDED-BUTT-20261022: qualified native planar/cylindrical section;
    // short-member priority is policy, complete visual coverage is not asserted.
    internal static class AfjFoldedButtRules
    {
        internal const string RuleId="ORTHOGONAL_FOLDED_SHORT_COVER_BUTT";
        const double Eps=1e-7;
        internal sealed class Section
        {
            internal AfjProfile Profile;
            internal double[][] Ordered;
            internal double[] NativeWidth, NativeDepth, Origin, Width, Depth, Axis;
            internal double Thickness, Radius;
            internal string GraphJson;
        }
        internal sealed class Context
        {
            internal Section Loser,Winner;
            internal Body2 LoserBody,WinnerBody;
            internal double LoserCut,WinnerCut,LoserBandStart,WinnerBandStart;
            internal SupportEvidence LongCutSupport,ShortCutSupport;
        }
        static double Dot(double[] a,double[] b){return AfjParametricRules.Dot(a,b);}
        static double[] Add(double[] a,double[] b){return AfjParametricRules.Add(a,b);}
        static double[] Sub(double[] a,double[] b){return AfjParametricRules.Sub(a,b);}
        static double[] Mul(double[] a,double b){return AfjParametricRules.Scale(a,b);}
        static double[] Unit(double[] a){return AfjParametricRules.Unit(a);}
        static double[] P(double[] l,int end){return AfjParametricRules.P(l,end);}
        static double[] Cross(double[] a,double[] b){return new[]{a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]};}
        static double Norm(double[] a){return Math.Sqrt(Dot(a,a));}
        static bool Same(double[] a,double[] b){return Norm(Sub(a,b))<=Eps;}
        static double[] World(double[] p,MathTransform t){return AfjProfileAnalyzer.Transform(p,(double[])t.ArrayData);}
        static double[] WorldVector(double[] p,MathTransform t){return Unit(Sub(World(p,t),World(new double[3],t)));}
        static double[] NativeVector(double[] p,MathTransform t){return Unit(Sub(AfjParametricRules.Native(p,t),AfjParametricRules.Native(new double[3],t)));}
        static void Fail(string reason){throw new InvalidOperationException("UNVERIFIED_RULE: folded profile "+reason);}

        internal static double[][] Ordered(AfjProfile profile)
        {
            if(profile==null||profile.Lines==null||profile.Lines.Length<3||profile.Lines.Length>32)Fail("line count");
            var nodes=new List<double[]>();var edges=new List<int[]>();
            Func<double[],int> node=p=>{int[] hits=Enumerable.Range(0,nodes.Count).Where(i=>Same(nodes[i],p)).ToArray();if(hits.Length>1)Fail("ambiguous endpoint");if(hits.Length==1)return hits[0];nodes.Add(p);return nodes.Count-1;};
            double[] axis=Unit(profile.Axis);double plane=Dot(P(profile.Lines[0],0),axis);
            foreach(double[] line in profile.Lines)
            {
                if(line==null||line.Length!=6||line.Any(value=>!AfjTemporaryBoolean.Finite(value)))Fail("invalid line");
                double[] a=P(line,0),b=P(line,1);
                if(Norm(Sub(a,b))<Eps||Math.Abs(Dot(a,axis)-plane)>Eps||Math.Abs(Dot(b,axis)-plane)>Eps)Fail("nontransverse or zero line");
                int u=node(a),v=node(b);
                if(edges.Any(e=>e[0]==u&&e[1]==v||e[0]==v&&e[1]==u))Fail("duplicate line");
                edges.Add(new[]{u,v});
            }
            var incidence=Enumerable.Range(0,nodes.Count).Select(i=>Enumerable.Range(0,edges.Count).Where(k=>edges[k].Contains(i)).ToArray()).ToArray();
            if(incidence.Any(e=>e.Length<1||e.Length>2))Fail("branched chain");
            int[] ends=Enumerable.Range(0,nodes.Count).Where(i=>incidence[i].Length==1).ToArray();
            if(ends.Length!=2)Fail("requires one open chain");
            var used=new HashSet<int>();var result=new List<double[]>();int current=ends[0];
            while(true)
            {
                int[] options=incidence[current].Where(i=>!used.Contains(i)).ToArray();if(options.Length==0)break;
                if(options.Length!=1)Fail("branched traversal");int k=options[0];used.Add(k);int next=edges[k][0]==current?edges[k][1]:edges[k][0];
                result.Add(nodes[current].Concat(nodes[next]).ToArray());current=next;
            }
            if(used.Count!=profile.Lines.Length)Fail("disconnected chain");
            for(int i=1;i<result.Count;i++)if(Dot(Unit(Sub(P(result[i-1],1),P(result[i-1],0))),Unit(Sub(P(result[i],1),P(result[i],0))))<-.999999)Fail("fold-back turn");
            return result.ToArray();
        }
        internal static bool HasComplexProfile(AfjProfile profile)
        {
            double[][] ordered=Ordered(profile);double[] web;
            try{web=AfjParametricRules.WebLine(profile);}
            catch(InvalidOperationException ex){if(!ex.Message.StartsWith("UNVERIFIED_RULE",StringComparison.Ordinal))throw;return true;}
            int index=Array.FindIndex(ordered,l=>Same(P(l,0),P(web,0))&&Same(P(l,1),P(web,1))||Same(P(l,0),P(web,1))&&Same(P(l,1),P(web,0)));
            if(index<0)Fail("Web not in chain");
            if(index>2||ordered.Length-index-1>2)return true;
            double[] width=Unit(Sub(P(web,1),P(web,0)));
            double[] depth=Unit(Cross(Unit(profile.Axis),width));
            // Inclined native strips need the measured full-section rule;
            // the orthogonal U reader must not reject them before dispatch.
            foreach(double[] line in ordered)
            {
                double[] direction=Unit(Sub(P(line,1),P(line,0)));
                if(Math.Abs(Dot(direction,width))<1-1e-7&&Math.Abs(Dot(direction,depth))<1-1e-7)return true;
            }
            for(int side=0;side<2;side++)
            {
                double[] at=P(web,side);var used=new HashSet<double[]>{web};var path=new List<double[]>();
                while(true){var hit=profile.Lines.Where(l=>!used.Contains(l)&&(Same(P(l,0),at)||Same(P(l,1),at))).ToArray();if(hit.Length==0)break;if(hit.Length!=1)Fail("branched side");var line=hit[0];at=Same(P(line,0),at)?P(line,1):P(line,0);path.Add(at);used.Add(line);}
                if(path.Count==2){double projection=Dot(Sub(path[1],path[0]),width);if(side==0&&projection>0||side==1&&projection<0)return true;}
            }
            return false;
        }
        internal static bool RequiresButtPlacement(AfjUSection loser,AfjUSection winner)
        {
            if(Math.Abs(loser.Thickness-winner.Thickness)>1e-8||Math.Abs(loser.Radius-winner.Radius)>1e-8||loser.NearReturn||winner.NearReturn)return true;
            double loserDepth=loser.FarHeight+(loser.FarReturn?loser.Thickness:0),winnerDepth=winner.FarHeight+(winner.FarReturn?winner.Thickness:0);
            bool unequal=Math.Abs(loser.NearHeight-winner.NearHeight)>1e-7||Math.Abs(loserDepth-winnerDepth)>1e-7;
            return unequal&&(loser.FarReturn||winner.FarReturn||winner.NearHeight+1e-7<loser.NearHeight||winnerDepth+1e-7<loserDepth);
        }
        static Section Read(AfjProfile p,Body2 body,MathTransform transform,double[] otherOutgoing)
        {
            double[][] ordered=Ordered(p);double[] a=P(ordered[0],0),b=P(ordered[0],1);
            // This orthonormal pair encloses tools and slices only. It does not
            // label a Web or impose perpendicularity on the physical strips.
            double[] width=Unit(Sub(b,a)),depth=Unit(Cross(Unit(p.Axis),width));
            var result=new Section{Profile=p,Ordered=ordered,NativeWidth=width,NativeDepth=depth,Origin=World(a,transform),Width=WorldVector(width,transform),Depth=WorldVector(depth,transform),Axis=WorldVector(p.Axis,transform),Thickness=p.Thickness,Radius=p.Radius};
            VerifyNativeSkins(result,body);
            result.GraphJson=GraphJson(result);
            Trace.WriteLine("[AFJ-FOLDED] PROFILE_GRAPH "+result.GraphJson);
            return result;
        }
        static string PointJson(double[] p){return "["+String.Join(",",p.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)))+"]";}
        static string GraphJson(Section section)
        {
            var nodes=section.Ordered.Select(l=>P(l,0)).Concat(new[]{P(section.Ordered.Last(),1)}).ToArray();
            string edges=String.Join(",",section.Ordered.Select((l,i)=>"{\"from\":"+i+",\"to\":"+(i+1)+",\"source_line_index\":"+Array.FindIndex(section.Profile.Lines,x=>Same(P(x,0),P(l,0))&&Same(P(x,1),P(l,1))||Same(P(x,0),P(l,1))&&Same(P(x,1),P(l,0)))+"}"));
            return "{\"schema\":\"AFJ_FOLDED_CHAIN_1\",\"coordinates\":\"native_part_metres\",\"graph_evidence\":\"DERIVED_FROM_API_BASE_SKETCH\",\"native_skin_bend_checks\":true,\"web_label\":\"NOT_REQUIRED_FOR_AXIAL_BUTT\",\"axis\":"+PointJson(Unit(section.Profile.Axis))+",\"thickness_m\":"+section.Thickness.ToString("R",CultureInfo.InvariantCulture)+",\"radius_m\":"+section.Radius.ToString("R",CultureInfo.InvariantCulture)+",\"nodes\":["+String.Join(",",nodes.Select(PointJson))+"],\"edges\":["+edges+"]}";
        }
        static bool PlaneMatches(Face2 face,double[] normal,double support)
        {
            var surface=face.GetSurface() as Surface;if(surface==null||!surface.IsPlane())return false;
            var p=surface.PlaneParams as double[];return p!=null&&p.Length>=6&&Math.Abs(Dot(Unit(p.Take(3).ToArray()),normal))>.999999&&Math.Abs(Dot(p.Skip(3).Take(3).ToArray(),normal)-support)<Eps&&face.GetArea()>1e-14;
        }
        static bool AdjacentToStrip(Face2 cylinder,double[] normal,double support,double thickness)
        {
            var edges=cylinder.GetEdges() as Array;if(edges==null)return false;
            foreach(Edge edge in edges)
            {
                var faces=edge.GetTwoAdjacentFaces2() as Array;if(faces==null)continue;
                foreach(object value in faces){var f=value as Face2;if(f!=null&&(PlaneMatches(f,normal,support)||PlaneMatches(f,normal,support+thickness)||PlaneMatches(f,normal,support-thickness)))return true;}
            }
            return false;
        }
        sealed class NativeFaceRecord
        {
            internal Face2 Face;
            internal double[] PlaneNormal,PlanePoint,CylinderCenter,CylinderAxis;
            internal double Area,Radius;
            internal readonly List<NativeFaceRecord> AdjacentPlanes=new List<NativeFaceRecord>();
        }
        static NativeFaceRecord ReadFace(Face2 face)
        {
            var result=new NativeFaceRecord{Face=face};var surface=face.GetSurface() as Surface;
            if(surface==null)return result;
            if(surface.IsPlane())
            {
                double[] p=surface.PlaneParams as double[];
                if(p!=null&&p.Length>=6){result.PlaneNormal=Unit(p.Take(3).ToArray());result.PlanePoint=p.Skip(3).Take(3).ToArray();result.Area=face.GetArea();}
            }
            else if(surface.IsCylinder())
            {
                double[] c=surface.CylinderParams as double[];
                if(c!=null&&c.Length>=7){result.CylinderCenter=c.Take(3).ToArray();result.CylinderAxis=Unit(c.Skip(3).Take(3).ToArray());result.Radius=c[6];}
            }
            return result;
        }
        static NativeFaceRecord[] SnapshotNativeFaces(Body2 body)
        {
            // AFJ-FOLDED-NATIVE-FACE-SNAPSHOT-20261022
            // Body-local snapshot only. No face/edge data survives this call or
            // is reused after Boolean operations or native feature mutations.
            var timer=Stopwatch.StartNew();var raw=body.GetFaces() as Array;if(raw==null)Fail("missing faces");
            var byFace=new Dictionary<object,NativeFaceRecord>();var records=new List<NativeFaceRecord>();
            foreach(object value in raw){var face=value as Face2;if(face==null)continue;var record=ReadFace(face);byFace[value]=record;records.Add(record);}
            int edgesRead=0;
            foreach(var record in records.Where(f=>f.CylinderCenter!=null))
            {
                var edges=record.Face.GetEdges() as Array;if(edges==null)continue;
                foreach(Edge edge in edges)
                {
                    edgesRead++;var adjacent=edge.GetTwoAdjacentFaces2() as Array;if(adjacent==null)continue;
                    foreach(object value in adjacent)
                    {
                        var face=value as Face2;if(face==null)continue;NativeFaceRecord other;
                        if(!byFace.TryGetValue(value,out other)){other=ReadFace(face);byFace[value]=other;}
                        if(other.PlaneNormal!=null&&!record.AdjacentPlanes.Contains(other))record.AdjacentPlanes.Add(other);
                    }
                }
            }
            Trace.WriteLine("[AFJ-FOLDED-PERF] phase=skin_snapshot faces="+records.Count+" cylinder_edges="+edgesRead+" ms="+timer.ElapsedMilliseconds);
            return records.ToArray();
        }
        static bool PlaneMatches(NativeFaceRecord face,double[] normal,double support)
        {return face.PlaneNormal!=null&&Math.Abs(Dot(face.PlaneNormal,normal))>.999999&&Math.Abs(Dot(face.PlanePoint,normal)-support)<Eps&&face.Area>1e-14;}
        static bool AdjacentToStrip(NativeFaceRecord cylinder,double[] normal,double support,double thickness)
        {return cylinder.AdjacentPlanes.Any(f=>PlaneMatches(f,normal,support)||PlaneMatches(f,normal,support+thickness)||PlaneMatches(f,normal,support-thickness));}
        static bool ContainsSkinPoint(NativeFaceRecord face,double[] normal,double support,double[] point)
        {
            if(!PlaneMatches(face,normal,support))return false;
            double[] closest=face.Face.GetClosestPointOn(point[0],point[1],point[2]) as double[];
            return closest!=null&&closest.Length>=3&&Norm(Sub(closest.Take(3).ToArray(),point))<Eps;
        }
        internal static void VerifyNativeSkins(Section section,Body2 body)
        {
            double[] axis=Unit(section.Profile.Axis);double t=section.Thickness,r=section.Radius;
            if(t<=0||r<=0||!AfjTemporaryBoolean.Finite(t+r))Fail("invalid sheet parameters");
            var faces=SnapshotNativeFaces(body);
            double axialLow=-NativeExtreme(body,Mul(axis,-1)),axialHigh=NativeExtreme(body,axis);
            int strip=0;
            foreach(double[] line in section.Ordered)
            {
                double[] normal=Unit(Cross(axis,Unit(Sub(P(line,1),P(line,0)))));double support=Dot(P(line,0),normal);
                double[] middle=Mul(Add(P(line,0),P(line,1)),.5);bool matched=false;
                foreach(double fraction in new[]{.35,.65})
                {
                    double[] p=Add(middle,Mul(axis,axialLow+(axialHigh-axialLow)*fraction-Dot(middle,axis)));
                    bool zero=faces.Any(f=>ContainsSkinPoint(f,normal,support,p));
                    bool plus=faces.Any(f=>ContainsSkinPoint(f,normal,support+t,Add(p,Mul(normal,t))));
                    bool minus=faces.Any(f=>ContainsSkinPoint(f,normal,support-t,Add(p,Mul(normal,-t))));
                    Trace.WriteLine("[AFJ-FOLDED] STRIP_SKINS index="+strip+" sample="+fraction.ToString("R",CultureInfo.InvariantCulture)+" zero="+zero+" plus="+plus+" minus="+minus);
                    if(zero&&plus!=minus){matched=true;break;}
                }
                if(!matched)Fail("missing or ambiguous finite paired 0/+t/-t skins at strip "+strip);
                strip++;
            }
            for(int k=1;k<section.Ordered.Length;k++)
            {
                double[] first=section.Ordered[k-1],second=section.Ordered[k],vertex=P(first,1);
                double turn=Dot(Unit(Sub(P(first,1),P(first,0))),Unit(Sub(P(second,1),P(second,0))));
                if(turn>.999999)continue; // Preserve a collinear split; do not invent a bend.
                double[] n1=Unit(Cross(axis,Unit(Sub(P(first,1),P(first,0))))),n2=Unit(Cross(axis,Unit(Sub(P(second,1),P(second,0)))));
                var inner=new List<double[]>();var outer=new List<double[]>();
                foreach(var face in faces)
                {
                    if(face.CylinderCenter==null||Math.Abs(Dot(face.CylinderAxis,axis))<.999999)continue;
                    if(Math.Abs(face.Radius-r)>Eps&&Math.Abs(face.Radius-r-t)>Eps)continue;
                    double[] center=face.CylinderCenter,delta=Sub(center,vertex);delta=Sub(delta,Mul(axis,Dot(delta,axis)));
                    if(Norm(delta)>(r+t)/Math.Sqrt((1+turn)/2)+Eps)continue;
                    if(!AdjacentToStrip(face,n1,Dot(vertex,n1),t)||!AdjacentToStrip(face,n2,Dot(vertex,n2),t))continue;
                    (Math.Abs(face.Radius-r)<Eps?inner:outer).Add(center);
                }
                if(!inner.Any(a=>outer.Any(b=>Norm(Sub(Sub(a,b),Mul(axis,Dot(Sub(a,b),axis))))<Eps)))Fail("missing paired adjacent R/R+t cylinder at vertex "+k);
            }
            Trace.WriteLine("[AFJ-FOLDED] NATIVE_SECTION_VERIFIED strips="+section.Ordered.Length+" turn_nodes="+(section.Ordered.Length-1));
        }
        static double NativeExtreme(Body2 body,double[] normal)
        {return AfjJointSolver.Projection(body,normal);}
        // AFJ-FOLDED-BREP-BOUNDARY-SUPPORT-20261022
        // Direction comes from the already verified perpendicular Frame axes.
        // This is a geometric placement witness, not an associative feature
        // reference and not a claim that an orthogonal support FACE exists.
        internal sealed class SupportEvidence
        {
            internal string Kind;
            internal double[] NativeDirection, ExtremePointNative, WitnessPointNative;
            internal double NativeSupport, Coordinate, WitnessDistance;
            internal bool Minimum, ExtremePointOnTrimmedFace;
            internal int TrimmedFaceWitnesses, PlanarSupportFaces, VertexSupportWitnesses;
            internal int EdgeSupportWitnesses=-1; // -1: not searched, never zero-measured.
        }
        static bool FinitePoint(double[] point)
        { return point!=null&&point.Length>=3&&point.Take(3).All(AfjTemporaryBoolean.Finite); }
        static string SupportJson(SupportEvidence evidence)
        {
            if(evidence==null)return "null";
            return "{\"kind\":\""+evidence.Kind+"\",\"coordinate_system\":\"native_part_metres\",\"minimum\":"+
                (evidence.Minimum?"true":"false")+",\"native_direction\":"+PointJson(evidence.NativeDirection)+
                ",\"extreme_point\":"+PointJson(evidence.ExtremePointNative)+",\"witness_point\":"+PointJson(evidence.WitnessPointNative)+
                ",\"native_support_m\":"+evidence.NativeSupport.ToString("R",CultureInfo.InvariantCulture)+
                ",\"returned_coordinate_m\":"+evidence.Coordinate.ToString("R",CultureInfo.InvariantCulture)+
                ",\"witness_distance_from_extreme_point_m\":"+evidence.WitnessDistance.ToString("R",CultureInfo.InvariantCulture)+
                ",\"extreme_on_trimmed_face\":"+(evidence.ExtremePointOnTrimmedFace?"true":"false")+
                ",\"trimmed_face_witnesses\":"+evidence.TrimmedFaceWitnesses+",\"planar_support_faces\":"+evidence.PlanarSupportFaces+
                ",\"vertex_support_witnesses\":"+evidence.VertexSupportWitnesses+",\"edge_support_witnesses\":"+evidence.EdgeSupportWitnesses+
                ",\"associative_cut_reference\":false}";
        }
        static double Support(Body2 body,MathTransform transform,double[] worldDirection,double[] origin,bool minimum,out SupportEvidence evidence)
        {
            // Body2.GetExtremePoint supplies the global support scalar from the
            // actual B-rep. Closest-point calls below only certify its native
            // trimmed topology; they never estimate it from a bounding box.
            double[] direction=minimum?Mul(worldDirection,-1):worldDirection,native=NativeVector(direction,transform);
            double x,y,z;
            if(!body.GetExtremePoint(native[0],native[1],native[2],out x,out y,out z))Fail("B-rep extremum unavailable");
            double[] extreme=new[]{x,y,z};
            if(!FinitePoint(extreme))Fail("non-finite B-rep extremum");
            double support=Dot(extreme,native);
            if(!AfjTemporaryBoolean.Finite(support))Fail("non-finite support scalar");
            evidence=new SupportEvidence{NativeDirection=native,ExtremePointNative=extreme,NativeSupport=support,Minimum=minimum};
            var raw=body.GetFaces() as Array;
            if(raw==null||raw.Length==0||raw.Length>50000)Fail("support face inventory unavailable or excessive");
            Face2 planarWitness=null;
            foreach(Face2 face in raw.Cast<object>().OfType<Face2>())
            {
                double[] closest=face.GetClosestPointOn(x,y,z) as double[];
                if(FinitePoint(closest)&&Norm(Sub(closest.Take(3).ToArray(),extreme))<Eps)evidence.TrimmedFaceWitnesses++;
                if(PlaneMatches(face,native,support)){evidence.PlanarSupportFaces++;if(planarWitness==null)planarWitness=face;}
            }
            evidence.ExtremePointOnTrimmedFace=evidence.TrimmedFaceWitnesses>0;
            if(!evidence.ExtremePointOnTrimmedFace)Fail("global extremum is not certified on an actual trimmed native Face");
            if(planarWitness!=null)
            {
                evidence.Kind="PLANAR_FACE_SUPPORT";
                // A native support face may be a different coplanar patch from
                // the particular global extremum returned by SOLIDWORKS.
                double[] closest=planarWitness.GetClosestPointOn(x,y,z) as double[];
                if(!FinitePoint(closest)||Math.Abs(Dot(closest.Take(3).ToArray(),native)-support)>Eps)
                    Fail("planar support face has no finite trimmed witness");
                evidence.WitnessPointNative=closest.Take(3).ToArray();
            }
            else
            {
                Array vertices=body.GetVertices() as Array;
                if(vertices==null||vertices.Length>100000)Fail("native support vertex inventory unavailable or excessive");
                foreach(Vertex vertex in vertices.Cast<object>().OfType<Vertex>())
                {
                    double[] point=vertex.GetPoint() as double[];
                    if(!FinitePoint(point))Fail("invalid native support vertex");
                    if(Math.Abs(Dot(point.Take(3).ToArray(),native)-support)<Eps)
                    {
                        evidence.VertexSupportWitnesses++;
                        if(evidence.WitnessPointNative==null)evidence.WitnessPointNative=point.Take(3).ToArray();
                    }
                }
                if(evidence.VertexSupportWitnesses>0)evidence.Kind="NATIVE_VERTEX_BOUNDARY_SUPPORT";
                else
                {
                    // For a rounded extremum the global point can lie inside a
                    // cylinder face. A cap arc can witness the SAME support at
                    // another axial position. Do not claim point coincidence.
                    Array edges=body.GetEdges() as Array;
                    if(edges==null||edges.Length>100000)Fail("native support edge inventory unavailable or excessive");
                    evidence.EdgeSupportWitnesses=0;
                    foreach(Edge edge in edges.Cast<object>().OfType<Edge>())
                    {
                        double[] point=edge.GetClosestPointOn(x,y,z) as double[];
                        if(!FinitePoint(point))continue;
                        if(Math.Abs(Dot(point.Take(3).ToArray(),native)-support)<Eps)
                        {
                            evidence.EdgeSupportWitnesses++;
                            if(evidence.WitnessPointNative==null)evidence.WitnessPointNative=point.Take(3).ToArray();
                        }
                    }
                    if(evidence.EdgeSupportWitnesses==0)Fail("support has no qualified actual native vertex or trimmed-edge witness");
                    evidence.Kind="NATIVE_EDGE_BOUNDARY_SUPPORT";
                }
            }
            if(evidence.WitnessPointNative==null)Fail("native support witness missing");
            evidence.WitnessDistance=Norm(Sub(evidence.WitnessPointNative,extreme));
            double[] pointOrigin=AfjParametricRules.Native(origin,transform);
            double signed=support-Dot(pointOrigin,native);
            evidence.Coordinate=minimum?-signed:signed;
            if(!AfjTemporaryBoolean.Finite(evidence.Coordinate))Fail("non-finite returned support coordinate");
            Trace.WriteLine("[AFJ-FOLDED] SUPPORT_EVIDENCE "+SupportJson(evidence));
            return evidence.Coordinate;
        }
        static AfjToolDefinition AxialBox(Body2 body,double[] axis,double[] u,double[] v,double start,double end)
        {
            const double margin=.002;
            double u0=-NativeExtreme(body,Mul(u,-1))-margin,u1=NativeExtreme(body,u)+margin;
            double v0=-NativeExtreme(body,Mul(v,-1))-margin,v1=NativeExtreme(body,v)+margin;
            Func<double,double,double[]> point=(y,z)=>Add(Mul(axis,start),Add(Mul(u,y),Mul(v,z)));
            return new AfjToolDefinition{Polygon=new[]{point(u0,v0),point(u1,v0),point(u1,v1),point(u0,v1)},Direction=axis,Length=end-start,DepthPolicy="FORMED_PROFILE_ENVELOPE"};
        }
        static AfjCutPlan Cut(Section section,Body2 body,MathTransform transform,double[] outgoing,double[] origin,double cutoff,string id)
        {
            double[] axis=NativeVector(outgoing,transform);double basis=Dot(AfjParametricRules.Native(origin,transform),axis);
            double lo=-NativeExtreme(body,Mul(axis,-1)),hi=NativeExtreme(body,axis),start=basis+cutoff;
            if(start<=(lo+hi)/2||start>hi+Eps)Fail("cutoff is not on the selected end half");
            var operation=new AfjCutOperation{EvidenceId=id,Kind=AfjOperationKind.Cut,NormalCutIntent=false,RegionEvidence="Actual planar transverse support; axial butt trim across complete measured folded section; partial visual coverage permitted"};
            operation.Tools.Add(AxialBox(body,axis,section.NativeWidth,section.NativeDepth,start,hi+.002));
            var result=new AfjCutPlan();result.Operations.Add(operation);return result;
        }
        static double[] ProfileCenter(AfjProfile profile,Body2 body)
        {
            double[][] ordered=Ordered(profile);var vertices=ordered.Select(l=>P(l,0)).Concat(new[]{P(ordered.Last(),1)}).ToArray();
            double[] center=new[]{vertices.Average(p=>p[0]),vertices.Average(p=>p[1]),vertices.Average(p=>p[2])},axis=Unit(profile.Axis);
            double middle=(NativeExtreme(body,axis)-NativeExtreme(body,Mul(axis,-1)))/2;
            return Add(center,Mul(axis,middle-Dot(center,axis)));
        }
        static double[] Outgoing(AfjProfile profile,Body2 body,MathTransform transform,AfjProfile other,Body2 otherBody,MathTransform otherTransform)
        {
            double[] axis=WorldVector(profile.Axis,transform),center=World(ProfileCenter(profile,body),transform),target=World(ProfileCenter(other,otherBody),otherTransform);
            double delta=Dot(Sub(target,center),axis);if(Math.Abs(delta)<1e-4)Fail("T/interior joint or ambiguous selected end");return Mul(axis,delta>0?1:-1);
        }
        internal static AfjParametricPlan Generate(AfjProfile pa,AfjProfile pb,Body2 ba,Body2 bb,MathTransform ta,MathTransform tb,bool coverA,double gap)
        {
            AfjProfile lp=coverA?pb:pa,wp=coverA?pa:pb;Body2 lb=coverA?bb:ba,wb=coverA?ba:bb;MathTransform lt=coverA?tb:ta,wt=coverA?ta:tb;
            double[] ul=Outgoing(lp,lb,lt,wp,wb,wt),uw=Outgoing(wp,wb,wt,lp,lb,lt);
            if(Math.Abs(Dot(ul,uw))>1e-7)Fail("requires perpendicular axes");
            Section l=Read(lp,lb,lt,uw),w=Read(wp,wb,wt,ul);
            // Derive two perpendicular axial support planes without choosing a
            // Web, a shared Web plane or a longest segment. Their intersection
            // is a coordinate origin; d only locates it along the intersection.
            double[] zero=new double[3],d=Unit(Cross(ul,uw));
            SupportEvidence longCutSupport,shortCutSupport;
            double loserPlane=Support(wb,wt,ul,zero,true,out longCutSupport)-gap,winnerPlane=Support(lb,lt,uw,zero,false,out shortCutSupport);
            double depth=Dot(Mul(Add(l.Origin,w.Origin),.5),d);
            double[] origin=Add(Mul(ul,loserPlane),Add(Mul(uw,winnerPlane),Mul(d,depth)));
            const double loserCut=0,winnerCut=0;
            var loserPlan=Cut(l,lb,lt,ul,origin,loserCut,"FOLDED_RULE:LONG_AXIAL_BUTT");
            var winnerPlan=Cut(w,wb,wt,uw,origin,winnerCut,"FOLDED_RULE:SHORT_AXIAL_COVER");
            double winnerBandStart=-AfjParametricRules.Extreme(lb,lt,Mul(uw,-1),origin)-.001;
            Trace.WriteLine("[AFJ-FOLDED] POLICY strategy=BUTT short_cover_priority partial_cover_permitted=True long_plane_world_m="+loserPlane.ToString("R",CultureInfo.InvariantCulture)+" short_plane_world_m="+winnerPlane.ToString("R",CultureInfo.InvariantCulture));
            return new AfjParametricPlan{Plan=new AfjJointPlan{RuleId=RuleId,A=coverA?winnerPlan:loserPlan,B=coverA?loserPlan:winnerPlan},JointOrigin=origin,OutLoser=ul,OutWinner=uw,Depth=d,
                Folded=new Context{Loser=l,Winner=w,LoserBody=lb,WinnerBody=wb,LoserCut=loserCut,WinnerCut=winnerCut,LoserBandStart=-.001,WinnerBandStart=winnerBandStart,LongCutSupport=longCutSupport,ShortCutSupport=shortCutSupport},
                CoverageWarning="BUTT / PARTIAL_COVER_POSSIBLE: Frame ngắn được ưu tiên che; cắt giáp đầu theo mặt vật lý. Có thể còn cạnh hở; chưa xác minh diện tích che kín.",
                Derivation="Two independent axial prisms from actual B-rep extrema; long cutoff witness="+longCutSupport.Kind+"; short cutoff witness="+shortCutSupport.Kind+"; no associative cut reference; complete ordered folded skins/bends retained; ideal stock checked from two interior native cap extrusions"};
        }
        static Body2 StockSample(ISldWorks app,Section section,Body2 source,double[] axis,double sample,double begin,double end)
        {
            var tool=AfjTemporaryBoolean.CreatePrism(app,AxialBox(source,axis,section.NativeWidth,section.NativeDepth,sample,sample+.0005));int code;
            var slab=AfjTemporaryBoolean.Boolean(source,tool,(int)swBodyOperationType_e.SWBODYINTERSECT,out code,"FOLDED_STOCK_SLICE");
            if(slab.Count!=1)Fail("stock sample is empty or disconnected");
            var raw=slab[0].GetFaces() as Array;if(raw==null)Fail("stock cap faces missing");var caps=raw.Cast<object>().OfType<Face2>().Where(f=>PlaneMatches(f,axis,sample)).ToArray();
            if(caps.Length==0)Fail("stock cap has no native plane at sample support");
            foreach(Face2 face in caps)
            {
                var loops=face.GetLoops() as Array;if(loops==null||loops.Length==0)Fail("cap loops unavailable");
                foreach(Loop2 loop in loops)if(!loop.IsOuter())Fail("interior sample has a hole loop");
            }
            var modeler=app.GetModeler() as Modeler;var math=app.GetMathUtility() as MathUtility;if(modeler==null||math==null)Fail("Modeler unavailable");
            var dispatch=caps.Select(f=>new System.Runtime.InteropServices.DispatchWrapper(f)).ToArray();
            Body2 sheet=modeler.CreateSheetFromFaces(dispatch);
            if(sheet==null||!sheet.IsTemporaryBody()||sheet.GetType()!=(int)swBodyType_e.swSheetBody||sheet.Check2()!=0)Fail("native caps did not form a valid sheet");
            double[] delta=Mul(axis,begin-sample);MathTransform shift=math.CreateTransform(new[]{1d,0,0,0,1,0,0,0,1,delta[0],delta[1],delta[2],1,0,0,0}) as MathTransform;
            if(shift==null||!sheet.ApplyTransform(shift))Fail("stock sheet translation failed");
            Body2 stock=modeler.CreateExtrudedBody(sheet,math.CreateVector(axis) as MathVector,end-begin);AfjTemporaryBoolean.Validate(stock);
            if(Math.Abs(-NativeExtreme(stock,Mul(axis,-1))-begin)>Eps||Math.Abs(NativeExtreme(stock,axis)-end)>Eps)Fail("extruded stock has incorrect axial support");
            VerifyNativeSkins(section,stock);GC.KeepAlive(slab);GC.KeepAlive(sheet);return stock;
        }
        static Body2 IdealStock(ISldWorks app,Section section,Body2 body,MathTransform transform,double[] origin,double[] outgoing,double cutoff,double bandStart)
        {
            double[] axis=NativeVector(outgoing,transform);double lo=-NativeExtreme(body,Mul(axis,-1)),hi=NativeExtreme(body,axis),span=hi-lo;
            if(span<.01)Fail("insufficient clean sampling length");double basis=Dot(AfjParametricRules.Native(origin,transform),axis),begin=basis+Math.Min(bandStart,Math.Min(-.001,cutoff-.001)),end=basis+Math.Max(.001,cutoff+.001);
            Body2 a=StockSample(app,section,body,axis,lo+span*.35,begin,end),b=StockSample(app,section,body,axis,lo+span*.65,begin,end);
            double difference=AfjTemporaryBoolean.DifferenceVolume(a,b)+AfjTemporaryBoolean.DifferenceVolume(b,a);
            if(difference>2e-12)Fail("two interior samples disagree; not a clean constant section");
            Trace.WriteLine("[AFJ-FOLDED] STOCK_TWO_SAMPLES delta_m3="+difference.ToString("R",CultureInfo.InvariantCulture));return a;
        }
        internal static void VerifyCoverage(ISldWorks app,AfjParametricPlan plan,MathTransform ta,MathTransform tb,bool coverA,AfjBooleanResult a,AfjBooleanResult b)
        {
            var c=plan.Folded;if(c==null)Fail("validation context missing");MathTransform lt=coverA?tb:ta,wt=coverA?ta:tb;
            Body2 l=IdealStock(app,c.Loser,c.LoserBody,lt,plan.JointOrigin,plan.OutLoser,c.LoserCut,c.LoserBandStart),w=IdealStock(app,c.Winner,c.WinnerBody,wt,plan.JointOrigin,plan.OutWinner,c.WinnerCut,c.WinnerBandStart);
            var expectedL=AfjTemporaryBoolean.Execute(app,l,coverA?plan.Plan.B:plan.Plan.A);var expectedW=AfjTemporaryBoolean.Execute(app,w,coverA?plan.Plan.A:plan.Plan.B);
            if(expectedL.Kept.Count!=1||expectedW.Kept.Count!=1)Fail("ideal required KEEP disconnected");
            double missingL=AfjTemporaryBoolean.DifferenceVolume(expectedL.Kept[0],(coverA?b:a).Kept[0]),missingW=AfjTemporaryBoolean.DifferenceVolume(expectedW.Kept[0],(coverA?a:b).Kept[0]);
            if(missingL>2e-12||missingW>2e-12)Fail("REQUIRED_KEEP_MISSING long/short mm3="+(missingL*1e9).ToString("R",CultureInfo.InvariantCulture)+"/"+(missingW*1e9).ToString("R",CultureInfo.InvariantCulture));
        }
        internal static void MeasureLongCutExposure(ISldWorks app,AfjParametricPlan plan,AfjJointSolution solution,List<Body2> worldA,List<Body2> worldB)
        {
            // AFJ-FOLDED-LONG-CAP-EXPOSURE-20261022
            // Optional diagnostic. It does not choose a cut or reject an
            // authorized butt merely because the short profile leaves an edge.
            if(plan.Folded==null)return;
            var timer=Stopwatch.StartNew();
            solution.CoverageMeasurementStatus="UNVERIFIED";
            solution.LongCutExposedAreaSquareMetres=null;
            solution.LongCutTotalAreaSquareMetres=null;
            solution.LongCutCoveredAreaSquareMetres=null;
            solution.LongCutExposedBandMinimumMetres=null;
            solution.LongCutExposedBandMaximumMetres=null;
            solution.LongCutExposedBandDirectionWorld=null;
            try
            {
                Body2 longBody=(solution.CoverA?worldB:worldA).Single(),shortBody=(solution.CoverA?worldA:worldB).Single();
                double[] axis=Unit(plan.OutLoser);double support=Dot(plan.JointOrigin,axis)+plan.Folded.LoserCut;
                var raw=longBody.GetFaces() as Array;if(raw==null)Fail("coverage cap faces unavailable");
                var caps=raw.Cast<object>().OfType<Face2>().Where(f=>PlaneMatches(f,axis,support)).ToArray();
                if(caps.Length==0)Fail("coverage actual long Cut cap missing");
                double area=caps.Sum(f=>f.GetArea());if(!AfjTemporaryBoolean.Finite(area)||area<=1e-12)Fail("coverage cap area unavailable");
                var modeler=app.GetModeler() as Modeler;var math=app.GetMathUtility() as MathUtility;if(modeler==null||math==null)Fail("coverage Modeler unavailable");
                Body2 sheet=modeler.CreateSheetFromFaces(caps.Select(f=>new System.Runtime.InteropServices.DispatchWrapper(f)).ToArray());
                if(sheet==null||!sheet.IsTemporaryBody()||sheet.GetType()!=(int)swBodyType_e.swSheetBody||sheet.Check2()!=0)Fail("coverage cap sheet invalid");
                const double probeLength=.00001;
                double displacement=AfjParametricRules.AssemblyGap+plan.Folded.Winner.Thickness/2;
                double[] shift=Mul(axis,displacement);MathTransform move=math.CreateTransform(new[]{1d,0,0,0,1,0,0,0,1,shift[0],shift[1],shift[2],1,0,0,0}) as MathTransform;
                if(move==null||!sheet.ApplyTransform(move))Fail("coverage probe translation failed");
                Body2 probe=modeler.CreateExtrudedBody(sheet,math.CreateVector(axis) as MathVector,probeLength);AfjTemporaryBoolean.Validate(probe);
                double probeArea=AfjTemporaryBoolean.Volume(probe)/probeLength,areaTolerance=Math.Max(1e-10,area*1e-6);
                if(Math.Abs(probeArea-area)>areaTolerance)Fail("coverage probe area differs from exact cap area");
                int code;var covered=AfjTemporaryBoolean.Boolean(probe,shortBody,(int)swBodyOperationType_e.SWBODYINTERSECT,out code,"FOLDED_LONG_CAP_COVER_PROBE");
                double coveredArea=AfjTemporaryBoolean.Total(covered)/probeLength;
                if(!AfjTemporaryBoolean.Finite(coveredArea)||coveredArea<0||coveredArea>area+areaTolerance)Fail("coverage probe intersection invalid");
                coveredArea=Math.Min(area,coveredArea);double exposedArea=Math.Max(0,area-coveredArea);
                if(exposedArea>areaTolerance)
                {
                    var missing=AfjTemporaryBoolean.Boolean(probe,shortBody,(int)swBodyOperationType_e.SWBODYCUT,out code,"FOLDED_LONG_CAP_EXPOSED_PROBE");
                    if(missing.Count==0||Math.Abs(AfjTemporaryBoolean.Total(missing)/probeLength-exposedArea)>areaTolerance)Fail("exposed probe difference disagrees with coverage");
                    // Depth is perpendicular to both member axes. Probe shift
                    // and extrusion leave these coordinates unchanged. This
                    // band describes the exposed long-cap material, rather
                    // than arbitrary visible edges or BodyBox dimensions.
                    double[] depth=Unit(plan.Depth);double minimum=Double.PositiveInfinity,maximum=Double.NegativeInfinity;
                    foreach(Body2 fragment in missing){minimum=Math.Min(minimum,-NativeExtreme(fragment,Mul(depth,-1)));maximum=Math.Max(maximum,NativeExtreme(fragment,depth));}
                    solution.LongCutExposedBandDirectionWorld=depth;
                    solution.LongCutExposedBandMinimumMetres=minimum;
                    solution.LongCutExposedBandMaximumMetres=maximum;
                }
                solution.LongCutTotalAreaSquareMetres=area;
                solution.LongCutCoveredAreaSquareMetres=coveredArea;
                solution.LongCutExposedAreaSquareMetres=exposedArea;
                solution.CoverageMeasurementStatus="GEOMETRY_VERIFIED_PROBE_SECTION";
                solution.CoverageMeasurementDetail="Actual long Cut cap; probe at AssemblyFitGap + t_short/2; delta=0.00001 m; area from Boolean volume/delta. This is section coverage, not all-angle visual occlusion.";
                solution.CoverageWarning="BUTT: Frame ngắn ưu tiên che. Mặt cắt Frame dài "+(area*1e6).ToString("0.###",CultureInfo.InvariantCulture)+" mm²; còn hở "+(exposedArea*1e6).ToString("0.###",CultureInfo.InvariantCulture)+" mm² theo lát kiểm tra B-rep tại giữa bề dày Frame ngắn.";
                Trace.WriteLine("[AFJ-FOLDED] LONG_CUT_COVERAGE status="+solution.CoverageMeasurementStatus+" total_m2="+area.ToString("R",CultureInfo.InvariantCulture)+" covered_m2="+coveredArea.ToString("R",CultureInfo.InvariantCulture)+" exposed_m2="+exposedArea.ToString("R",CultureInfo.InvariantCulture));
                GC.KeepAlive(caps);GC.KeepAlive(sheet);GC.KeepAlive(probe);
            }
            catch(Exception ex)
            {
                solution.LongCutTotalAreaSquareMetres=null;solution.LongCutCoveredAreaSquareMetres=null;solution.LongCutExposedAreaSquareMetres=null;
                solution.LongCutExposedBandMinimumMetres=null;solution.LongCutExposedBandMaximumMetres=null;solution.LongCutExposedBandDirectionWorld=null;
                solution.CoverageMeasurementDetail="HRESULT=0x"+ex.HResult.ToString("X8")+" "+ex.Message;
                solution.CoverageWarning="BUTT / EXPOSURE_UNVERIFIED: Chưa đo được diện tích cạnh hở bằng B-rep; không coi diện tích hở bằng 0. Xem log AFJ-FOLDED.";
                Trace.WriteLine("[AFJ-FOLDED] LONG_CUT_COVERAGE status=UNVERIFIED "+solution.CoverageMeasurementDetail);
            }
            finally{Trace.WriteLine("[AFJ-R52-PERF] phase=long_cut_exposure ms="+timer.ElapsedMilliseconds);}
        }
        internal static double[] SelectedOrigin(AfjParametricPlan plan,bool winner)
        {return Add(plan.JointOrigin,Mul(winner?plan.OutWinner:plan.OutLoser,(winner?plan.Folded.WinnerCut:plan.Folded.LoserCut)+AfjParametricRules.AssemblyGap));}
    }
}


// R53 internal helpers compile in this existing source file; no project include changes.
// R53 native transaction for the existing AFJ command. No automatic Save or deployment.
namespace ADDIN.Commands.AutoFrameJoint
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Collections.Generic;
    using SolidWorks.Interop.sldworks;
    using SolidWorks.Interop.swconst;

    internal sealed class AfjNativeUTransaction
    {
        private sealed class FeatureState
        {
            internal int Id;
            internal string Type, Name;
            internal bool Suppressed;
        }
        private sealed class DocumentState
        {
            internal ModelDoc2 Model;
            internal string Label, Path, Configuration;
            internal HashSet<int> TopIds;
            internal int[] TopOrder;
            internal Dictionary<int, FeatureState> Original;
            internal int AcceptedStamp;
        }
        private sealed class Step
        {
            internal int Part;
            internal AfjCutOperation Operation;
            internal Body2 Before, Target;
            internal double Removed;
        }
        private sealed class RawCreation
        {
            // Mutation/registration accounting is primitive; native ownership
            // belongs to the journal's private persistent reference.
            internal string Frame, Role;
            internal int FeatureId=-1;
            internal bool JournalReturned, ClosedConfirmed;
        }
        internal sealed class SketchCheck
        {
            public string Phase, Frame, EvidenceId;
            public int FeatureId, Status, Relations, Dangling, Overdefining, Dimensions;
            public int ReferenceType, SupportOwnerId, Error;
            public bool Warning, ReferenceIsFace, OriginalSupportOwner;
        }

        private readonly ISldWorks App;
        private readonly ModelDoc2 Assembly;
        private readonly AfjApplyTicket Ticket;
        private readonly AfjJointSolution Preview;
        private readonly AfjParametricPlan Intent;
        private readonly AfjNativeApplyReport Report;
        private readonly string Prefix;
        private readonly Action<string> Sink;
        private readonly DocumentState[] Documents;
        private readonly Component2[] Components;
        private readonly double[][] OriginalTransforms;
        private readonly Body2[] Expected;
        private readonly bool[] OriginalSheetMetal;
        private readonly List<Step> Steps = new List<Step>();
        private readonly List<RawCreation> Raw = new List<RawCreation>();
        private readonly AfjUOwnedFeatureJournal Journal;
        private bool Applied, ApplyStarted, Committed, UnresolvedCreation;

        internal AfjUParametricSketchWriter.WriteResult SketchWriterResult { get; private set; }
        internal readonly List<SketchCheck> SketchChecks = new List<SketchCheck>();
        internal AfjUOwnedFeatureJournal.RollbackReport RollbackResult { get; private set; }
        internal bool HasNativeMutation { get { return Raw.Count != 0 || UnresolvedCreation; } }
        internal bool Complete { get { return Applied; } }
        internal bool IsCommitted { get { return Committed; } }

        // Construction reads and computes independent temporary targets only.
        // The caller must keep this instance before calling Apply so a failure
        // during the very first native Sketch can still reach its journal.
        internal AfjNativeUTransaction(ISldWorks app, ModelDoc2 assembly, AfjApplyTicket ticket,
            AfjParametricPlan intent, AfjNativeApplyReport report, string prefix,
            Action<string> stageSink = null)
        {
            Need(System.Threading.Thread.CurrentThread.GetApartmentState() == System.Threading.ApartmentState.STA,
                "SOLIDWORKS owner STA thread required");
            Need(app != null && assembly != null && ticket != null && ticket.Preview != null && report != null,
                "Bound application, Assembly, reviewed ticket and report required");
            App = app; Assembly = assembly; Ticket = ticket; Preview = ticket.Preview;
            Intent = intent; Report = report; Prefix = prefix; Sink = stageSink;
            Need(intent != null && Object.ReferenceEquals(intent.Plan, Preview.Plan) && intent.Folded == null,
                "Use the exact construction intent retained by the reviewed Solve; do not regenerate on OK");
            Need(intent.Plan.RuleId == "ORTHOGONAL_U_COVER" || intent.Plan.RuleId == "ORTHOGONAL_U_TALLER_COVER",
                "The constrained-U transaction cannot handle this rule");
            Need(!String.IsNullOrWhiteSpace(prefix) && prefix.StartsWith("AFJ_", StringComparison.Ordinal) &&
                prefix.Length <= 48 && prefix.All(c => Char.IsLetterOrDigit(c) || c == '_'), "Unique AFJ_ prefix required");
            Need(Same(App.ActiveDoc, Assembly) && Assembly is AssemblyDoc &&
                Same(((AssemblyDoc)Assembly).GetEditTarget(), Assembly), "Active reviewed Assembly outside component edit required");
            Need(Assembly.GetActiveSketch2() == null, "Close existing Sketch before starting a transaction");
            Components = new[] { ticket.FrameA, ticket.FrameB };
            Need(Components.All(c => c != null) && !Same(Preview.PartA, Preview.PartB), "Independent occurrences/Parts required");
            var parts = new[] { Preview.PartA, Preview.PartB };
            for (int i = 0; i < 2; i++)
                Need(parts[i] != null && Same(Components[i].GetModelDoc2(), parts[i]) &&
                    parts[i].GetActiveSketch2() == null, "Reviewed occurrence/Part identity or existing Sketch differs");
            AfjUParametricSketchWriter.ValidateIntent(intent); // Complete geometric recipe check before journal/native mutation.
            OriginalTransforms = Components.Select(c => CloneTransform(c.GetTotalTransform(false))).ToArray();
            Expected = new[] { AfjTemporaryBoolean.Copy(ticket.BeforeA), AfjTemporaryBoolean.Copy(ticket.BeforeB) };
            OriginalSheetMetal = parts.Select(p => AfjNativeCutCommit.CurrentBody(p).IsSheetMetal()).ToArray();
            Documents = new[] { Capture(parts[0], "A"), Capture(parts[1], "B"), Capture(Assembly, "ASSEMBLY") };
            Report.GapMetres = Double.NaN; Report.InterferenceCubicMetres = Double.NaN;
            CheckInvariant("BEFORE_TEMPORARY_TARGETS");
            PrepareSteps();
            Need(ticket.IsSourceCurrent != null && ticket.IsSourceCurrent(), "PREVIEW_STALE before first mutation");
            CheckInvariant("BEFORE_JOURNAL");
            Journal = new AfjUOwnedFeatureJournal(app, assembly, parts[0], parts[1], Expected[0], Expected[1], Log);
            AcceptStamps();
        }

        private static void Need(bool ok, string reason)
        { if (!ok) throw new InvalidOperationException("AFJ_U_TRANSACTION_UNVERIFIED: " + reason); }
        private bool Same(object a, object b)
        { return a != null && b != null && App.IsSame(a, b) == (int)swObjectEquality.swObjectSame; }
        // Logging must never become a new failure between native creation and
        // ownership token capture, or while a rollback is already running.
        private void Log(string phase)
        {
            try { Trace.WriteLine("[AFJ-U-TRANSACTION] " + phase); } catch { }
            try { if (Sink != null) Sink(phase); } catch { }
        }
        private static double[] CloneTransform(MathTransform transform)
        {
            var v = transform == null ? null : transform.ArrayData as double[];
            Need(v != null && v.Length == 16 && v.All(AfjTemporaryBoolean.Finite), "Native rigid transform unavailable");
            return (double[])v.Clone();
        }
        private static Dictionary<int, FeatureState> Features(ModelDoc2 part, out HashSet<int> top, out int[] topOrder)
        {
            // Native absorbed Sketches are included by the flat API. Do not recurse
            // GetFirstSubFeature/GetNextSubFeature through an edited feature tree.
            top = new HashSet<int>(); var orderedTop = new List<int>(); var topFeatures=new Dictionary<int,Feature>(); int count = 0;
            for (Feature feature=part.FirstFeature() as Feature; feature!=null; feature=feature.GetNextFeature() as Feature)
            {
                Need(++count < 2400, "Top-level Feature count bound exceeded");
                int topId=feature.GetID(); Need(top.Add(topId), "Duplicate top-level Feature identity"); orderedTop.Add(topId); topFeatures.Add(topId,feature);
            }
            var native = part.FeatureManager.GetFeatures(false) as Array;
            Need(native != null && native.Length > 0 && native.Length < 2400, "Flat native Feature inventory unavailable");
            var result = new Dictionary<int, FeatureState>();
            foreach (object item in native)
            {
                var feature=item as Feature; Need(feature!=null, "Flat inventory contains a non-Feature entry");
                int id=feature.GetID();
                var row=new FeatureState { Id=id, Type=feature.GetTypeName2(), Name=feature.Name, Suppressed=feature.IsSuppressed() };
                FeatureState existing;
                if (result.TryGetValue(id,out existing))
                {
                    Need(existing.Type==row.Type && existing.Name==row.Name && existing.Suppressed==row.Suppressed,
                        "Duplicate flat Feature identity has conflicting native state");
                    continue;
                }
                result.Add(id,row);
            }
            foreach(var pair in topFeatures)
            {
                var row=new FeatureState{Id=pair.Key,Type=pair.Value.GetTypeName2(),Name=pair.Value.Name,Suppressed=pair.Value.IsSuppressed()};
                FeatureState flat;
                if(result.TryGetValue(pair.Key,out flat))
                    Need(flat.Type==row.Type&&flat.Name==row.Name&&flat.Suppressed==row.Suppressed,
                        "Conflicting top/flat Feature identity; id="+pair.Key);
                else result.Add(pair.Key,row);
            }
            var sheets=native.Cast<object>().OfType<Feature>().Concat(topFeatures.Values)
                .Where(feature=>feature.GetTypeName2()=="SheetMetal").GroupBy(feature=>feature.GetID()).Select(group=>group.First()).ToArray();
            foreach(var sheet in sheets)
            {
                var parents=sheet.GetParents() as Array;Need(parents==null||parents.Length<=512,"SheetMetal parent count bound");
                if(parents==null)continue;
                foreach(object value in parents)
                {
                    var parent=value as Feature;Need(parent!=null,"SheetMetal has unresolved native parent");
                    if(parent.GetTypeName2()!="TemplateSheetMetal")continue;
                    var row=new FeatureState{Id=parent.GetID(),Type=parent.GetTypeName2(),Name=parent.Name,Suppressed=parent.IsSuppressed()};
                    FeatureState existing;
                    if(result.TryGetValue(row.Id,out existing))
                        Need(existing.Type==row.Type&&existing.Name==row.Name&&existing.Suppressed==row.Suppressed,
                            "Conflicting native SheetMetal template parent; id="+row.Id);
                    else result.Add(row.Id,row);
                }
            }
            Need(result.Count<2400,"Native inventory with SheetMetal parent closure exceeds bound");
            topOrder=orderedTop.ToArray(); return result;
        }
        private static DocumentState Capture(ModelDoc2 part, string label)
        {
            HashSet<int> top; int[] topOrder; var original = Features(part, out top, out topOrder); var configuration = part.ConfigurationManager.ActiveConfiguration;
            Need(configuration != null, "Active configuration unavailable");
            return new DocumentState { Model = part, Label = label, Path = part.GetPathName(),
                Configuration = configuration.Name, Original = original, TopIds = top, TopOrder = topOrder, AcceptedStamp = part.GetUpdateStamp() };
        }
        private Feature Find(ModelDoc2 part, int id)
        {
            // First resolve a known owned name, then require its immutable native ID.
            // Names are only a lookup accelerator and are never ownership evidence.
            var owned=SketchWriterResult==null ? null : SketchWriterResult.Owned.SingleOrDefault(
                item=>item.FeatureId==id && Same(item.Part,part));
            if (owned!=null)
            {
                var nativePart=part as PartDoc; Need(nativePart!=null, "Owned Sketch lookup requires Part document"); var named=nativePart.FeatureByName(owned.IntendedName) as Feature;
                if (named!=null && named.GetID()==id) return named;
            }
            var native=part.FeatureManager.GetFeatures(false) as Array;
            Need(native!=null && native.Length < 2400, "Exact owned Feature inventory unavailable");
            Feature found=null;
            foreach (object item in native)
            {
                var feature=item as Feature; Need(feature!=null, "Flat lookup contains a non-Feature entry");
                if (feature.GetID()!=id) continue;
                Need(found==null, "Exact owned Feature ID was duplicated"); found=feature;
            }
            return found;
        }
        private static string DescribeUnregistered(ModelDoc2 document,int[] ids)
        {
            // Failure-only live diagnostics. Never convert an unexplained native
            // delta into owned state or recurse its native feature tree.
            var descriptions=new List<string>(); var expected=new HashSet<int>(ids);
            try
            {
                var raw=document.FeatureManager.GetFeatures(false) as Array;
                if(raw==null || raw.Length>=2400)return "FLAT_INVENTORY_UNAVAILABLE";
                foreach(object value in raw)
                {
                    var feature=value as Feature; if(feature==null)continue;
                    int id=feature.GetID(); if(!expected.Contains(id))continue;
                    string parents="NOT_AVAILABLE",specific="NOT_AVAILABLE";
                    try
                    {
                        var nativeParents=feature.GetParents() as Array;
                        if(nativeParents==null)parents="NONE";
                        else if(nativeParents.Length>128)parents="COUNT_BOUND_EXCEEDED";
                        else parents=String.Join(",",nativeParents.Cast<object>().Select(parent=>
                        {
                            var native=parent as Feature;
                            return native==null?"NON_FEATURE":native.GetID()+":"+native.GetTypeName2()+":"+native.Name;
                        }));
                    }
                    catch(Exception error){parents="HRESULT=0x"+error.HResult.ToString("X8")+":"+error.Message;}
                    try
                    {
                        var native=feature.GetSpecificFeature2();
                        specific=native==null?"NULL":native.GetType().FullName+";isSketch="+(native is Sketch)+";isFeature="+(native is Feature)+";isComponent="+(native is Component2);
                    }
                    catch(Exception error){specific="HRESULT=0x"+error.HResult.ToString("X8")+":"+error.Message;}
                    descriptions.Add("id="+id+";type="+feature.GetTypeName2()+";name="+feature.Name+";parents=["+parents+"];specific=["+specific+"]");
                }
            }
            catch(Exception error){descriptions.Add("DIAGNOSTIC_HRESULT=0x"+error.HResult.ToString("X8")+":"+error.Message);}
            return String.Join(" | ",descriptions);
        }
        private void AcceptStamps()
        { foreach (var state in Documents) state.AcceptedStamp = state.Model.GetUpdateStamp(); }
        private void RequireAcceptedStamps()
        {
            foreach (var state in Documents)
                Need(state.Model.GetUpdateStamp() == state.AcceptedStamp,
                    "Unexpected native update after accepted checkpoint in " + state.Label);
        }
        private void CheckInvariant(string phase)
        {
            Log(phase + ".INVARIANTS_BEGIN");
            for (int i = 0; i < Documents.Length; i++)
            {
                var state = Documents[i]; var configuration = state.Model.ConfigurationManager.ActiveConfiguration;
                Need(configuration != null && configuration.Name == state.Configuration &&
                    String.Equals(state.Model.GetPathName(), state.Path, StringComparison.OrdinalIgnoreCase),
                    "Document/configuration identity changed in " + state.Label);
                HashSet<int> top; int[] topOrder; var now = Features(state.Model, out top, out topOrder);
                if(Journal!=null)foreach(var native in Journal.GetKnownOwnedPassiveNativeFeatures(state.Model))
                {
                    var row=new FeatureState{Id=native.GetID(),Type=native.GetTypeName2(),Name=native.Name,Suppressed=native.IsSuppressed()};
                    FeatureState prior;
                    if(now.TryGetValue(row.Id,out prior))
                        Need(prior.Type==row.Type&&prior.Name==row.Name&&prior.Suppressed==row.Suppressed,
                            "Current native inventory conflicts with exact owned passive token; id="+row.Id);
                    else now.Add(row.Id,row);
                }
                Need(state.TopIds.All(top.Contains) && topOrder.Where(state.TopIds.Contains).SequenceEqual(state.TopOrder),
                    "Protected original top-level Feature order changed in " + state.Label);
                foreach (var original in state.Original.Values)
                {
                    FeatureState current;
                    Need(now.TryGetValue(original.Id, out current) && current.Type == original.Type &&
                        current.Name == original.Name && current.Suppressed == original.Suppressed,
                        "Protected original Feature changed in " + state.Label + "; id="+original.Id+
                        "; before="+original.Type+":"+original.Name+":"+original.Suppressed+
                        "; current="+(current==null?"MISSING":current.Type+":"+current.Name+":"+current.Suppressed));
                }
                var owned = Journal == null ? new HashSet<int>() : new HashSet<int>(Journal.GetPublicOwnedFeatures()
                    .Where(e => e.Document == state.Label && !e.Removed).Select(e => e.Id));
                var unregistered=now.Keys.Where(id=>!state.Original.ContainsKey(id) && !owned.Contains(id)).ToArray();
                if(unregistered.Length!=0)
                    Need(false,"Unregistered native Feature delta in " + state.Label + "; " + DescribeUnregistered(state.Model,unregistered));
                Need(owned.All(now.ContainsKey), "Registered owned Feature disappeared in " + state.Label);
            }
            for (int i = 0; i < 2; i++)
            {
                var state = Documents[i]; Need(Same(Components[i].GetModelDoc2(), state.Model) &&
                    Components[i].ReferencedConfiguration == state.Configuration, "Occurrence/configuration changed");
                var matrix = CloneTransform(Components[i].GetTotalTransform(false));
                Need(matrix.Where((v, k) => Math.Abs(v - OriginalTransforms[i][k]) > 1e-10).Count() == 0,
                    "Occurrence transform changed");
                var current = AfjNativeCutCommit.CurrentBody(state.Model);
                // CurrentBody is a persisted Part body. The Boolean validator
                // deliberately accepts temporary bodies only; validate its copy.
                AfjTemporaryBoolean.Validate(AfjTemporaryBoolean.Copy(current));
                Need(current.IsSheetMetal() == OriginalSheetMetal[i], "Sheet-metal identity changed");
                double delta = AfjNativeCutCommit.SymmetricDifference(AfjTemporaryBoolean.Copy(current), Expected[i]);
                Need(AfjTemporaryBoolean.Finite(delta) && delta <= 1e-12, "Unplanned Body change in " + state.Label);
            }
            Log(phase + ".INVARIANTS_END");
        }
        private void PrepareSteps()
        {
            var running = Expected.Select(AfjTemporaryBoolean.Copy).ToArray(); int winner = Preview.CoverA ? 0 : 1;
            foreach (int i in new[] { 1 - winner, winner })
            {
                var plan = i == 0 ? Preview.Plan.A : Preview.Plan.B;
                foreach (var operation in plan.Operations)
                {
                    Need(operation.Kind == AfjOperationKind.Cut && operation.Tools.Count == 1 &&
                        !String.IsNullOrEmpty(operation.EvidenceId), "Independent verified single prism operation required");
                    var stepPlan = new AfjCutPlan(); stepPlan.Operations.Add(operation);
                    var target = AfjTemporaryBoolean.Execute(App, AfjTemporaryBoolean.Copy(running[i]), stepPlan);
                    Need(target.Kept.Count == 1, "Each intermediate native Cut must keep one solid");
                    if (target.RemovedVolume <= 1e-13) continue;
                    Steps.Add(new Step { Part = i, Operation = operation, Before = running[i],
                        Target = target.Kept.Single(), Removed = target.RemovedVolume });
                    running[i] = target.Kept.Single();
                }
            }
            Need(Steps.Count == 4 && Steps.Select(s => s.Operation.EvidenceId).Distinct().Count() == 4,
                "Only the native-qualified four nonempty U operations are admitted");
            for (int i = 0; i < 2; i++)
            {
                var target = i == 0 ? Preview.A.Kept.Single() : Preview.B.Kept.Single();
                Need(AfjNativeCutCommit.SymmetricDifference(running[i], target) <= 1e-12,
                    "Sequential operation targets differ from reviewed final Preview");
            }
        }
        private void Register(ModelDoc2 part, Feature feature, string role)
        {
            var raw = new RawCreation { Role = role }; Raw.Add(raw);
            try
            {
                // Capture exact ownership first; no new COM readback precedes it.
                var captured=Journal.RegisterNewNativeFeature(part, feature, role);
                raw.Frame=captured.Document;raw.FeatureId=captured.Id;raw.JournalReturned=true;
            }
            catch { UnresolvedCreation = true; throw; }
        }
        private void ConfirmClosed(ModelDoc2 part,Feature feature,string role)
        {
            try
            {
                var captured=Journal.ConfirmClosedNativeFeature(part,feature,role,Prefix);
                var raw=Raw.SingleOrDefault(item=>item.Frame==captured.Document && item.FeatureId==captured.Id && item.Role==role);
                Need(raw!=null && raw.JournalReturned,"Closed native Feature was not provisionally registered");
                raw.ClosedConfirmed=true;
            }
            catch { UnresolvedCreation=true; throw; }
        }
        private void Activate(ModelDoc2 document)
        {
            Need(Same(((AssemblyDoc)Assembly).GetEditTarget(), Assembly) && Assembly.GetActiveSketch2() == null,
                "Native Cut requires restored Assembly edit context");
            if (Same(App.ActiveDoc, document)) return;
            int error = 0; var actual = App.ActivateDoc3(document.GetTitle(), false,
                (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref error) as ModelDoc2;
            Need(Same(actual, document) && (error & ~(int)swActivateDocError_e.swDocNeedsRebuildWarning) == 0,
                "Cannot activate exact native target");
        }
        internal void Apply()
        {
            Need(!ApplyStarted && !Committed, "Transaction cannot be reused"); ApplyStarted = true;
            RequireAcceptedStamps(); CheckInvariant("BEFORE_FIRST_NATIVE_SKETCH");
            Need(Ticket.IsSourceCurrent != null && Ticket.IsSourceCurrent(), "PREVIEW_STALE before first mutation");
            int winner = Preview.CoverA ? 0 : 1, loser = 1 - winner;
            SketchWriterResult = AfjUParametricSketchWriter.TryWrite(App, Assembly, Components[loser], Components[winner],
                Intent, Documents[loser].TopIds, Documents[winner].TopIds, Prefix, Register, Log, ConfirmClosed);
            // InsertSketch may mutate native history before a Feature interface
            // is available. Keep that uncertainty on the transaction itself so
            // the caller cannot skip rollback merely because Raw is still empty.
            UnresolvedCreation |= SketchWriterResult.UnresolvedCreation;
            Need(SketchWriterResult.Success && !SketchWriterResult.UnresolvedCreation && SketchWriterResult.ContextRestored,
                "Native U Sketch writer failed: " + SketchWriterResult.Error);
            Journal.CaptureOwnedAssemblyContextFeatures(Components[0],Components[1],Prefix);
            CheckInvariant("AFTER_FOUR_NATIVE_SKETCHES"); CheckSketches("BEFORE_ANY_CUT"); AcceptStamps();
            foreach (var step in Steps)
            {
                RequireAcceptedStamps(); CheckInvariant("BEFORE_CUT_" + step.Operation.EvidenceId);
                var owned = SketchWriterResult.Owned.Single(o => o.EvidenceId == step.Operation.EvidenceId);
                Need(Same(owned.Part, Documents[step.Part].Model), "Prepared Sketch/operation Part mapping differs");
                ApplyCut(step, owned); Expected[step.Part] = step.Target;
                CheckSketches("AFTER_CUT_" + step.Operation.EvidenceId);
                CheckInvariant("AFTER_CUT_" + step.Operation.EvidenceId); AcceptStamps();
#if AFJ_U_LINKED_WRITER_HARNESS
                // Detached native harness only; this injection is absent from Add-in compilation.
                if (Report.Cuts.Count==4 && System.Environment.GetEnvironmentVariable("AFJ_R53_APPLY_TEST_MODE")=="FORCE_ROLLBACK")
                    throw new InvalidOperationException("R53_CONTROLLED_AFTER_FOUR_VERIFIED_CUTS");
#endif
            }
            Activate(Assembly); CheckSketches("PAIR_FINAL"); CheckInvariant("PAIR_FINAL");
            var wa = AfjJointSolver.World(new[] { AfjTemporaryBoolean.Copy(AfjNativeCutCommit.CurrentBody(Documents[0].Model)) }, Components[0].GetTotalTransform(false));
            var wb = AfjJointSolver.World(new[] { AfjTemporaryBoolean.Copy(AfjNativeCutCommit.CurrentBody(Documents[1].Model)) }, Components[1].GetTotalTransform(false));
            Report.GapMetres = AfjJointSolver.Gap(Assembly, wa, wb); Report.InterferenceCubicMetres = AfjJointSolver.Overlap(wa, wb);
            Need(Report.InterferenceCubicMetres <= 1e-12 && Math.Abs(Report.GapMetres - AfjManufacturingConventions.AssemblyFitGap) <= 2e-7,
                "Final native pair fit/interference failed");
            Need(Report.Cuts.Count == 4 && Raw.All(r => r.JournalReturned && r.ClosedConfirmed), "Native ownership/result count incomplete");
            Applied = true; AcceptStamps(); Log("PAIR_GEOMETRY_AND_SKETCHES_VERIFIED_NOT_YET_COMMITTED");
        }
        private void ApplyCut(Step step, AfjUParametricSketchWriter.OwnedSketch owned)
        {
            var part = Documents[step.Part].Model; Activate(part); var feature = Find(part, owned.FeatureId);
            Need(feature != null, "Exact owned profile disappeared before Cut"); var sketch = feature.GetSpecificFeature2() as Sketch;
            Need(sketch != null && sketch.GetConstrainedStatus() == (int)swConstrainedStatus_e.swFullyConstrained, "Fully constrained native Sketch required");
            var spec = step.Operation.Tools.Single(); var inverse = sketch.ModelToSketchTransform.Inverse() as MathTransform;
            Need(inverse != null, "Sketch inverse unavailable"); var matrix = inverse.ArrayData as double[];
            var origin = AfjProfileAnalyzer.Transform(new double[3], matrix);
            var normal = AfjParametricRules.Unit(AfjParametricRules.Sub(AfjProfileAnalyzer.Transform(new[] { 0d, 0d, 1d }, matrix), origin));
            bool reverse = AfjNativeCutCommit.ReverseForAlignment(AfjParametricRules.Dot(normal, spec.Direction));
            bool normalCut = AfjNativeCutCommit.UseNativeNormalCut(step.Operation, spec);
            double signedOffset = AfjParametricRules.Dot(AfjParametricRules.Sub(spec.Polygon[0], origin), normal), offset = Math.Abs(signedOffset);
            bool offsetReverse = signedOffset < 0;
            Need(Math.Abs(offset - AfjManufacturingConventions.CutterEnvelopeOverrun) < 1e-8, "Reviewed start offset differs from calibrated native representation");
            string ownedSketchName=feature.Name;
            var assemblyFactoryCheckpoint=Journal.CaptureCheckpoint(Assembly);
            var factoryCheckpoint=Journal.CaptureCheckpoint(part); // no native modeling factory call occurs between this read and FeatureCut4 below.
            part.ClearSelection2(true); Need(feature.Select2(false, 0), "Cannot select exact owned Sketch");
            // Native selection owns the selected profile for FeatureCut4. These
            // pre-Cut aliases must not be queried or retained after mutation.
            feature=null;sketch=null;inverse=null;
            // Register the returned native object immediately, without any log,
            // rename, readback, file I/O or callback between return and capture.
            Feature cut;
            try
            {
                cut = part.FeatureManager.FeatureCut4(true, false, reverse, (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind,
                    spec.Length, spec.Length, false, false, false, false, 0, 0, false, false, false, false, normalCut, true, true,
                    false, false, false, (int)swStartConditions_e.swStartOffset, offset, offsetReverse, normalCut);
            }
            catch
            {
                // A native call can throw after mutation; without a returned
                // Feature identity the journal must not guess new ownership.
                UnresolvedCreation = true; throw;
            }
            if (cut == null)
            {
                UnresolvedCreation = true;
                Need(false, "FeatureCut4 returned null; no Cut identity available");
            }
            Register(part, cut, step.Operation.EvidenceId + ":CUT");
            cut.Name = owned.IntendedName + "_Cut";
            ConfirmClosed(part,cut,step.Operation.EvidenceId + ":CUT");
            var section=step.Part==(Preview.CoverA?0:1)?Intent.Winner:Intent.Loser;
            Journal.CaptureCutFactoryCompanions(part,factoryCheckpoint,cut,step.Operation.EvidenceId,section.Thickness,section.Radius);
            Journal.CaptureCutFactoryAssemblyContextFeatures(Components[0],Components[1],Prefix,assemblyFactoryCheckpoint,part,cut);
            bool warning; int error = cut.GetErrorCode2(out warning); var data = cut.GetDefinition() as ExtrudeFeatureData2;
            Need(error == 0 && data != null && data.GetEndCondition(true) == (int)swEndConditions_e.swEndCondBlind &&
                Math.Abs(data.GetDepth(true) - spec.Length) < 1e-8 && data.ReverseDirection == reverse && data.NormalCut == normalCut &&
                !data.FlipSideToCut && data.FromType == (int)swExtrudeFrom_e.swExtrudeFrom_Offset &&
                Math.Abs(data.FromOffsetDistance - offset) < 1e-8 && data.FromOffsetReverse == offsetReverse,
                "Native Cut definition differs from reviewed operation");
            double delta = AfjNativeCutCommit.SymmetricDifference(AfjTemporaryBoolean.Copy(AfjNativeCutCommit.CurrentBody(part)), step.Target);
            Need(AfjTemporaryBoolean.Finite(delta) && delta <= 1e-12, "Native Cut differs from independent sequential target");
            var measured = MeasureSketch(part, owned, "CONSUMED_BY_NATIVE_CUT");
            Report.Cuts.Add(new AfjNativeCutEvidence { Frame = Documents[step.Part].Label, EvidenceId = step.Operation.EvidenceId,
                FeatureName = cut.Name, FeatureType = cut.GetTypeName2(), UnderlyingType = cut.GetTypeName(),
                DepthPolicy = spec.DepthPolicy, EndCondition = data.GetEndCondition(true), NormalCut = data.NormalCut,
                FlipSideToCut = data.FlipSideToCut, ReverseDirection = data.ReverseDirection, DepthMetres = data.GetDepth(true),
                RemovedCubicMetres = step.Removed, SymmetricDifferenceCubicMetres = delta,
                NativeRepresentation = "FACE_ATTACHED_U_DATUM_PROFILE", PlanCutterLengthMetres = spec.Length,
                NativeActualDepthMetres = data.GetDepth(true), SketchName = ownedSketchName, SketchSupport = "ORIGINAL_OUTER_WEB_FACE",
                SketchReferenceType = measured.ReferenceType, SketchConstrainedStatus = measured.Status,
                SketchRelationCount = measured.Relations, SketchDrivingDimensions = measured.Dimensions, NewReferencePlanes = 0,
                SketchReferenceQualification = "API_FACE_REFERENCE_TYPE_2_ORIGINAL_OWNER;DIRECT_GEOMETRIC_RELATIONS;NO_CONVERT_OR_FIX",
                SketchObservedReferenceType = measured.ReferenceType, SketchSupportOwnerId = measured.SupportOwnerId,
                SketchObservedReferenceIsFace = measured.ReferenceIsFace, SketchPlaneVerified = true });
            // Primitive report construction is complete before the next Cut.
            data=null;cut=null;
            Log("NATIVE_CUT_VERIFIED " + Documents[step.Part].Label + " " + step.Operation.EvidenceId);
        }
        private SketchCheck MeasureSketch(ModelDoc2 part, AfjUParametricSketchWriter.OwnedSketch owned, string phase)
        {
            int index = Same(part, Documents[0].Model) ? 0 : 1; var feature = Find(part, owned.FeatureId);
            Need(feature != null && owned.ClosedOwnerVerified, "Cannot resolve closed owned Sketch");
            var sketch = feature.GetSpecificFeature2() as Sketch; Need(sketch != null && !sketch.Is3D(), "Owned 2D Sketch interface unavailable");
            int referenceType = 0; var face = sketch.GetReferenceEntity(ref referenceType) as Face2;
            var owner = face == null ? null : face.GetFeature() as Feature; bool warning; int error = feature.GetErrorCode2(out warning), dimensions = 0;
            for (var d = feature.GetFirstDisplayDimension() as DisplayDimension; d != null; d = feature.GetNextDisplayDimension(d) as DisplayDimension)
                Need(++dimensions < 64, "Sketch dimension traversal bound");
            var row = new SketchCheck { Phase = phase, Frame = Documents[index].Label, EvidenceId = owned.EvidenceId,
                FeatureId = owned.FeatureId, Status = sketch.GetConstrainedStatus(),
                Relations = sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swAll),
                Dangling = sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swDangling),
                Overdefining = sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swOverDefining),
                Dimensions = dimensions, ReferenceType = referenceType, SupportOwnerId = owner == null ? -1 : owner.GetID(),
                Error = error, Warning = warning, ReferenceIsFace = face != null,
                OriginalSupportOwner = owner != null && Documents[index].Original.ContainsKey(owner.GetID()) };
            int wanted = owned.EvidenceId == "U_RULE:COVER_WEB_WALL" || owned.EvidenceId == "U_RULE:LOSER_STEPPED_WEB_WALL_RETURN" ? 1 : 0;
            SketchChecks.Add(row);
            Need(row.Status == (int)swConstrainedStatus_e.swFullyConstrained && row.Dangling == 0 && row.Overdefining == 0 &&
                dimensions == wanted && error == 0 && face != null && referenceType == 2 && row.OriginalSupportOwner,
                "Owned Sketch constraint/support graph failed after " + phase);
            return row;
        }
        internal void CheckSketches(string phase)
        {
            Need(SketchWriterResult != null && SketchWriterResult.Owned.Count == 4, "Four owned Sketches not prepared");
            foreach (var owned in SketchWriterResult.Owned) MeasureSketch(owned.Part, owned, phase);
        }
        internal void Commit()
        {
            Need(Applied && !Committed && !UnresolvedCreation, "Only a fully applied owned transaction can commit");
            RequireAcceptedStamps(); CheckSketches("BEFORE_COMMIT"); CheckInvariant("BEFORE_COMMIT");
            Need(Same(App.ActiveDoc, Assembly) && Same(((AssemblyDoc)Assembly).GetEditTarget(), Assembly) &&
                Assembly.GetActiveSketch2() == null, "Restore Assembly context before committing");
            Committed = true; Log("COMMITTED_NO_SAVE");
        }
        internal AfjUOwnedFeatureJournal.RollbackReport Rollback()
        {
            Need(!Committed, "Committed transaction cannot be reused for rollback");
            RollbackResult = Journal.Rollback();
            if (UnresolvedCreation || Raw.Any(r => !r.JournalReturned || !r.ClosedConfirmed))
            {
                RollbackResult.Verified = false; RollbackResult.Status = "ROLLBACK_UNVERIFIED";
                RollbackResult.Error += " Unresolved creation or incomplete immediate ownership capture";
            }
            Report.RollbackVerified = RollbackResult.Verified;
            Report.Rollback.Add("CONSTRAINED_U_GLOBAL_JOURNAL " + RollbackResult.Status);
            if (!String.IsNullOrEmpty(RollbackResult.Error)) Report.Rollback.Add(RollbackResult.Error);
            return RollbackResult;
        }
    }
}


// R53 face-attached linked U Sketch writer. Two shared manufacturing dimensions.
// No new RefPlane, Convert, FIX, coordinate dimension ladder, Save or deployment.
namespace ADDIN.Commands.AutoFrameJoint
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using SolidWorks.Interop.sldworks;
    using SolidWorks.Interop.swconst;

    internal sealed class AfjUParametricSketchWriter
    {
        internal sealed class Datum
        {
            internal Component2 Component;
            internal object Native,Context;
            internal string Kind,Role;
            internal int OwnerFeatureId;
            internal double[] PlaneWorld,SeedWorld;
            internal bool SeedOnFiniteTrim;
            internal double ClosestDistance;
        }
        internal sealed class PointEvidence
        {
            internal string Role,PointId;
            internal double[] ExpectedWorld,ExpectedSketch,ObservedSketch;
            internal Datum First,Second;
            internal int[] AllowedFirst,AllowedSecond;
            internal int FirstRelationType,SecondRelationType;
            internal string Qualification;
        }
        internal sealed class SketchReport
        {
            internal string Status="UNVERIFIED",Error,CoordinateContext="ASSEMBLY_WORLD_TO_TARGET_PART_TO_SKETCH",Scope="TWO_CAP_SKETCHES_ACTUAL_MASTER_LOSER_REFS_ZERO_DIM_NO_CUT";
            internal bool NativeApi,InstalledAddin=false,CutCreated=false,SaveApiCalled=false,OwnSupportVerified,SketchOwnerVerified,ContextRestored;
            internal bool CriticalSeamNearMatchesPlan,AllPointsFullyConstrained,NoDimensions,NoNewPlanes;
            internal int SketchFeatureId=-1,SupportReferenceType=-1,ConstraintStatus=-1,Relations=-1,Dangling=-1,Overdefining=-1;
            internal int NewPlanes=-1,Dimensions=-1,ExternalReferences=-1;
            internal string SketchName;
            internal readonly List<PointEvidence> Points=new List<PointEvidence>();
            internal int MasterFeatureId,LoserFeatureId,MasterDefinitionIdentitiesVerified;
            internal readonly List<CapEvidence> Caps=new List<CapEvidence>();
            internal int ReacquiredSegmentCount,ReacquiredIsolatedPointCount;
            internal string ReacquisitionSource;
            internal readonly List<string> ReacquiredSegmentIds=new List<string>();
            internal readonly List<string> ReacquiredIsolatedPointIds=new List<string>();
            internal double GapValue;
            internal string GapDimensionName;
            internal bool MasterSeamParallelVerified,MasterGapFootCoincidentVerified,SeedGeometryUnchanged;
            internal readonly List<Datum> GapDatums=new List<Datum>();
            internal int FullProfileVertexCount,AxisRelations,MarginEqualRelations,ClosureCount;
            internal int ProfileVertexCount,CriticalDatumRelations,ClosureGuides,EqualMarginRelations;
            internal double ToolClosureMargin,MarginDimensionValue;
            internal string AxialClosureDatum,MarginDimensionName;
            internal bool SeedPolygonUnchanged;
            internal double SharedMarginValue;
            internal bool MasterMarginIdentityVerified;
            internal readonly List<Datum> ClosureDatums=new List<Datum>();
            internal readonly List<string> MarginMasterDefinitionIds=new List<string>();
            internal readonly List<string> Stages=new List<string>();
            internal readonly List<string> MasterDefinitionEntityTypes=new List<string>();
        }
        static void Need(bool b,string reason){if(!b)throw new InvalidOperationException("U_GATE2_UNVERIFIED: "+reason);}
        static double Dot(double[] a,double[] b){return AfjParametricRules.Dot(a,b);}
        static double[] Add(double[] a,double[] b){return AfjParametricRules.Add(a,b);}
        static double[] Sub(double[] a,double[] b){return AfjParametricRules.Sub(a,b);}
        static double[] Mul(double[] a,double b){return AfjParametricRules.Scale(a,b);}
        static double Norm(double[] a){return Math.Sqrt(Dot(a,a));}
        static double[] Unit(double[] a){return AfjParametricRules.Unit(a);}
        static double[] Point(ISldWorks app,double[] p,MathTransform t){var math=app.GetMathUtility() as MathUtility;Need(math!=null,"MathUtility unavailable");var q=math.CreatePoint(p) as MathPoint;return ((MathPoint)q.MultiplyTransform(t)).ArrayData as double[];}
        static double[] World(ISldWorks app,double[] p,Component2 c){return Point(app,p,c.GetTotalTransform(false));}
        static double[] WorldVector(ISldWorks app,double[] p,Component2 c){var t=c.GetTotalTransform(false);return Unit(Sub(Point(app,p,t),Point(app,new double[3],t)));}
        static bool Same(ISldWorks app,object a,object b){return a!=null&&b!=null&&app.IsSame(a,b)==(int)swObjectEquality.swObjectSame;}
        static Body2 NativeBody(Component2 c){object info;var raw=c.GetBodies3((int)swBodyType_e.swSolidBody,out info) as Array;Need(raw!=null&&raw.Length==1,"one native body required");return raw.GetValue(0) as Body2;}
        static int Planes(ModelDoc2 part){int n=0,seen=0;for(Feature f=part.FirstFeature() as Feature;f!=null;f=f.GetNextFeature() as Feature){Need(++seen<2000,"plane traversal bound");if(f.GetTypeName2()=="RefPlane")n++;}return n;}
        static HashSet<int> Ids(ModelDoc2 part){var ids=new HashSet<int>();int count=0;for(Feature f=part.FirstFeature() as Feature;f!=null;f=f.GetNextFeature() as Feature){Need(++count<2000,"feature traversal bound");Need(ids.Add(f.GetID()),"duplicate native feature ID in one Part tree");}return ids;}
        static Feature FindFeature(ModelDoc2 part,int id){int n=0;for(Feature f=part.FirstFeature() as Feature;f!=null;f=f.GetNextFeature() as Feature){Need(++n<2000,"owned feature traversal bound");if(f.GetID()==id)return f;}return null;}
        static double[] Common(AfjParametricPlan plan,double s,double q){return Add(plan.JointOrigin,Add(Mul(plan.OutLoser,s),Mul(plan.OutWinner,q)));}
        // PRIVATE SKETCH-ONLY per-Run cache. Never carry it across native Cuts,
        // rebuilds, component movement, configuration changes or Run returns.
        private sealed class CachedDatumFrame
        {
            internal Component2 Component;
            internal double[] WorldMatrix,InverseMatrix;
            internal readonly List<CachedDatumFace> Faces=new List<CachedDatumFace>();
        }
        private sealed class CachedDatumFace
        {
            internal Face2 Native;
            internal double[] NormalWorld,PointWorld;
            internal double Area=-1;
            internal int Owner=-1;
            internal object Context;
            internal List<CachedDatumEdge> Edges;
        }
        private sealed class CachedDatumEdge
        {
            internal Edge Native;
            internal double[] A,B,Direction;
            internal double Length;
            internal object Context;
        }
        private readonly List<CachedDatumFrame> NativeDatumFrames=new List<CachedDatumFrame>();
        private void ClearNativeDatumCache(){NativeDatumFrames.Clear();}
        private static double[] CachedWorld(CachedDatumFrame frame,double[] point){return AfjProfileAnalyzer.Transform(point,frame.WorldMatrix);}
        private static double[] CachedNative(CachedDatumFrame frame,double[] point){return AfjProfileAnalyzer.Transform(point,frame.InverseMatrix);}
        private CachedDatumFrame NativeDatumFrame(Component2 component)
        {
            var existing=NativeDatumFrames.FirstOrDefault(f=>Object.ReferenceEquals(f.Component,component));if(existing!=null)return existing;
            var timer=System.Diagnostics.Stopwatch.StartNew();var transform=component.GetTotalTransform(false);Need(transform!=null,"datum component transform unavailable");
            var inverse=transform.Inverse() as MathTransform;Need(inverse!=null,"datum inverse unavailable");
            var world=transform.ArrayData as double[];var native=inverse.ArrayData as double[];
            Need(world!=null&&native!=null&&world.Length==16&&native.Length==16&&world.Concat(native).All(AfjTemporaryBoolean.Finite),"datum transform nonfinite or malformed");
            var frame=new CachedDatumFrame{Component=component,WorldMatrix=(double[])world.Clone(),InverseMatrix=(double[])native.Clone()};
            var raw=NativeBody(component).GetFaces() as Array;Need(raw!=null&&raw.Length<50000,"bounded native faces required");
            double[] zero=CachedWorld(frame,new double[3]);
            foreach(Face2 face in raw.Cast<object>().OfType<Face2>())
            {
                var surface=face.GetSurface() as Surface;if(surface==null||!surface.IsPlane())continue;
                var p=surface.PlaneParams as double[];if(p==null||p.Length!=6||p.Any(v=>!AfjTemporaryBoolean.Finite(v)))continue;
                frame.Faces.Add(new CachedDatumFace{Native=face,NormalWorld=Unit(Sub(CachedWorld(frame,p.Take(3).ToArray()),zero)),PointWorld=CachedWorld(frame,p.Skip(3).Take(3).ToArray())});
            }
            NativeDatumFrames.Add(frame);
            System.Diagnostics.Trace.WriteLine("[U-DATUM-CACHE] phase=one_native_face_snapshot faces="+raw.Length+" planar="+frame.Faces.Count+" ms="+timer.ElapsedMilliseconds);
            return frame;
        }
        private static int CachedOwner(CachedDatumFace face)
        {
            if(face.Owner>=0)return face.Owner;var owner=face.Native.GetFeature() as Feature;Need(owner!=null,"native datum owner unavailable");face.Owner=owner.GetID();return face.Owner;
        }
        private static List<CachedDatumEdge> CachedEdges(CachedDatumFrame frame,CachedDatumFace face)
        {
            if(face.Edges!=null)return face.Edges;var result=new List<CachedDatumEdge>();var raw=face.Native.GetEdges() as Array;Need(raw!=null&&raw.Length<10000,"bounded native support edges required");
            foreach(Edge edge in raw.Cast<object>().OfType<Edge>())
            {
                var curve=edge.GetCurve() as Curve;if(curve==null||!curve.IsLine())continue;
                var a=edge.GetStartVertex() as Vertex;var b=edge.GetEndVertex() as Vertex;if(a==null||b==null)continue;
                var pa=a.GetPoint() as double[];var pb=b.GetPoint() as double[];if(pa==null||pb==null||pa.Length!=3||pb.Length!=3||pa.Concat(pb).Any(v=>!AfjTemporaryBoolean.Finite(v)))continue;
                var wa=CachedWorld(frame,pa);var wb=CachedWorld(frame,pb);double length=Norm(Sub(wb,wa));if(length<1e-7)continue;
                result.Add(new CachedDatumEdge{Native=edge,A=wa,B=wb,Length=length,Direction=Unit(Sub(wb,wa))});
            }
            face.Edges=result;return result;
        }
        Datum FaceDatum(ISldWorks app,Component2 c,double[] expectedNormal,double[] seed,string role)
        {
            var frame=NativeDatumFrame(c);double support=Dot(seed,expectedNormal);
            var matches=frame.Faces.Where(f=>Math.Abs(Math.Abs(Dot(f.NormalWorld,expectedNormal))-1)<1e-7&&Math.Abs(Dot(f.PointWorld,expectedNormal)-support)<1e-8).ToArray();
            Need(matches.Length>0,"cached native Face plane datum unavailable: "+role);
            // Area/owner/context/trim calls happen only for actual matching
            // native planes, not for every face for every requested datum.
            foreach(var face in matches)if(face.Area<0)face.Area=face.Native.GetArea();
            foreach(var face in matches.OrderByDescending(f=>f.Area))
            {
                var local=CachedNative(frame,seed);var closest=face.Native.GetClosestPointOn(local[0],local[1],local[2]) as double[];if(closest==null||closest.Length<3)continue;
                if(face.Context==null)face.Context=c.GetCorrespondingEntity(face.Native);if(!(face.Context is Face2))continue;
                RequireOriginalDatum(c,CachedOwner(face));double distance=Norm(Sub(CachedWorld(frame,closest.Take(3).ToArray()),seed));
                return new Datum{Component=c,Native=face.Native,Context=face.Context,Kind="ACTUAL_NATIVE_FACE_UNDERLYING_PLANE",Role=role,OwnerFeatureId=CachedOwner(face),PlaneWorld=face.NormalWorld.Concat(face.PointWorld).ToArray(),SeedWorld=seed,SeedOnFiniteTrim=distance<1e-8,ClosestDistance=distance};
            }
            throw new InvalidOperationException("U_GATE2_UNVERIFIED: matching native Face trim/context unavailable: "+role);
        }
        Datum EdgeDatum(ISldWorks app,Component2 c,Face2 support,double[] worldDirection,double[] seed,string role)
        {
            var frame=NativeDatumFrame(c);var face=frame.Faces.FirstOrDefault(f=>Object.ReferenceEquals(f.Native,support));
            Need(face!=null,"support Face is not from current per-Run native snapshot");
            foreach(var edge in CachedEdges(frame,face))
            {
                if(Math.Abs(Math.Abs(Dot(edge.Direction,worldDirection))-1)>1e-7)continue;double along=Dot(Sub(seed,edge.A),edge.Direction);
                var projection=Add(edge.A,Mul(edge.Direction,along));if(Norm(Sub(projection,seed))>1e-8||along< -1e-8||along>edge.Length+1e-8)continue;
                RequireOriginalDatum(c,CachedOwner(face));if(edge.Context==null)edge.Context=c.GetCorrespondingEntity(edge.Native);if(!(edge.Context is Edge))continue;
                return new Datum{Component=c,Native=edge.Native,Context=edge.Context,Kind="ACTUAL_NATIVE_FINITE_EDGE",Role=role,OwnerFeatureId=CachedOwner(face),SeedWorld=seed,SeedOnFiniteTrim=true,ClosestDistance=Norm(Sub(projection,seed))};
            }
            throw new InvalidOperationException("U_GATE2_UNVERIFIED: actual finite cached Web tangent edge unavailable: "+role);
        }

        static Datum AtSeed(ISldWorks app,Datum source,double[] seed)
        {
            double[] local=Point(app,seed,source.Component.GetTotalTransform(false).Inverse() as MathTransform),closest=null;
            var face=source.Native as Face2;var edge=source.Native as Edge;
            if(face!=null)closest=face.GetClosestPointOn(local[0],local[1],local[2]) as double[];
            else if(edge!=null)closest=edge.GetClosestPointOn(local[0],local[1],local[2]) as double[];
            Need(closest!=null&&closest.Length>=3,"datum trimmed witness unavailable at individual critical point");
            double distance=Norm(Sub(World(app,closest.Take(3).ToArray(),source.Component),seed));
            if(edge!=null)Need(distance<1e-8,"critical point outside actual finite tangent edge "+source.Role);
            return new Datum{Component=source.Component,Native=source.Native,Context=source.Context,Kind=source.Kind,Role=source.Role,
                OwnerFeatureId=source.OwnerFeatureId,PlaneWorld=source.PlaneWorld,SeedWorld=seed,SeedOnFiniteTrim=distance<1e-8,ClosestDistance=distance};
        }
        static int[] Allowed(Sketch sketch,params object[] entities){var values=sketch.RelationManager.GetAllowedRelations(entities.Select(e=>new DispatchWrapper(e)).ToArray()) as Array;return values==null?new int[0]:values.Cast<object>().Select(Convert.ToInt32).ToArray();}
        static int Relate(Sketch sketch,SketchPoint point,object target){var relation=sketch.RelationManager.AddRelation(new[]{new DispatchWrapper(point),new DispatchWrapper(target)},(int)swConstraintType_e.swConstraintType_COINCIDENT) as SketchRelation;Need(relation!=null,"Coincident native relation returned null");return relation.GetRelationType();}
        static string PointId(SketchPoint point){var raw=point.GetID() as Array;return raw==null?Convert.ToString(point.GetID()):String.Join(",",raw.Cast<object>().Select(Convert.ToString));}
        static string SegmentObjectId(SketchSegment segment){var raw=segment.GetID() as Array;return raw==null?Convert.ToString(segment.GetID()):String.Join(",",raw.Cast<object>().Select(Convert.ToString));}
        static SketchPoint SegmentStart(SketchSegment segment){var line=segment as SketchLine;Need(line!=null,"straight native line required");return line.GetStartPoint2() as SketchPoint;}
        static SketchPoint SegmentEnd(SketchSegment segment){var line=segment as SketchLine;Need(line!=null,"straight native line required");return line.GetEndPoint2() as SketchPoint;}
        static int RelationChecked(Sketch sketch,int type,params object[] entities)
        {
            Need(Allowed(sketch,entities).Contains(type),"native API does not advertise relation "+type);
            var relation=sketch.RelationManager.AddRelation(entities.Select(e=>new DispatchWrapper(e)).ToArray(),type) as SketchRelation;
            Need(relation!=null&&relation.GetRelationType()==type,"native relation failed "+type);return type;
        }
        static void MasterRelationChecked(ISldWorks app,Sketch sketch,int type,object local,object masterContext,object masterNative,SketchReport result)
        {
            Need(Allowed(sketch,local,masterContext).Contains(type),"master relation not advertised "+type);
            var relation=sketch.RelationManager.AddRelation(new[]{new DispatchWrapper(local),new DispatchWrapper(masterContext)},type) as SketchRelation;
            Need(relation!=null&&relation.GetRelationType()==type,"master native relation creation failed "+type);
            Array entities=relation.GetDefinitionEntities2() as Array;Need(entities!=null,"master relation definition entities unavailable");
            bool expected=false;var types=new List<string>();
            foreach(object entity in entities)
            {
                types.Add(entity==null?"NULL":entity.GetType().FullName);
                if(entity!=null&&(Same(app,entity,masterContext)||Same(app,entity,masterNative)))expected=true;
            }
            result.MasterDefinitionEntityTypes.Add(String.Join(";",types));
            Need(expected,"master actual definition entity identity unverified; no copied-line fallback");result.MasterDefinitionIdentitiesVerified++;
        }
        static SketchSegment FindMasterSeam(ISldWorks app,Component2 component,Feature feature,double[] a,double[] b)
        {
            var sketch=feature.GetSpecificFeature2() as Sketch;Need(sketch!=null&&sketch.GetConstrainedStatus()==(int)swConstrainedStatus_e.swFullyConstrained,"master native Sketch must already be fully defined");
            Need(sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swDangling)==0&&sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swOverDefining)==0,"master native relation graph invalid");
            var inverse=sketch.ModelToSketchTransform.Inverse() as MathTransform;Need(inverse!=null,"closed master transform unavailable");
            var hits=new List<SketchSegment>();var raw=sketch.GetSketchSegments() as Array;Need(raw!=null,"master segments unavailable");
            foreach(SketchSegment segment in raw.Cast<object>().OfType<SketchSegment>())
            {
                if(segment.ConstructionGeometry||!(segment is SketchLine))continue;var p=SegmentStart(segment);var q=SegmentEnd(segment);
                var pa=World(app,Point(app,new[]{p.X,p.Y,p.Z},inverse),component);var pb=World(app,Point(app,new[]{q.X,q.Y,q.Z},inverse),component);
                if(Norm(Sub(pa,a))<1e-8&&Norm(Sub(pb,b))<1e-8||Norm(Sub(pa,b))<1e-8&&Norm(Sub(pb,a))<1e-8)hits.Add(segment);
            }
            Need(hits.Count==1,"exact native master seam not uniquely identified");return hits[0];
        }
        static SketchSegment MasterLine(ISldWorks app,Component2 component,Feature feature,double[] expectedA,double[] expectedB,bool construction)
        {
            var master=feature.GetSpecificFeature2() as Sketch;Need(master!=null&&master.GetConstrainedStatus()==(int)swConstrainedStatus_e.swFullyConstrained,"fully constrained actual master required");
            Need(master.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swDangling)==0&&master.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swOverDefining)==0,"master relation graph invalid");
            var inverse=master.ModelToSketchTransform.Inverse() as MathTransform;Need(inverse!=null,"master inverse unavailable");var raw=master.GetSketchSegments() as Array;Need(raw!=null,"master segments unavailable");var hits=new List<SketchSegment>();
            foreach(SketchSegment segment in raw.Cast<object>().OfType<SketchSegment>())
            {
                if(!(segment is SketchLine)||segment.ConstructionGeometry!=construction)continue;var a=SegmentStart(segment);var b=SegmentEnd(segment);
                var wa=World(app,Point(app,new[]{a.X,a.Y,a.Z},inverse),component);var wb=World(app,Point(app,new[]{b.X,b.Y,b.Z},inverse),component);
                if(Norm(Sub(wa,expectedA))<1e-8&&Norm(Sub(wb,expectedB))<1e-8||Norm(Sub(wa,expectedB))<1e-8&&Norm(Sub(wb,expectedA))<1e-8)hits.Add(segment);
            }
            Need(hits.Count==1,"actual master segment not uniquely matched by construction/geometry");return hits[0];
        }
        static void CheckFullPolygon(SketchSegment[] profile,double[][] expected)
        {
            for(int i=0;i<profile.Length;i++){var a=SegmentStart(profile[i]);var b=SegmentEnd(profile[i]);Need(Norm(Sub(new[]{a.X,a.Y,a.Z},expected[i]))<1e-8&&Norm(Sub(new[]{b.X,b.Y,b.Z},expected[(i+1)%expected.Length]))<1e-8,"native constraint changed exact loser polygon vertex "+i);}
        }
        internal sealed class CapEvidence
        {
            internal string Role,Status="UNVERIFIED",SketchName,ReacquisitionSource;
            internal int SketchFeatureId=-1,SupportReferenceType=-1,ConstraintStatus=-1,Relations=-1,Dangling=-1,Overdefining=-1,Dimensions=-1,NewPlanes=-1,ExternalReferences=-1;
            internal int ReacquiredSegmentCount,AxisRelations,SourceDefinitionIdentitiesVerified;
            internal bool OwnSupportVerified,SketchOwnerVerified,SeedGeometryUnchanged,ClosedFullyConstrained;
            internal readonly List<string> SourcePointIds=new List<string>(),SourceSegmentIds=new List<string>(),ReacquiredSegmentIds=new List<string>();
            internal double[][] ExpectedWorld;
        }
        static SketchPoint SourceEndpoint(ISldWorks app,Component2 component,Feature feature,double[] expected,double[] other)
        {
            // The actual reviewed contour segment supplies its structural
            // endpoint. Other Coincident construction points at this same
            // coordinate may legitimately retain a different native point ID.
            var segment=MasterLine(app,component,feature,expected,other,false);var sketch=feature.GetSpecificFeature2() as Sketch;
            var inverse=sketch.ModelToSketchTransform.Inverse() as MathTransform;Need(inverse!=null,"source endpoint transform unavailable");
            var a=SegmentStart(segment);var b=SegmentEnd(segment);var wa=World(app,Point(app,new[]{a.X,a.Y,a.Z},inverse),component);var wb=World(app,Point(app,new[]{b.X,b.Y,b.Z},inverse),component);
            bool matchA=Norm(Sub(wa,expected))<1e-8,matchB=Norm(Sub(wb,expected))<1e-8;Need(matchA!=matchB,"source segment endpoint not uniquely identified");return matchA?a:b;
        }
        static void OneDrivingDimension(Feature feature,string requiredName,double value)
        {
            int count=0;for(DisplayDimension display=feature.GetFirstDisplayDimension() as DisplayDimension;display!=null;display=feature.GetNextDisplayDimension(display) as DisplayDimension)
            {Need(++count<64,"source dimension traversal bound");var dimension=display.GetDimension2(0) as Dimension;Need(dimension!=null&&dimension.DrivenState==(int)swDimensionDrivenState_e.swDimensionDriving&&dimension.FullName.IndexOf(requiredName,StringComparison.Ordinal)>=0&&Math.Abs(Math.Abs(dimension.SystemValue)-value)<1e-8,"actual source driving manufacturing dimension not qualified");}
            Need(count==1,"one actual source manufacturing dimension required");
        }

        static SketchPoint LineStart(SketchSegment segment){return SegmentStart(segment);}
        static SketchPoint LineEnd(SketchSegment segment){return SegmentEnd(segment);}
        static int AddRelation(Sketch sketch,int kind,params object[] entities){return RelationChecked(sketch,kind,entities);}
        static int Coincident(Sketch sketch,SketchPoint point,Datum datum){return RelationChecked(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,point,datum.Context);}
        static void CheckPolygon(SketchSegment[] segments,double[][] local){CheckFullPolygon(segments,local);}
        // Internal Sketch writer. No Cut, Save, Rebuild or
        // deployment is performed here. Caller owns the native transaction.
        internal sealed class OwnedSketch
        {
            internal ModelDoc2 Part;
            internal Feature AssemblyFeature,Feature;
            internal string EvidenceId,IntendedName,ObservedName;
            internal int FeatureId=-1;
            internal bool CallbackAttempted,CallbackReturned,ClosedOwnerVerified,DirectFeatureIdentityMatched;
        }
        internal sealed class WriteResult
        {
            internal string Status="UNVERIFIED",Error,RunPrefix;
            internal bool Success,NativeApi,CutCreated=false,SaveApiCalled=false,InstalledAddin=false,ContextRestored;
            internal bool RegistrationCallbackThrew,UnresolvedCreation,RequiresCallerRollback;
            internal bool RequiresIndependentNearClearanceDecision=true;
            internal readonly List<OwnedSketch> Owned=new List<OwnedSketch>();
            internal readonly List<SketchReport> Reports=new List<SketchReport>();
            internal readonly List<string> Stages=new List<string>();
            internal int NewPlanes=-1,Dimensions=-1,Relations=-1;
            // Managed references only: no Marshal release and no forced GC.
            // A returned report must not carry native topology across the first
            // Cut, Rebuild, configuration change or document close. The caller
            // retains exact native ownership in its separate token journal.
            internal bool TransientNativeReferencesDropped;
            internal void DropTransientNativeReferences()
            {
                Action<Datum> drop = datum => {
                    if(datum==null)return;
                    datum.Native=null;datum.Context=null;datum.Component=null;
                };
                foreach(var owned in Owned)
                {owned.AssemblyFeature=null;owned.Feature=null;}
                foreach(var report in Reports)
                {
                    foreach(var point in report.Points)
                    {drop(point.First);drop(point.Second);}
                    foreach(var datum in report.GapDatums)drop(datum);
                    foreach(var datum in report.ClosureDatums)drop(datum);
                }
                TransientNativeReferencesDropped=true;
            }
        }
        private sealed class PendingCreation
        {
            internal ModelDoc2 Part;
            internal Component2 Component;
            internal string EvidenceId;
            internal Sketch Sketch;
            internal bool InsertAttempted;
        }
        private sealed class ReacquiredBatch
        {
            internal Sketch Sketch;
            internal SketchSegment[] Lines;
            internal string[] Ids;
        }
        [ThreadStatic] private static bool EntryActive;
        private ISldWorks _app;
        private ModelDoc2 _model,_longPart,_shortPart;
        private AssemblyDoc _assembly;
        private Component2 _longFrame,_shortFrame;
        private HashSet<int> _longOriginal,_shortOriginal;
        private string _prefix;
        private Action<ModelDoc2,Feature,string> _register;
        private Action<ModelDoc2,Feature,string> _confirmClosed;
        private Action<string> _stageSink;
        private WriteResult _output;
        private PendingCreation _pending;
        private void Stage(string message)
        {
            _output.Stages.Add(message);Trace.WriteLine("[AFJ-U-PARAMETRIC-WRITER] "+message);if(_stageSink!=null)_stageSink(message);
        }
        private HashSet<int> OriginalIds(ModelDoc2 part)
        {
            if(Same(_app,part,_longPart))return _longOriginal;if(Same(_app,part,_shortPart))return _shortOriginal;throw new InvalidOperationException("UNVERIFIED_RULE: datum Part is outside the pair");
        }
        private void RequireOriginalDatum(Component2 component,int owner)
        {
            var part=component.GetModelDoc2() as ModelDoc2;Need(part!=null&&OriginalIds(part).Contains(owner),"source Face/Edge owner is not in caller's original feature IDs; no transaction-new datum allowed");
        }
        private void ValidateTransactionHistory()
        {
            Need(Same(_app,_app.ActiveDoc,_model)&&Same(_app,_assembly.GetEditTarget(),_model),"history validation requires bound assembly outside Part edit");
            foreach(var part in new[]{_longPart,_shortPart})
            {
                var expected=new HashSet<int>(OriginalIds(part));foreach(var owned in _output.Owned.Where(o=>Same(_app,o.Part,part)))
                {Need(owned.FeatureId>=0&&owned.CallbackReturned&&owned.ClosedOwnerVerified,"only registered, closed owned Sketches may extend source history");Need(expected.Add(owned.FeatureId),"owned ID overlaps original/prior feature ID");}
                Need(Ids(part).SetEquals(expected),"Part history differs from immutable originals plus exact closed transaction-owned Sketches");
            }
        }
        private string IntendedName(string role)
        {
            switch(role)
            {
                case "U_RULE:COVER_WEB_WALL":return _prefix+"_Cover";
                case "U_RULE:LOSER_STEPPED_WEB_WALL_RETURN":return _prefix+"_Loser";
                case "U_RULE:NORMAL_BEND_CAP":return _prefix+"_ShortRelief";
                case "U_RULE:FAR_BEND_CLEARANCE":return _prefix+"_LongFarRelief";
                default:throw new InvalidOperationException("UNVERIFIED_RULE: unknown Sketch intent role");
            }
        }
        private OwnedSketch RegisterCreated(ModelDoc2 part,Feature feature,string role)
        {
            Need(feature!=null,"created Sketch Feature QI unavailable");Need(!_output.Owned.Any(o=>o.EvidenceId==role),"semantic Sketch role already created");
            // Retain the native handle before calling user transaction code.
            // Do not depend on Part.FirstFeature visibility while editing.
            var owned=new OwnedSketch{Part=part,AssemblyFeature=feature,EvidenceId=role,IntendedName=IntendedName(role)};_output.Owned.Add(owned);owned.CallbackAttempted=true;
            try{_register(part,feature,role);owned.CallbackReturned=true;}
            catch{_output.RegistrationCallbackThrew=true;throw;}
            finally
            {
                // Preserve a callback exception; acquiring the ID here is only
                // an ownership evidence best effort if registration threw.
                try{owned.FeatureId=feature.GetID();}catch{_output.UnresolvedCreation=true;}
            }
            Need(owned.FeatureId>=0&&!OriginalIds(part).Contains(owned.FeatureId),"created Sketch ID overlaps protected original source history");
            Need(!_output.Owned.Any(o=>!Object.ReferenceEquals(o,owned)&&Same(_app,o.Part,part)&&o.FeatureId==owned.FeatureId),"duplicate created feature ID in target Part");return owned;
        }
        private Sketch OpenOwnedSketch(ModelDoc2 part,Component2 component,string role)
        {
            Need(_pending==null,"previous creation context is still pending");Need(Same(_app,_assembly.GetEditTarget(),part)&&Same(_app,_assembly.GetEditTargetComponent(),component),"exact target Part edit context required before Sketch creation");
            Need(_model.SketchManager.ActiveSketch==null&&part.SketchManager.ActiveSketch==null,"another active Sketch exists before creation");
            _pending=new PendingCreation{Part=part,Component=component,EvidenceId=role,InsertAttempted=true};
            try
            {
                _model.SketchManager.InsertSketch(true);var sketch=_model.SketchManager.ActiveSketch??part.SketchManager.ActiveSketch;_pending.Sketch=sketch;Need(sketch!=null,"InsertSketch returned no active native Sketch");var feature=sketch as Feature;
                if(feature==null){_output.UnresolvedCreation=true;Need(false,"active created Sketch has no Feature interface");}
                RegisterCreated(part,feature,role);
                Need(Same(_app,_app.ActiveDoc,_model)&&Same(_app,_assembly.GetEditTarget(),part)&&Same(_app,_assembly.GetEditTargetComponent(),component),"registration callback changed active document/edit target");
                Need(Same(_app,_model.SketchManager.ActiveSketch,sketch),"registration callback changed active Sketch");return sketch;
            }
            catch
            {
                // InsertSketch itself may throw after native creation. Capture
                // the actual active Sketch once if this role has no ledger.
                if(!_output.Owned.Any(o=>o.EvidenceId==role))
                {
                    try
                    {
                        var active=_model.SketchManager.ActiveSketch??part.SketchManager.ActiveSketch;_pending.Sketch=active;var feature=active as Feature;
                        if(feature!=null&&Same(_app,_assembly.GetEditTarget(),part))RegisterCreated(part,feature,role);else _output.UnresolvedCreation=true;
                    }
                    catch{_output.UnresolvedCreation=true;}
                }
                throw;
            }
        }
        private void CleanupPendingSketch()
        {
            if(_pending==null)return;var active=_model.SketchManager.ActiveSketch??_pending.Part.SketchManager.ActiveSketch;
            if(active!=null)
            {
                Need(_pending.Sketch!=null&&Same(_app,active,_pending.Sketch)&&Same(_app,_assembly.GetEditTarget(),_pending.Part)&&Same(_app,_assembly.GetEditTargetComponent(),_pending.Component),"active Sketch is not the recorded owned creation; do not toggle unknown native context");_model.SketchManager.InsertSketch(true);
            }
            Need(_model.SketchManager.ActiveSketch==null&&_pending.Part.SketchManager.ActiveSketch==null,"owned Sketch did not close");_pending=null;
        }
        private void NameOwned(ModelDoc2 part,Feature native,string role)
        {
            var owned=_output.Owned.Single(o=>o.EvidenceId==role);Need(Same(_app,owned.Part,part)&&owned.FeatureId==native.GetID()&&!OriginalIds(part).Contains(native.GetID()),"closed native owner does not match registered target/ID");
            owned.DirectFeatureIdentityMatched=Same(_app,native,owned.AssemblyFeature);native.Name=owned.IntendedName;owned.ObservedName=native.Name;Need(String.Equals(owned.ObservedName,owned.IntendedName,StringComparison.Ordinal),"native Sketch name changed/deduplicated");
            owned.Feature=native;owned.ClosedOwnerVerified=true;CleanupPendingSketch();
            Need(Same(_app,_app.ActiveDoc,_model)&&Same(_app,_assembly.GetEditTarget(),_model)&&_model.GetActiveSketch2()==null,
                "closed native Sketch confirmation requires restored Assembly context");
            // This is the fresh Part-context Feature reacquired after closing and
            // leaving EditPart. An early Assembly-context token is provisional.
            if(_confirmClosed!=null)_confirmClosed(part,native,role);
            ValidateTransactionHistory();
        }
        private ReacquiredBatch Reacquire(Feature originalAssemblyFeature,SketchSegment[] aliases,double[][] starts,double[][] ends,bool[] construction,SketchReport result)
        {
            Need(aliases.Length==starts.Length&&aliases.Length==ends.Length&&aliases.Length==construction.Length,"reacquisition specification length mismatch");var ids=aliases.Select(SegmentObjectId).ToArray();Need(ids.Distinct().Count()==ids.Length,"created native segment IDs not distinct");
            var proper=originalAssemblyFeature.GetSpecificFeature2() as Sketch;Need(proper!=null,"original Assembly-context Sketch Feature specific missing");var raw=proper.GetSketchSegments() as Array;Need(raw!=null&&raw.Length==aliases.Length,"unexpected actual native segment inventory");var all=raw.Cast<object>().OfType<SketchSegment>().ToArray();var lines=new SketchSegment[aliases.Length];
            for(int i=0;i<aliases.Length;i++)
            {
                var id=ids[i];var hits=all.Where(candidate=>SegmentObjectId(candidate)==id).ToArray();Need(hits.Length==1,"original segment native ID not unique "+id);var segment=hits[0];Need(segment is SketchLine&&segment.ConstructionGeometry==construction[i],"reacquired line type/construction differs");
                var a=SegmentStart(segment);var b=SegmentEnd(segment);Need(Norm(Sub(new[]{a.X,a.Y,a.Z},starts[i]))<1e-8&&Norm(Sub(new[]{b.X,b.Y,b.Z},ends[i]))<1e-8,"reacquired original ID changed actual line geometry");lines[i]=segment;result.ReacquiredSegmentIds.Add(id);
            }
            result.ReacquiredSegmentCount+=lines.Length;result.ReacquisitionSource="ORIGINAL_ASSEMBLY_CONTEXT_SKETCH_FEATURE_GETSPECIFICFEATURE2";return new ReacquiredBatch{Sketch=proper,Lines=lines,Ids=ids};
        }
        private static HashSet<int> CloneOriginal(IEnumerable<int> ids)
        {
            Need(ids!=null,"caller original feature-ID set required");var values=ids.ToArray();var result=new HashSet<int>(values);Need(values.Length>0&&result.Count==values.Length,"caller original feature IDs empty/duplicated");return result;
        }
        internal static void ValidateIntent(AfjParametricPlan intent)
        {
            Need(intent!=null&&intent.Plan!=null&&intent.Folded==null&&intent.Winner!=null&&intent.Loser!=null&&(intent.Plan.RuleId=="ORTHOGONAL_U_COVER"||intent.Plan.RuleId=="ORTHOGONAL_U_TALLER_COVER"),"UNVERIFIED_RULE: only qualified U intent");
            Need(intent.SeamNear!=null&&intent.SeamFar!=null&&intent.SeamNear.Length==2&&intent.SeamFar.Length==2&&intent.SeamNear.Concat(intent.SeamFar).All(AfjTemporaryBoolean.Finite),"UNVERIFIED_RULE: finite seam intent required");
            Need(!intent.Winner.NearReturn&&!intent.Winner.FarReturn&&!intent.Loser.NearReturn&&!intent.Loser.FarReturn,"UNVERIFIED_RULE: plain-U 8/9-vertex recipe only");
            bool naturalNear=intent.SeamNear[1]>intent.Loser.Thickness+1e-8;
            Need(naturalNear||Math.Abs(intent.SeamNear[1]-intent.Loser.Thickness)<1e-8,"UNVERIFIED_RULE: near seam must be clamped inner Wall or adjusted bisector");
            if(naturalNear)Need(Math.Abs(intent.SeamNear[0]-intent.Winner.NearTangent)<1e-8||Math.Abs(intent.SeamNear[1]-intent.Loser.NearTangent)<1e-8,"UNVERIFIED_RULE: natural near seam must use an actual near Web tangent datum");
            var directions=new[]{intent.OutLoser,intent.OutWinner,intent.Depth};Need(directions.All(v=>v!=null&&v.Length==3&&v.All(AfjTemporaryBoolean.Finite)&&Math.Abs(Norm(v)-1)<1e-7),"UNVERIFIED_RULE: measured unit frame directions required");
            Need(Math.Abs(Dot(intent.OutLoser,intent.OutWinner))<1e-7&&Math.Abs(Dot(intent.OutLoser,intent.Depth))<1e-7&&Math.Abs(Dot(intent.OutWinner,intent.Depth))<1e-7,"UNVERIFIED_RULE: orthogonal U recipe only");
            double sn=intent.SeamNear[0],qn=intent.SeamNear[1],sf=intent.SeamFar[0],qf=intent.SeamFar[1],g=AfjManufacturingConventions.AssemblyFitGap;Need(qf-qn>1e-8&&sf-sn>1e-8&&g>0&&AfjTemporaryBoolean.Finite(g),"UNVERIFIED_RULE: positive seam span/gap required");
            double ds=(sf-sn)/(qf-qn),shift=g*Math.Sqrt(1+ds*ds),near=intent.Loser.NearTangent,far=intent.Loser.FarTangent;Func<double,double> seam=q=>sn+(q-qn)*ds-shift;
            double qHit=qn+(intent.Winner.Thickness+shift-sn)/ds;bool knee=qHit>near+1e-8&&qHit<far-1e-8;
            Need(seam(near)>intent.Winner.Thickness+1e-8||knee,"UNVERIFIED_RULE: degenerate/tangent near clipping is not admitted");
            if(naturalNear)Need(Math.Abs(ds-1)<1e-8,"UNVERIFIED_RULE: natural unequal-width branch requires geometric equal-leg bisector");
            Need(Math.Abs(far-qf)<1e-8&&intent.Winner.Width-intent.Winner.Thickness-g>seam(qf)+1e-8,"UNVERIFIED_RULE: nondegenerate losing far branch required");
            string[] roles={"U_RULE:COVER_WEB_WALL","U_RULE:LOSER_STEPPED_WEB_WALL_RETURN","U_RULE:NORMAL_BEND_CAP","U_RULE:FAR_BEND_CLEARANCE"};int[] counts={naturalNear?9:8,knee?9:8,4,4};
            for(int i=0;i<roles.Length;i++){var operations=intent.Plan.A.Operations.Concat(intent.Plan.B.Operations).Where(o=>o.EvidenceId==roles[i]).ToArray();Need(operations.Length==1&&operations[0].Tools.Count==1,"UNVERIFIED_RULE: one independent oracle tool per Sketch role required");var tool=operations[0].Tools[0];Need(tool.Polygon!=null&&tool.Polygon.Length==counts[i]&&tool.Polygon.All(p=>p!=null&&p.Length==3&&p.All(AfjTemporaryBoolean.Finite))&&(tool.ArcCenters==null||tool.ArcCenters.All(c=>c==null)),"UNVERIFIED_RULE: exact reviewed polyline intent required");}
        }
        internal static WriteResult TryWrite(ISldWorks app,ModelDoc2 assemblyModel,Component2 longFrame,Component2 shortFrame,AfjParametricPlan intent,IEnumerable<int> longOriginalFeatureIds,IEnumerable<int> shortOriginalFeatureIds,string uniqueRunPrefix,Action<ModelDoc2,Feature,string> registerOwnedFeature,Action<string> stageSink=null,Action<ModelDoc2,Feature,string> confirmClosedFeature=null)
        {
            var output=new WriteResult();if(EntryActive){output.Error="UNVERIFIED_RULE: writer reentry on owner thread";return output;}EntryActive=true;AfjUParametricSketchWriter writer=null;
            try
            {
                Need(System.Threading.Thread.CurrentThread.GetApartmentState()==System.Threading.ApartmentState.STA,"native writer requires owner STA thread");Need(app!=null&&assemblyModel!=null&&registerOwnedFeature!=null,"bound app/assembly and nonnull transaction registration callback required");
                ValidateIntent(intent);
                Need(longFrame!=null&&shortFrame!=null,"resolved pair required");var longPart=longFrame.GetModelDoc2() as ModelDoc2;var shortPart=shortFrame.GetModelDoc2() as ModelDoc2;var assembly=assemblyModel as AssemblyDoc;Need(assembly!=null&&longPart!=null&&shortPart!=null&&!Same(app,longPart,shortPart),"two independent native target Parts required");output.NativeApi=true;
                // Reject an existing edit/sketch before constructing a writer
                // that is allowed to restore native context in its finally.
                Need(Same(app,app.ActiveDoc,assemblyModel)&&Same(app,assembly.GetEditTarget(),assemblyModel)&&assemblyModel.SketchManager.ActiveSketch==null&&longPart.SketchManager.ActiveSketch==null&&shortPart.SketchManager.ActiveSketch==null,"entry requires bound assembly outside any pre-existing Part/Sketch edit");
                Need(!String.IsNullOrWhiteSpace(uniqueRunPrefix)&&uniqueRunPrefix.Length<=48&&uniqueRunPrefix.StartsWith("AFJ_",StringComparison.Ordinal)&&uniqueRunPrefix.All(c=>Char.IsLetterOrDigit(c)||c=='_'),"unique AFJ_ run prefix with letters/digits/underscore required");
                writer=new AfjUParametricSketchWriter{_app=app,_model=assemblyModel,_assembly=assembly,_longPart=longPart,_shortPart=shortPart,_longFrame=longFrame,_shortFrame=shortFrame,_longOriginal=CloneOriginal(longOriginalFeatureIds),_shortOriginal=CloneOriginal(shortOriginalFeatureIds),_prefix=uniqueRunPrefix,_register=registerOwnedFeature,_confirmClosed=confirmClosedFeature,_stageSink=stageSink,_output=output};output.RunPrefix=uniqueRunPrefix;
                writer.ValidateTransactionHistory();foreach(var target in new[]{longFrame,shortFrame}){var part=target.GetModelDoc2() as ModelDoc2;var cfg=part.ConfigurationManager.ActiveConfiguration;Need(cfg!=null&&cfg.Name==target.ReferencedConfiguration,"source referenced/active configuration differs");}
                var names=new[]{"U_RULE:COVER_WEB_WALL","U_RULE:LOSER_STEPPED_WEB_WALL_RETURN","U_RULE:NORMAL_BEND_CAP","U_RULE:FAR_BEND_CLEARANCE"}.Select(writer.IntendedName).ToArray();Need(names.Distinct(StringComparer.OrdinalIgnoreCase).Count()==4,"four distinct intended feature names required");
                foreach(var part in new[]{longPart,shortPart}){int count=0;for(Feature feature=part.FirstFeature() as Feature;feature!=null;feature=feature.GetNextFeature() as Feature){Need(++count<2000,"name traversal bound");Need(!names.Contains(feature.Name,StringComparer.OrdinalIgnoreCase),"run prefix collides with an existing protected feature name");}}
                writer.Stage("BEGIN_COVER_SKETCH");var cover=writer.WriteCover(app,assemblyModel,longFrame,shortFrame,intent);output.Reports.Add(cover);Need(cover.Status.StartsWith("PASS_NATIVE_",StringComparison.Ordinal),"cover Sketch: "+cover.Error);var master=writer._output.Owned.Single(o=>o.EvidenceId=="U_RULE:COVER_WEB_WALL").Feature;
                writer.Stage("BEGIN_LOSER_SKETCH");var loser=writer.WriteLoser(app,assemblyModel,longFrame,shortFrame,intent,master,writer.Stage);output.Reports.Add(loser);Need(loser.Status.StartsWith("PASS_NATIVE_",StringComparison.Ordinal),"losing Sketch: "+loser.Error);var losing=writer._output.Owned.Single(o=>o.EvidenceId=="U_RULE:LOSER_STEPPED_WEB_WALL_RETURN").Feature;
                writer.Stage("BEGIN_TWO_CAP_SKETCHES");var caps=writer.WriteCaps(app,assemblyModel,longFrame,shortFrame,intent,master,losing,writer.Stage);output.Reports.Add(caps);Need(caps.Status.StartsWith("PASS_NATIVE_",StringComparison.Ordinal),"cap Sketches: "+caps.Error);
                writer.ValidateTransactionHistory();Need(output.Owned.Count==4&&output.Owned.All(o=>o.CallbackReturned&&o.ClosedOwnerVerified),"four registered/closed owned Sketches required");
                foreach(var owned in output.Owned){var actual=FindFeature(owned.Part,owned.FeatureId);var sketch=actual==null?null:actual.GetSpecificFeature2() as Sketch;Need(sketch!=null&&sketch.GetConstrainedStatus()==(int)swConstrainedStatus_e.swFullyConstrained&&sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swDangling)==0&&sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swOverDefining)==0,"final four-Sketch native graph invalid");}
                output.NewPlanes=output.Reports.Sum(r=>r.NewPlanes);output.Dimensions=output.Reports.Sum(r=>r.Dimensions);output.Relations=output.Reports.Sum(r=>r.Relations);Need(output.NewPlanes==0&&output.Dimensions==2,"exactly two manufacturing dimensions and zero new planes required");
                output.ContextRestored=Same(app,assembly.GetEditTarget(),assemblyModel);Need(output.ContextRestored&&!output.UnresolvedCreation,"restored context and resolved creation evidence required");output.Success=true;output.Status="NATIVE_FOUR_U_SKETCHES_VERIFIED_NO_CUT_NO_FOLLOW_OR_ROLLBACK_CLAIM";
            }
            catch(Exception ex){output.Status="UNVERIFIED_RULE_OR_SKETCH_WRITE_FAILURE";output.Error="HRESULT=0x"+ex.HResult.ToString("X8")+" "+ex;output.RequiresCallerRollback=output.Owned.Count>0||output.UnresolvedCreation;}
            finally
            {
                if(writer!=null)
                {
                    try{writer.CleanupPendingSketch();if(!Same(app,writer._assembly.GetEditTarget(),assemblyModel))writer._assembly.EditAssembly();output.ContextRestored=Same(app,writer._assembly.GetEditTarget(),assemblyModel);if(!output.ContextRestored){output.Success=false;output.Status="UNVERIFIED_NATIVE_CONTEXT_CLEANUP";output.RequiresCallerRollback=output.Owned.Count>0||output.UnresolvedCreation;}}
                    catch(Exception ex){output.Success=false;output.Status="UNVERIFIED_NATIVE_CONTEXT_CLEANUP";output.Error+=" CLEANUP "+ex;output.RequiresCallerRollback=true;}
                    writer.ClearNativeDatumCache();
                }
                // Cleanup and all four-Sketch checks have finished; preserve only
                // primitive evidence and exact Part/ID mapping for fresh queries.
                output.DropTransientNativeReferences();
                EntryActive=false;
            }
            return output;
        }
        private SketchReport WriteCover(ISldWorks app,ModelDoc2 model,Component2 longFrame,Component2 shortFrame,AfjParametricPlan plan)
        {
            var result=new SketchReport{Scope="FULL_U_COVER_SKETCH_ONE_MARGIN_DIM_NO_CUT"};var assembly=model as AssemblyDoc;ModelDoc2 part=null;Sketch sketch=null;bool editing=false,opened=false;
            bool inputPreferenceRead=false,oldInputPreference=false;
            try
            {
                Need(app!=null&&assembly!=null,"bound application/assembly required");result.NativeApi=true;
                Need(Same(app,app.ActiveDoc,model)&&Same(app,assembly.GetEditTarget(),model),"active bound assembly outside edit context required");
                Need(plan!=null&&plan.Folded==null&&(plan.Plan.RuleId=="ORTHOGONAL_U_COVER"||plan.Plan.RuleId=="ORTHOGONAL_U_TALLER_COVER"),"current U miter plan required");
                Need(plan.Winner!=null&&plan.Loser!=null,"measured sections required");
                bool naturalNear=plan.SeamNear[1]>plan.Loser.Thickness+1e-8;
                bool pinnedNearQ=naturalNear&&Math.Abs(plan.SeamNear[1]-plan.Loser.NearTangent)<1e-8&&plan.SeamNear[0]>plan.Winner.NearTangent+1e-8;
                Need(naturalNear||Math.Abs(plan.SeamNear[1]-plan.Loser.Thickness)<1e-8,"bounded native near-seam recipe required");result.CriticalSeamNearMatchesPlan=true;
                int vertexCount=naturalNear?9:8,seamEdge=naturalNear?2:1,farPoint=naturalNear?3:2,innerPoint=farPoint+1,outerPoint=farPoint+2,rightPoint=farPoint+3,topPoint=farPoint+4;
                part=shortFrame.GetModelDoc2() as ModelDoc2;var longPart=longFrame.GetModelDoc2() as ModelDoc2;
                Need(part!=null&&longPart!=null&&!Same(app,part,longPart),"two independent resolved Parts required");
                ValidateTransactionHistory();
                var cfg=part.ConfigurationManager.ActiveConfiguration;Need(cfg!=null&&cfg.Name==shortFrame.ReferencedConfiguration,"target configuration mismatch");
                HashSet<int> beforeIds=Ids(part),longBeforeIds=Ids(longPart);int beforePlanes=Planes(part);
                var operations=plan.Plan.A.Operations.Concat(plan.Plan.B.Operations).Where(o=>o.EvidenceId=="U_RULE:COVER_WEB_WALL").ToArray();
                Need(operations.Length==1&&operations[0].Tools.Count==1,"exact current winner tool missing");var tool=operations[0].Tools[0];
                Need(tool.Polygon.Length==vertexCount&&(tool.ArcCenters==null||tool.ArcCenters.All(a=>a==null)),"reviewed winner contour must match measured 8/9-vertex mode");
                double m=Math.Max(.01,5*Math.Max(plan.Loser.Thickness+plan.Loser.Radius,plan.Winner.Thickness+plan.Winner.Radius));result.ToolClosureMargin=m;
                double sn=plan.SeamNear[0],sf=plan.SeamFar[0],qf=plan.SeamFar[1],innerFar=plan.Winner.Width-plan.Winner.Thickness;
                double shortEnd=AfjParametricRules.Extreme(NativeBody(shortFrame),shortFrame.GetTotalTransform(false),plan.OutWinner,plan.JointOrigin);
                double qMax=Math.Max(shortEnd,plan.Loser.MaxWidth)+m,sMin=plan.Winner.MinWidth-m,sMax=plan.Winner.MaxWidth+m;
                var sourceList=new List<double[]>{new[]{sMin,plan.Loser.Thickness},new[]{sn,plan.Loser.Thickness}};
                if(naturalNear)sourceList.Add(new[]{sn,plan.SeamNear[1]});
                sourceList.AddRange(new[]{new[]{sf,qf},new[]{innerFar,qf},new[]{innerFar,plan.Loser.Width},new[]{sMax,plan.Loser.Width},new[]{sMax,qMax},new[]{sMin,qMax}});
                double[][] sourceCoordinates=sourceList.ToArray();
                var seeds=sourceCoordinates.Select(p=>Common(plan,p[0],p[1])).ToArray();result.ProfileVertexCount=seeds.Length;
                for(int i=0;i<seeds.Length;i++)
                {
                    double[] actual=Add(World(app,tool.Polygon[i],shortFrame),Mul(Unit(plan.Depth),AfjManufacturingConventions.CutterEnvelopeOverrun));
                    Need(Norm(Sub(actual,seeds[i]))<1e-8,"derived intent does not equal current reviewed projected winner polygon at "+i);
                }
                Trace.WriteLine("[AFJ-U-MASTER] DATUMS_BEGIN "+DateTime.UtcNow.ToString("o"));
                var ownWeb=FaceDatum(app,shortFrame,Unit(plan.Depth),seeds[1],"SHORT_OUTER_WEB_SUPPORT");
                var longWeb=FaceDatum(app,longFrame,Unit(plan.Depth),seeds[farPoint],"LONG_OUTER_WEB_DATUM");
                var nearOwn=EdgeDatum(app,shortFrame,(Face2)ownWeb.Native,Unit(plan.OutWinner),Common(plan,plan.Winner.NearTangent,plan.Loser.Thickness),"SHORT_NEAR_WEB_BEND_TANGENT");
                var nearLong=pinnedNearQ?EdgeDatum(app,longFrame,(Face2)longWeb.Native,Unit(plan.OutLoser),seeds[seamEdge],"LONG_NEAR_WEB_BEND_TANGENT_FOR_ADJUSTED_BISECTOR"):null;
                var farOwn=EdgeDatum(app,shortFrame,(Face2)ownWeb.Native,Unit(plan.OutWinner),seeds[farPoint],"SHORT_FAR_WEB_BEND_TANGENT");
                var nearOther=FaceDatum(app,longFrame,Unit(plan.OutWinner),seeds[1],"LONG_INNER_NEAR_WALL_PLANE");
                var farOther=EdgeDatum(app,longFrame,(Face2)longWeb.Native,Unit(plan.OutLoser),seeds[farPoint],"LONG_FAR_WEB_BEND_TANGENT");
                var innerOwn=FaceDatum(app,shortFrame,Unit(plan.OutLoser),seeds[innerPoint],"SHORT_INNER_FAR_WALL_PLANE");
                var outerOther=FaceDatum(app,longFrame,Unit(plan.OutWinner),seeds[outerPoint],"LONG_OUTER_FAR_WALL_PLANE");
                double[][] closureWorld={Common(plan,plan.Winner.MinWidth,plan.Loser.Thickness),Common(plan,plan.Winner.MaxWidth,plan.Loser.Width),Common(plan,sMax,qMax-m)};
                var closureNear=FaceDatum(app,shortFrame,Unit(plan.OutLoser),closureWorld[0],"SHORT_NEAR_OUTER_SUPPORT_FOR_MARGIN");
                var closureFar=FaceDatum(app,shortFrame,Unit(plan.OutLoser),closureWorld[1],"SHORT_MAX_PROFILE_SUPPORT_FOR_MARGIN");
                bool endDominates=shortEnd>=plan.Loser.MaxWidth;
                var closureAxial=FaceDatum(app,endDominates?shortFrame:longFrame,Unit(plan.OutWinner),closureWorld[2],endDominates?"SHORT_ACTUAL_AXIAL_END_FOR_MARGIN":"LONG_MAX_PROFILE_SUPPORT_FOR_MARGIN");
                result.AxialClosureDatum=closureAxial.Role;
                foreach(var source in new[]{ownWeb,longWeb,nearOwn,farOwn,nearOther,farOther,innerOwn,outerOther,closureNear,closureFar,closureAxial})
                    Need((Same(app,source.Component,shortFrame)?beforeIds:longBeforeIds).Contains(source.OwnerFeatureId),"datum owner does not predate new Sketch");
                Trace.WriteLine("[AFJ-U-MASTER] DATUMS_READY "+DateTime.UtcNow.ToString("o"));
                model.ClearSelection2(true);Need(shortFrame.Select4(false,null,false),"target select failed");int information=0;assembly.EditPart2(true,false,ref information);editing=true;
                Need(Same(app,assembly.GetEditTarget(),part)&&Same(app,assembly.GetEditTargetComponent(),shortFrame),"exact target context missing");
                model.ClearSelection2(true);Need(((IEntity)ownWeb.Context).Select4(false,null),"own Web select failed");sketch=OpenOwnedSketch(part,shortFrame,"U_RULE:COVER_WEB_WALL");opened=true;
                Need(sketch!=null&&!sketch.Is3D(),"2D native Sketch unavailable");
                Feature feature=sketch as Feature;Need(feature!=null,"Sketch QI Feature unavailable");result.SketchFeatureId=feature.GetID();
                int refType=0;object reference=sketch.GetReferenceEntity(ref refType);result.SupportReferenceType=refType;
                result.OwnSupportVerified=reference is Face2&&(Same(app,reference,ownWeb.Context)||Same(app,reference,ownWeb.Native));Need(result.OwnSupportVerified,"actual own Web attachment unavailable");
                Trace.WriteLine("[AFJ-U-MASTER] SKETCH_OPENED "+DateTime.UtcNow.ToString("o"));
                MathTransform inverseTarget=shortFrame.GetTotalTransform(false).Inverse() as MathTransform;Need(inverseTarget!=null,"component inverse unavailable");
                Func<double[],double[]> localPoint=world=>Point(app,Point(app,world,inverseTarget),sketch.ModelToSketchTransform);
                var local=seeds.Select(localPoint).ToArray();var closureLocal=closureWorld.Select(localPoint).ToArray();
                double[][] marginWorld={Common(plan,sMin,qf),Common(plan,sMax,plan.Loser.Thickness),Common(plan,sn,qMax)};
                closureWorld=new[]{Common(plan,plan.Winner.MinWidth,qf),Common(plan,plan.Winner.MaxWidth,plan.Loser.Thickness),Common(plan,sn,qMax-m)};
                closureLocal=closureWorld.Select(localPoint).ToArray();var marginLocal=marginWorld.Select(localPoint).ToArray();
                Need(local.Concat(closureLocal).All(p=>Math.Abs(p[2])<1e-8),"target Part→Sketch point off native plane");
                Trace.WriteLine("[AFJ-U-MASTER] CREATE_LINES "+DateTime.UtcNow.ToString("o"));
                var segments=new SketchSegment[vertexCount];var margin=new SketchSegment[3];
                var bisectorLegs=new SketchSegment[naturalNear?2:0];
                double[][] legWorldA=naturalNear?new[]{Common(plan,sn,plan.SeamNear[1]),Common(plan,sn,qf)}:new double[0][];
                double[][] legWorldB=naturalNear?new[]{Common(plan,sn,qf),Common(plan,sf,qf)}:new double[0][];
                var legLocalA=legWorldA.Select(localPoint).ToArray();var legLocalB=legWorldB.Select(localPoint).ToArray();
                bool previousAdd=model.SketchManager.AddToDB;
                try
                {
                    model.SketchManager.AddToDB=true;
                    for(int i=0;i<vertexCount;i++){var a=local[i];var b=local[(i+1)%vertexCount];segments[i]=model.SketchManager.CreateLine(a[0],a[1],0,b[0],b[1],0);Need(segments[i]!=null,"profile line null");}
                    for(int i=0;i<3;i++){var a=marginLocal[i];var b=closureLocal[i];Need(Math.Abs(Norm(Sub(a,b))-m)<1e-8,"margin line not actual m");margin[i]=model.SketchManager.CreateLine(a[0],a[1],0,b[0],b[1],0);Need(margin[i]!=null,"margin line null");margin[i].ConstructionGeometry=true;}
                    for(int i=0;i<bisectorLegs.Length;i++){var a=legLocalA[i];var b=legLocalB[i];bisectorLegs[i]=model.SketchManager.CreateLine(a[0],a[1],0,b[0],b[1],0);Need(bisectorLegs[i]!=null,"bisector leg null");bisectorLegs[i].ConstructionGeometry=true;}
                }
                finally{model.SketchManager.AddToDB=previousAdd;}
                // SOLIDWORKS API context matrix proved that direct CreateLine
                // handles are not usable for line relations in this assembly
                // edit context. Reacquire from the ASSEMBLY-context Sketch
                // Feature, not the native Part Feature with the same ID.
                var batch=Reacquire(feature,segments.Concat(margin).Concat(bisectorLegs).ToArray(),local.Concat(marginLocal).Concat(legLocalA).ToArray(),local.Skip(1).Concat(new[]{local[0]}).Concat(closureLocal).Concat(legLocalB).ToArray(),Enumerable.Repeat(false,vertexCount).Concat(Enumerable.Repeat(true,3+bisectorLegs.Length)).ToArray(),result);
                for(int i=0;i<vertexCount;i++)segments[i]=batch.Lines[i];for(int i=0;i<3;i++)margin[i]=batch.Lines[vertexCount+i];for(int i=0;i<bisectorLegs.Length;i++)bisectorLegs[i]=batch.Lines[vertexCount+3+i];sketch=batch.Sketch;CheckPolygon(segments,local);
                Trace.WriteLine("[AFJ-U-MASTER] CONTEXT_HANDLES_REACQUIRED profile="+vertexCount+" guides="+(3+bisectorLegs.Length)+" single_allowed="+String.Join(",",Allowed(sketch,segments[0])));
                Trace.WriteLine("[AFJ-U-MASTER] CONNECT_PROFILE "+DateTime.UtcNow.ToString("o"));
                for(int i=0;i<vertexCount;i++){var a=LineEnd(segments[i]);var b=LineStart(segments[(i+1)%vertexCount]);if(!Same(app,a,b))AddRelation(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,a,b);}
                Trace.WriteLine("[AFJ-U-MASTER] FIRST_SEGMENT id="+String.Join(",",((Array)segments[0].GetID()).Cast<object>().Select(Convert.ToString))+" type="+segments[0].GetType()+" construction="+segments[0].ConstructionGeometry+" sketch_id="+((Feature)segments[0].GetSketch()).GetID()+" active_id="+((Feature)sketch).GetID()+" AddToDB="+model.SketchManager.AddToDB);
                Trace.WriteLine("[AFJ-U-MASTER] ALLOWED_FIRST_SINGLE="+String.Join(",",Allowed(sketch,segments[0]))+" line="+String.Join(",",Allowed(sketch,segments[0] as SketchLine))+" own_context="+String.Join(",",Allowed(sketch,segments[0],nearOwn.Context))+" own_native="+String.Join(",",Allowed(sketch,segments[0],nearOwn.Native))+" line_context="+String.Join(",",Allowed(sketch,segments[0] as SketchLine,nearOwn.Context)));
                CheckPolygon(segments,local);
                Trace.WriteLine("[AFJ-U-MASTER] AXIS_RELATIONS "+DateTime.UtcNow.ToString("o"));
                for(int i=0;i<vertexCount;i++)
                {
                    if(i==seamEdge)continue;double[] worldDirection=Unit(Sub(seeds[(i+1)%vertexCount],seeds[i]));
                    Datum axis=Math.Abs(Dot(worldDirection,Unit(plan.OutWinner)))>.999999?nearOwn:Math.Abs(Dot(worldDirection,Unit(plan.OutLoser)))>.999999?farOther:null;
                    Need(axis!=null,"unexpected non-axis contour edge");AddRelation(sketch,Math.Abs(Dot(worldDirection,Unit(plan.OutWinner)))>.999999?(int)swConstraintType_e.swConstraintType_PARALLEL:(int)swConstraintType_e.swConstraintType_PERPENDICULAR,segments[i],nearOwn.Context);result.AxisRelations++;
                }
                Trace.WriteLine("[AFJ-U-MASTER] CRITICAL_RELATIONS "+DateTime.UtcNow.ToString("o"));
                SketchPoint[] points=segments.Select(LineStart).ToArray();int[] critical={1,farPoint,innerPoint,outerPoint};
                Datum[][] datumPairs={new[]{nearOwn,nearOther},new[]{farOwn,farOther},new[]{innerOwn,farOther},new[]{innerOwn,outerOther}};
                string[] roles={"MASTER_NEAR","MASTER_FAR","INNER_FAR_ROW_SHARED","OUTER_FAR_COLUMN_SHARED"};
                for(int i=0;i<4;i++)
                {
                    int index=critical[i];var first=AtSeed(app,i==0&&pinnedNearQ?nearOther:datumPairs[i][0],seeds[index]);var second=AtSeed(app,datumPairs[i][1],seeds[index]);
                    var row=new PointEvidence{Role=roles[i],PointId=PointId(points[index]),ExpectedWorld=seeds[index],ExpectedSketch=local[index],First=first,Second=second};result.Points.Add(row);
                    row.AllowedFirst=Allowed(sketch,points[index],first.Context);row.AllowedSecond=Allowed(sketch,points[index],second.Context);
                    // Six independent coordinate constraints. The other two
                    // gate2 relations are implied by connected row/column edges.
                    if(i<=2&&!(i==0&&pinnedNearQ)){row.FirstRelationType=Coincident(sketch,points[index],first);result.CriticalDatumRelations++;}
                    if(i<=1||i==3){row.SecondRelationType=Coincident(sketch,points[index],second);result.CriticalDatumRelations++;}
                    row.ObservedSketch=new[]{points[index].X,points[index].Y,points[index].Z};
                    row.Qualification=i==0&&pinnedNearQ?"ONE_NATIVE_NEAR_WALL_DATUM_OTHER_COORDINATE_FROM_EQUAL_LEG_BISECTOR":i<2?"TWO_DIRECT_NATIVE_DATUMS":"ONE_DIRECT_NATIVE_DATUM_OTHER_COORDINATE_SHARED_BY_PROFILE";
                }
                if(naturalNear)
                {
                    // Unequal widths: the seam endpoint has no arbitrary q
                    // dimension. A native right triangle with equal legs makes
                    // it the geometric bisector through the far tangency pair.
                    var n=points[seamEdge];var f=points[farPoint];
                    if(pinnedNearQ){Coincident(sketch,n,AtSeed(app,nearLong,seeds[seamEdge]));result.CriticalDatumRelations++;}
                    if(!Same(app,LineStart(bisectorLegs[0]),n))AddRelation(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,LineStart(bisectorLegs[0]),n);
                    if(!Same(app,LineEnd(bisectorLegs[0]),LineStart(bisectorLegs[1])))AddRelation(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,LineEnd(bisectorLegs[0]),LineStart(bisectorLegs[1]));
                    if(!Same(app,LineEnd(bisectorLegs[1]),f))AddRelation(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,LineEnd(bisectorLegs[1]),f);
                    AddRelation(sketch,(int)swConstraintType_e.swConstraintType_PARALLEL,bisectorLegs[0],nearOwn.Context);
                    AddRelation(sketch,(int)swConstraintType_e.swConstraintType_PERPENDICULAR,bisectorLegs[1],nearOwn.Context);
                    AddRelation(sketch,(int)swConstraintType_e.swConstraintType_SAMELENGTH,bisectorLegs[0],bisectorLegs[1]);
                    result.AxisRelations+=2;
                }
                Trace.WriteLine("[AFJ-U-MASTER] CLOSURE_RELATIONS "+DateTime.UtcNow.ToString("o"));
                Datum[] closureDatums={closureNear,closureFar,closureAxial};
                for(int i=0;i<3;i++)
                {
                    var a=LineStart(margin[i]);
                    if(i<2){AddRelation(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,a,segments[i==0?vertexCount-1:rightPoint]);AddRelation(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,a,segments[i==0?farPoint:0]);}
                    else{AddRelation(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,a,segments[topPoint]);
                        var p=points[1];Need(Math.Abs(a.X-p.X)<1e-8||Math.Abs(a.Y-p.Y)<1e-8,"native Sketch basis not aligned to Frame guide");
                        AddRelation(sketch,Math.Abs(a.X-p.X)<1e-8?(int)swConstraintType_e.swConstraintType_VERTPOINTS:(int)swConstraintType_e.swConstraintType_HORIZPOINTS,a,p);}
                    AddRelation(sketch,i<2?(int)swConstraintType_e.swConstraintType_PERPENDICULAR:(int)swConstraintType_e.swConstraintType_PARALLEL,margin[i],nearOwn.Context);
                    Datum datum=AtSeed(app,closureDatums[i],closureWorld[i]);Coincident(sketch,LineEnd(margin[i]),datum);result.ClosureDatums.Add(datum);result.ClosureGuides++;
                    if(i>0){AddRelation(sketch,(int)swConstraintType_e.swConstraintType_SAMELENGTH,margin[0],margin[i]);result.EqualMarginRelations++;}
                }
                CheckPolygon(segments,local);
                // Exactly one manufacturing dimension, created at its measured
                // seed length. Never set or overwrite a source dimension.
                Trace.WriteLine("[AFJ-U-MASTER] DIMENSION_BEGIN "+DateTime.UtcNow.ToString("o"));
                oldInputPreference=app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate);inputPreferenceRead=true;
                app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate,false);
                model.ClearSelection2(true);Need(margin[0].Select4(false,null),"margin line select failed");
                double[] label=Add(closureWorld[0],Add(Mul(plan.OutLoser,-.004),Mul(plan.OutWinner,.004)));
                Trace.WriteLine("[AFJ-U-MASTER] DIMENSION_CREATE "+DateTime.UtcNow.ToString("o"));
                var display=model.AddDimension2(label[0],label[1],label[2]) as DisplayDimension;var dimension=display==null?null:display.GetDimension2(0) as Dimension;
                Trace.WriteLine("[AFJ-U-MASTER] DIMENSION_READBACK "+DateTime.UtcNow.ToString("o"));
                Need(dimension!=null&&dimension.DrivenState==(int)swDimensionDrivenState_e.swDimensionDriving&&Math.Abs(Math.Abs(dimension.SystemValue)-m)<1e-8,"one native ToolClosureMargin driving dimension did not read back actual m");
                dimension.Name="AFJ_ToolClosureMargin";result.MarginDimensionName=dimension.FullName;result.MarginDimensionValue=dimension.SystemValue;
                app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate,oldInputPreference);inputPreferenceRead=false;
                Trace.WriteLine("[AFJ-U-MASTER] CONSTRAINT_READBACK "+DateTime.UtcNow.ToString("o"));
                CheckPolygon(segments,local);result.SeedPolygonUnchanged=true;
                result.ConstraintStatus=sketch.GetConstrainedStatus();result.Relations=sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swAll);
                result.Dangling=sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swDangling);result.Overdefining=sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swOverDefining);
                result.AllPointsFullyConstrained=result.ConstraintStatus==(int)swConstrainedStatus_e.swFullyConstrained&&result.Dangling==0&&result.Overdefining==0;
                Need(result.AllPointsFullyConstrained,"full cover profile did not become valid native status3");
                Trace.WriteLine("[AFJ-U-MASTER] CLOSE_SKETCH "+DateTime.UtcNow.ToString("o"));
                model.SketchManager.InsertSketch(true);opened=false;assembly.EditAssembly();editing=false;
                Trace.WriteLine("[AFJ-U-MASTER] CLOSED_READBACK "+DateTime.UtcNow.ToString("o"));
                var owned=FindFeature(part,result.SketchFeatureId);result.SketchOwnerVerified=owned!=null&&!beforeIds.Contains(owned.GetID());Need(result.SketchOwnerVerified,"new Sketch not proven owned by short Part");
                NameOwned(part,owned,"U_RULE:COVER_WEB_WALL");result.SketchName=owned.Name;var final=owned.GetSpecificFeature2() as Sketch;Need(final!=null,"closed Sketch unavailable");
                result.Dimensions=0;for(DisplayDimension dim=owned.GetFirstDisplayDimension() as DisplayDimension;dim!=null;dim=owned.GetNextDisplayDimension(dim) as DisplayDimension)Need(++result.Dimensions<64,"dimension traversal bound");
                result.NoDimensions=false;Need(result.Dimensions==1,"exactly one native manufacturing dimension required");
                result.NewPlanes=Planes(part)-beforePlanes;result.NoNewPlanes=result.NewPlanes==0;result.ExternalReferences=part.Extension.ListExternalFileReferencesCount();
                Need(result.NoNewPlanes&&result.ExternalReferences>0,"plane/external-reference result unqualified");
                Need(final.GetConstrainedStatus()==(int)swConstrainedStatus_e.swFullyConstrained&&final.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swDangling)==0&&final.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swOverDefining)==0,"closed full profile lost native constraint proof");
                result.ContextRestored=Same(app,assembly.GetEditTarget(),model);Need(result.ContextRestored,"context not restored");
                Trace.WriteLine("[AFJ-U-MASTER] COMPLETE "+DateTime.UtcNow.ToString("o"));
                result.Status="PASS_NATIVE_FULL_COVER_SKETCH_ONLY_ONE_MARGIN_DIM_NO_CUT_NO_FOLLOW_TEST";
            }
            catch(Exception ex){result.Status="FAIL_OR_UNVERIFIED";result.Error="HRESULT=0x"+ex.HResult.ToString("X8")+" "+ex;}
            finally
            {
                try{if(inputPreferenceRead&&app!=null)app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate,oldInputPreference);}catch(Exception ex){result.Error+=" INPUT_PREFERENCE_RESTORE "+ex;result.Status="FAIL_OR_UNVERIFIED";}
                try{CleanupPendingSketch();}catch(Exception ex){result.Error+=" SKETCH_CLOSE "+ex;result.Status="FAIL_OR_UNVERIFIED";}
                try{if(assembly!=null&&!Same(app,assembly.GetEditTarget(),model))assembly.EditAssembly();}catch(Exception ex){result.Error+=" EDIT_ASSEMBLY "+ex;result.Status="FAIL_OR_UNVERIFIED";}
                try{if(assembly!=null&&app!=null)result.ContextRestored=Same(app,assembly.GetEditTarget(),model);}catch(Exception ex){result.Error+=" CONTEXT_RECHECK "+ex;result.Status="FAIL_OR_UNVERIFIED";}
            }
            return result;
        }
        private SketchReport WriteLoser(ISldWorks app,ModelDoc2 model,Component2 longFrame,Component2 shortFrame,AfjParametricPlan plan,Feature shortMasterFeature,Action<string> stageSink=null)
        {
            var result=new SketchReport{Scope="FULL_U_LOSER_SKETCH_ONE_GAP_DIM_NO_CUT"};var assembly=model as AssemblyDoc;ModelDoc2 part=null;Sketch sketch=null;bool editing=false,opened=false,prefRead=false,oldPref=false;
            Action<string> mark=s=>{result.Stages.Add(s);System.Diagnostics.Trace.WriteLine("[U-FULL-LOSER] "+s);if(stageSink!=null)stageSink(s);};
            try
            {
                Need(app!=null&&assembly!=null,"bound application/assembly required");result.NativeApi=true;Need(Same(app,app.ActiveDoc,model)&&Same(app,assembly.GetEditTarget(),model),"bound active assembly outside edit context required");
                Need(plan!=null&&plan.Folded==null&&plan.Winner!=null&&plan.Loser!=null&&(plan.Plan.RuleId=="ORTHOGONAL_U_COVER"||plan.Plan.RuleId=="ORTHOGONAL_U_TALLER_COVER"),"current U plan required");
                Need(!plan.Winner.NearReturn&&!plan.Loser.NearReturn&&!plan.Winner.FarReturn&&!plan.Loser.FarReturn,"bounded plain-U only");Need(plan.SeamNear[1]>=plan.Loser.NearTangent-1e-8||Math.Abs(plan.SeamNear[1]-plan.Loser.Thickness)<1e-8,"bounded clamped/natural near seam only");
                part=longFrame.GetModelDoc2() as ModelDoc2;var masterPart=shortFrame.GetModelDoc2() as ModelDoc2;Need(part!=null&&masterPart!=null&&!Same(app,part,masterPart)&&shortMasterFeature!=null,"two independent Parts and actual master required");
                result.MasterFeatureId=shortMasterFeature.GetID();Need(FindFeature(masterPart,result.MasterFeatureId)!=null,"master not in short Part tree");
                ValidateTransactionHistory();
                int beforePlanes=Planes(part);var beforeIds=Ids(part);double g=AfjManufacturingConventions.AssemblyFitGap;
                double m=Math.Max(.01,5*Math.Max(plan.Loser.Thickness+plan.Loser.Radius,plan.Winner.Thickness+plan.Winner.Radius)),sn=plan.SeamNear[0],qn=plan.SeamNear[1],sf=plan.SeamFar[0],qf=plan.SeamFar[1],near=plan.Loser.NearTangent,far=plan.Loser.FarTangent;
                double ds=(sf-sn)/(qf-qn),shift=g*Math.Sqrt(1+ds*ds);Func<double,double> seam=q=>sn+(q-qn)*ds-shift;
                double qHit=qn+(plan.Winner.Thickness+shift-sn)/ds;bool knee=qHit>near+1e-8&&qHit<far-1e-8;
                Need(seam(near)>plan.Winner.Thickness+1e-8||knee,"degenerate near clipping is not admitted");
                int vertexCount=knee?9:8,seamPoint=knee?3:2,farPoint=knee?4:3,farWallPoint=farPoint+1,farTopPoint=farPoint+2,rightTopPoint=farPoint+3;
                double farWall=plan.Winner.Width-plan.Winner.Thickness-g;Need(Math.Abs(farWall-seam(far))>1e-9,"far zero-length extra segment unsupported");
                double longEnd=AfjParametricRules.Extreme(NativeBody(longFrame),longFrame.GetTotalTransform(false),plan.OutLoser,plan.JointOrigin),sMax=Math.Max(longEnd,plan.Winner.MaxWidth)+m,qMin=plan.Loser.MinWidth-m,qTop=plan.Loser.MaxWidth+m;
                var coordinateList=new List<double[]>{new[]{-g,qMin},new[]{-g,near},new[]{Math.Max(plan.Winner.Thickness,seam(near)),near}};
                if(knee)coordinateList.Add(new[]{plan.Winner.Thickness,qHit});
                coordinateList.AddRange(new[]{new[]{seam(far),far},new[]{farWall,far},new[]{farWall,qTop},new[]{sMax,qTop},new[]{sMax,qMin}});
                double[][] coordinates=coordinateList.ToArray();
                var seeds=coordinates.Select(p=>Common(plan,p[0],p[1])).ToArray();result.FullProfileVertexCount=vertexCount;result.GapValue=g;result.SharedMarginValue=m;
                var operations=plan.Plan.A.Operations.Concat(plan.Plan.B.Operations).Where(o=>o.EvidenceId=="U_RULE:LOSER_STEPPED_WEB_WALL_RETURN").ToArray();Need(operations.Length==1&&operations[0].Tools.Count==1,"exact current losing tool missing");var tool=operations[0].Tools[0];Need(tool.Polygon.Length==vertexCount,"reviewed losing target differs from native 8/9-vertex mode");
                for(int i=0;i<vertexCount;i++)Need(Norm(Sub(Add(World(app,tool.Polygon[i],longFrame),Mul(Unit(plan.Depth),AfjManufacturingConventions.CutterEnvelopeOverrun)),seeds[i]))<1e-8,"intent differs from unchanged reviewed losing tool at vertex "+i);
                mark("READ_MASTER_SEAM_AND_ACTUAL_MARGIN0");var masterA=Common(plan,sn,qn);var masterB=Common(plan,sf,qf);var masterDirection=Unit(Sub(masterB,masterA));
                var masterSeam=MasterLine(app,shortFrame,shortMasterFeature,masterA,masterB,false);
                // This is the nonoverlapping margin0 qualified by the current
                // full-cover probe, not any arbitrary line with length m.
                var masterMargin=MasterLine(app,shortFrame,shortMasterFeature,Common(plan,plan.Winner.MinWidth-m,qf),Common(plan,plan.Winner.MinWidth,qf),true);
                object seamContext=shortFrame.GetCorresponding(masterSeam),marginContext=shortFrame.GetCorresponding(masterMargin);Need(seamContext is SketchSegment&&marginContext is SketchSegment,"actual master segment contexts unavailable");
                int masterDims=0;for(DisplayDimension display=shortMasterFeature.GetFirstDisplayDimension() as DisplayDimension;display!=null;display=shortMasterFeature.GetNextDisplayDimension(display) as DisplayDimension){Need(++masterDims<64,"master dimension traversal bound");var dim=display.GetDimension2(0) as Dimension;Need(dim!=null&&dim.DrivenState==(int)swDimensionDrivenState_e.swDimensionDriving&&dim.FullName.IndexOf("AFJ_ToolClosureMargin",StringComparison.Ordinal)>=0&&Math.Abs(Math.Abs(dim.SystemValue)-m)<1e-8,"actual one master margin driving dimension required");}Need(masterDims==1,"one actual master margin dimension required");result.MasterMarginIdentityVerified=true;result.MarginMasterDefinitionIds.Add(SegmentObjectId(masterMargin));
                mark("READ_NATIVE_FRAME_DATUMS");var ownWeb=FaceDatum(app,longFrame,Unit(plan.Depth),seeds[2],"LONG_OUTER_WEB_SUPPORT");var nearEdge=EdgeDatum(app,longFrame,(Face2)ownWeb.Native,Unit(plan.OutLoser),seeds[2],"LONG_NEAR_WEB_BEND_TANGENT");var farEdge=EdgeDatum(app,longFrame,(Face2)ownWeb.Native,Unit(plan.OutLoser),seeds[farPoint],"LONG_FAR_WEB_BEND_TANGENT");
                var shortNear=FaceDatum(app,shortFrame,Unit(plan.OutLoser),Common(plan,0,0),"SHORT_OUTER_NEAR_WALL_PLANE");
                var shortInnerNear=knee?FaceDatum(app,shortFrame,Unit(plan.OutLoser),Common(plan,plan.Winner.Thickness,near),"SHORT_INNER_NEAR_WALL_FOR_BISECTOR_KNEE"):null;
                var shortInnerFar=FaceDatum(app,shortFrame,Unit(plan.OutLoser),Common(plan,plan.Winner.Width-plan.Winner.Thickness,far),"SHORT_INNER_FAR_WALL_PLANE");
                var longNear=FaceDatum(app,longFrame,Unit(plan.OutWinner),Common(plan,0,plan.Loser.MinWidth),"LONG_OUTER_NEAR_WALL_PLANE");var longFar=FaceDatum(app,longFrame,Unit(plan.OutWinner),Common(plan,0,plan.Loser.MaxWidth),"LONG_MAX_PROFILE_SUPPORT_PLANE");
                bool endDominates=longEnd>=plan.Winner.MaxWidth;var axial=FaceDatum(app,endDominates?longFrame:shortFrame,Unit(plan.OutLoser),Common(plan,sMax-m,qf),endDominates?"LONG_ACTUAL_AXIAL_END_PLANE":"SHORT_MAX_PROFILE_SUPPORT_PLANE");
                // No guide overlaps a profile edge. Near gap uses q=0 rather
                // than P1's q=near; the latter overlaps profile segment1.
                double projection=Dot(Sub(seeds[farPoint],masterA),masterDirection);Need(projection>=-1e-8&&projection<=Norm(Sub(masterB,masterA))+1e-8,"normal foot outside finite master seam");
                double[][] gapStart={Common(plan,-g,0),seeds[farWallPoint],seeds[farPoint]},gapEnd={Common(plan,0,0),Common(plan,plan.Winner.Width-plan.Winner.Thickness,far),Add(masterA,Mul(masterDirection,projection))};
                double[][] closureStart={Common(plan,0,qMin),Common(plan,0,qTop),Common(plan,sMax,qf)},closureEnd={Common(plan,0,plan.Loser.MinWidth),Common(plan,0,plan.Loser.MaxWidth),Common(plan,sMax-m,qf)};
                for(int i=0;i<3;i++){Need(Math.Abs(Norm(Sub(gapStart[i],gapEnd[i]))-g)<1e-8,"gap guide seed not g");Need(Math.Abs(Norm(Sub(closureStart[i],closureEnd[i]))-m)<1e-8,"closure guide seed not m");}
                mark("ENTER_LONG_PART_EDIT");model.ClearSelection2(true);Need(longFrame.Select4(false,null,false),"select long target");int information=0;assembly.EditPart2(true,false,ref information);editing=true;Need(Same(app,assembly.GetEditTarget(),part)&&Same(app,assembly.GetEditTargetComponent(),longFrame),"exact long edit target unavailable");
                model.ClearSelection2(true);Need(((IEntity)ownWeb.Context).Select4(false,null),"select actual own Web");sketch=OpenOwnedSketch(part,longFrame,"U_RULE:LOSER_STEPPED_WEB_WALL_RETURN");opened=true;Need(sketch!=null&&!sketch.Is3D(),"2D native Sketch unavailable");
                Feature originalFeature=sketch as Feature;Need(originalFeature!=null,"original Assembly-context Sketch Feature unavailable");result.SketchFeatureId=originalFeature.GetID();int referenceType=0;object reference=sketch.GetReferenceEntity(ref referenceType);result.SupportReferenceType=referenceType;result.OwnSupportVerified=reference is Face2&&(Same(app,reference,ownWeb.Context)||Same(app,reference,ownWeb.Native));Need(result.OwnSupportVerified,"own actual Web attachment not verified");
                var inverse=longFrame.GetTotalTransform(false).Inverse() as MathTransform;Need(inverse!=null,"target inverse missing");Func<double[],double[]> local=p=>Point(app,Point(app,p,inverse),sketch.ModelToSketchTransform);
                var localSeeds=seeds.Select(local).ToArray();var localGapA=gapStart.Select(local).ToArray();var localGapB=gapEnd.Select(local).ToArray();var localClosureA=closureStart.Select(local).ToArray();var localClosureB=closureEnd.Select(local).ToArray();Need(localSeeds.Concat(localGapA).Concat(localGapB).Concat(localClosureA).Concat(localClosureB).All(p=>Math.Abs(p[2])<1e-8),"native Part to Sketch seeds not planar");
                var profile=new SketchSegment[vertexCount];var gap=new SketchSegment[3];var closure=new SketchSegment[3];bool previous=model.SketchManager.AddToDB;
                mark("CREATE_14_LINES_NO_CONSTRAINTS");try{model.SketchManager.AddToDB=true;for(int i=0;i<vertexCount;i++){var a=localSeeds[i];var b=localSeeds[(i+1)%vertexCount];profile[i]=model.SketchManager.CreateLine(a[0],a[1],0,b[0],b[1],0);Need(profile[i]!=null,"profile line null");}for(int i=0;i<3;i++){var a=localGapA[i];var b=localGapB[i];gap[i]=model.SketchManager.CreateLine(a[0],a[1],0,b[0],b[1],0);Need(gap[i]!=null,"gap line null");gap[i].ConstructionGeometry=true;var c=localClosureA[i];var d=localClosureB[i];closure[i]=model.SketchManager.CreateLine(c[0],c[1],0,d[0],d[1],0);Need(closure[i]!=null,"closure line null");closure[i].ConstructionGeometry=true;}}finally{model.SketchManager.AddToDB=previous;}
                mark("REACQUIRE_ORIGINAL_ASSEMBLY_FEATURE_SPECIFIC_HANDLES");var batch=Reacquire(originalFeature,profile.Concat(gap).Concat(closure).ToArray(),localSeeds.Concat(localGapA).Concat(localClosureA).ToArray(),localSeeds.Skip(1).Concat(new[]{localSeeds[0]}).Concat(localGapB).Concat(localClosureB).ToArray(),Enumerable.Repeat(false,vertexCount).Concat(Enumerable.Repeat(true,6)).ToArray(),result);
                for(int i=0;i<vertexCount;i++)profile[i]=batch.Lines[i];for(int i=0;i<3;i++){gap[i]=batch.Lines[vertexCount+i];closure[i]=batch.Lines[vertexCount+3+i];}sketch=batch.Sketch;
                mark("PROFILE_CLOSURE_AND_7_NATIVE_AXIS_DIRECTIONS");for(int i=0;i<vertexCount;i++)if(!Same(app,SegmentEnd(profile[i]),SegmentStart(profile[(i+1)%vertexCount])))RelationChecked(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,SegmentEnd(profile[i]),SegmentStart(profile[(i+1)%vertexCount]));
                for(int i=0;i<vertexCount;i++){if(i==seamPoint)continue;double[] direction=Unit(Sub(seeds[(i+1)%vertexCount],seeds[i]));int type=Math.Abs(Dot(direction,Unit(plan.OutLoser)))>.999999?(int)swConstraintType_e.swConstraintType_PARALLEL:(int)swConstraintType_e.swConstraintType_PERPENDICULAR;RelationChecked(sketch,type,profile[i],nearEdge.Context);result.AxisRelations++;}
                RelationChecked(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,SegmentStart(profile[2]),nearEdge.Context);RelationChecked(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,SegmentStart(profile[farPoint]),farEdge.Context);
                MasterRelationChecked(app,sketch,(int)swConstraintType_e.swConstraintType_PARALLEL,profile[seamPoint],seamContext,masterSeam,result);result.MasterSeamParallelVerified=true;
                if(knee)RelationChecked(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,SegmentStart(profile[2]),AtSeed(app,shortInnerNear,seeds[2]).Context);
                mark("THREE_GAP_GUIDES_SHARED_ONE_GAP_DIM");
                RelationChecked(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,SegmentStart(gap[0]),profile[0]);RelationChecked(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,SegmentStart(gap[0]),longNear.Context);
                if(!Same(app,SegmentStart(gap[1]),SegmentStart(profile[farWallPoint])))RelationChecked(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,SegmentStart(gap[1]),SegmentStart(profile[farWallPoint]));if(!Same(app,SegmentStart(gap[2]),SegmentStart(profile[farPoint])))RelationChecked(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,SegmentStart(gap[2]),SegmentStart(profile[farPoint]));
                for(int i=0;i<3;i++)
                {
                    if(i<2){RelationChecked(sketch,(int)swConstraintType_e.swConstraintType_PARALLEL,gap[i],nearEdge.Context);var datum=AtSeed(app,i==0?shortNear:shortInnerFar,gapEnd[i]);RelationChecked(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,SegmentEnd(gap[i]),datum.Context);result.GapDatums.Add(datum);}else{MasterRelationChecked(app,sketch,(int)swConstraintType_e.swConstraintType_PERPENDICULAR,gap[i],seamContext,masterSeam,result);MasterRelationChecked(app,sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,SegmentEnd(gap[i]),seamContext,masterSeam,result);result.MasterGapFootCoincidentVerified=true;}
                    if(i>0)RelationChecked(sketch,(int)swConstraintType_e.swConstraintType_SAMELENGTH,gap[0],gap[i]);
                }
                mark("THREE_NONOVERLAP_CLOSURES_EQUAL_ACTUAL_MASTER_MARGIN");
                RelationChecked(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,SegmentStart(closure[0]),profile[vertexCount-1]);RelationChecked(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,SegmentStart(closure[0]),shortNear.Context);
                RelationChecked(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,SegmentStart(closure[1]),profile[farTopPoint]);RelationChecked(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,SegmentStart(closure[1]),shortNear.Context);
                RelationChecked(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,SegmentStart(closure[2]),profile[rightTopPoint]);
                double[] qAxis=Sub(local(Common(plan,0,1)),local(Common(plan,0,0)));Need(Math.Abs(qAxis[2])<1e-8,"Sketch q axis off plane");bool qIsX=Math.Abs(Math.Abs(Unit(qAxis)[0])-1)<1e-7,qIsY=Math.Abs(Math.Abs(Unit(qAxis)[1])-1)<1e-7;Need(qIsX||qIsY,"native point alignment requires measured axis-aligned Sketch basis");
                RelationChecked(sketch,qIsX?(int)swConstraintType_e.swConstraintType_VERTPOINTS:(int)swConstraintType_e.swConstraintType_HORIZPOINTS,SegmentStart(closure[2]),SegmentStart(profile[farPoint]));
                Datum[] closures={longNear,longFar,axial};for(int i=0;i<3;i++){RelationChecked(sketch,i<2?(int)swConstraintType_e.swConstraintType_PERPENDICULAR:(int)swConstraintType_e.swConstraintType_PARALLEL,closure[i],nearEdge.Context);var datum=AtSeed(app,closures[i],closureEnd[i]);RelationChecked(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,SegmentEnd(closure[i]),datum.Context);result.ClosureDatums.Add(datum);MasterRelationChecked(app,sketch,(int)swConstraintType_e.swConstraintType_SAMELENGTH,closure[i],marginContext,masterMargin,result);result.MarginEqualRelations++;result.ClosureCount++;}
                CheckFullPolygon(profile,localSeeds);
                mark("ADD_ONE_ASSEMBLY_FIT_GAP_DIMENSION");oldPref=app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate);prefRead=true;app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate,false);model.ClearSelection2(true);Need(gap[0].Select4(false,null),"select gap guide");var label=Add(gapEnd[0],Mul(plan.OutWinner,.004));var displayGap=model.AddDimension2(label[0],label[1],label[2]) as DisplayDimension;var dimension=displayGap==null?null:displayGap.GetDimension2(0) as Dimension;Need(dimension!=null&&dimension.DrivenState==(int)swDimensionDrivenState_e.swDimensionDriving&&Math.Abs(Math.Abs(dimension.SystemValue)-g)<1e-8,"single measured gap driving dimension missing");dimension.Name="AFJ_AssemblyFitGap";result.GapDimensionName=dimension.FullName;app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate,oldPref);prefRead=false;
                CheckFullPolygon(profile,localSeeds);result.SeedGeometryUnchanged=true;result.ConstraintStatus=sketch.GetConstrainedStatus();result.Relations=sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swAll);result.Dangling=sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swDangling);result.Overdefining=sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swOverDefining);Need(result.ConstraintStatus==(int)swConstrainedStatus_e.swFullyConstrained&&result.Dangling==0&&result.Overdefining==0,"full losing profile not valid native full3");result.AllPointsFullyConstrained=true;
                mark("CLOSE_SKETCH_AND_RESTORE_ASSEMBLY");model.SketchManager.InsertSketch(true);opened=false;assembly.EditAssembly();editing=false;var owned=FindFeature(part,result.SketchFeatureId);result.SketchOwnerVerified=owned!=null&&!beforeIds.Contains(owned.GetID());Need(result.SketchOwnerVerified,"full losing Sketch not proven owned by long Part");NameOwned(part,owned,"U_RULE:LOSER_STEPPED_WEB_WALL_RETURN");result.SketchName=owned.Name;
                result.Dimensions=0;for(DisplayDimension display=owned.GetFirstDisplayDimension() as DisplayDimension;display!=null;display=owned.GetNextDisplayDimension(display) as DisplayDimension)Need(++result.Dimensions<64,"dimension traversal bound");Need(result.Dimensions==1,"exactly one local gap dimension required");result.NoDimensions=false;result.NewPlanes=Planes(part)-beforePlanes;result.NoNewPlanes=result.NewPlanes==0;result.ExternalReferences=part.Extension.ListExternalFileReferencesCount();Need(result.NoNewPlanes&&result.ExternalReferences>0,"new plane/external reference result not qualified");
                var closed=owned.GetSpecificFeature2() as Sketch;Need(closed!=null&&closed.GetConstrainedStatus()==(int)swConstrainedStatus_e.swFullyConstrained&&closed.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swDangling)==0&&closed.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swOverDefining)==0,"closed full losing Sketch lost constraints");
                result.ContextRestored=Same(app,assembly.GetEditTarget(),model);Need(result.ContextRestored,"assembly context not restored");result.Status="PASS_NATIVE_FULL_LOSER_SKETCH_ONE_GAP_DIM_SHARED_MASTER_MARGIN_NO_CUT_NO_FOLLOW_TEST";
            }
            catch(Exception ex){result.Status="FAIL_OR_UNVERIFIED";result.Error="HRESULT=0x"+ex.HResult.ToString("X8")+" "+ex;}
            finally
            {
                try{if(prefRead&&app!=null)app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate,oldPref);}catch(Exception ex){result.Status="FAIL_OR_UNVERIFIED";result.Error+=" PREF_RESTORE "+ex;}
                try{CleanupPendingSketch();}catch(Exception ex){result.Status="FAIL_OR_UNVERIFIED";result.Error+=" SKETCH_CLOSE "+ex;}
                try{if(assembly!=null&&!Same(app,assembly.GetEditTarget(),model))assembly.EditAssembly();}catch(Exception ex){result.Status="FAIL_OR_UNVERIFIED";result.Error+=" EDIT_RESTORE "+ex;}
                try{if(app!=null&&assembly!=null)result.ContextRestored=Same(app,assembly.GetEditTarget(),model);}catch(Exception ex){result.Status="FAIL_OR_UNVERIFIED";result.Error+=" CONTEXT_RECHECK "+ex;}
            }
            return result;
        }
        private SketchReport WriteCaps(ISldWorks app,ModelDoc2 model,Component2 longFrame,Component2 shortFrame,AfjParametricPlan plan,Feature shortMasterFeature,Feature longLoserFeature,Action<string> stageSink=null)
        {
            var result=new SketchReport();var assembly=model as AssemblyDoc;Sketch sketch=null;bool editing=false,opened=false;
            Action<string> mark=s=>{result.Stages.Add(s);System.Diagnostics.Trace.WriteLine("[U-TWO-CAP-SKETCH] "+s);if(stageSink!=null)stageSink(s);};
            try
            {
                Need(app!=null&&assembly!=null,"bound application/assembly required");result.NativeApi=true;Need(Same(app,app.ActiveDoc,model)&&Same(app,assembly.GetEditTarget(),model),"active bound assembly outside edit context required");
                Need(plan!=null&&plan.Folded==null&&plan.Winner!=null&&plan.Loser!=null&&(plan.Plan.RuleId=="ORTHOGONAL_U_COVER"||plan.Plan.RuleId=="ORTHOGONAL_U_TALLER_COVER"),"current U plan required");
                Need(!plan.Winner.NearReturn&&!plan.Loser.NearReturn&&!plan.Winner.FarReturn&&!plan.Loser.FarReturn,"bounded plain-U only");Need(plan.SeamNear[1]>=plan.Loser.NearTangent-1e-8||Math.Abs(plan.SeamNear[1]-plan.Loser.Thickness)<1e-8,"bounded clamped/natural near seam only");
                var longPart=longFrame.GetModelDoc2() as ModelDoc2;var shortPart=shortFrame.GetModelDoc2() as ModelDoc2;Need(longPart!=null&&shortPart!=null&&!Same(app,longPart,shortPart)&&shortMasterFeature!=null&&longLoserFeature!=null,"two independent Parts and both actual native source Sketches required");
                int masterId=shortMasterFeature.GetID(),loserId=longLoserFeature.GetID();result.MasterFeatureId=masterId;result.LoserFeatureId=loserId;
                Need(FindFeature(shortPart,masterId)!=null&&FindFeature(longPart,loserId)!=null,"source Sketch IDs not in expected Parts");
                ValidateTransactionHistory();
                double g=AfjManufacturingConventions.AssemblyFitGap,m=Math.Max(.01,5*Math.Max(plan.Loser.Thickness+plan.Loser.Radius,plan.Winner.Thickness+plan.Winner.Radius)),sn=plan.SeamNear[0],qn=plan.SeamNear[1],sf=plan.SeamFar[0],qf=plan.SeamFar[1];
                double ds=(sf-sn)/(qf-qn),shift=g*Math.Sqrt(1+ds*ds);Func<double,double> seam=q=>sn+(q-qn)*ds-shift;
                double qHit=qn+(plan.Winner.Thickness+shift-sn)/ds;bool knee=qHit>plan.Loser.NearTangent+1e-8&&qHit<plan.Loser.FarTangent-1e-8;
                Need(seam(plan.Loser.NearTangent)>plan.Winner.Thickness+1e-8||knee,"degenerate near clipping is not admitted");
                double longEnd=AfjParametricRules.Extreme(NativeBody(longFrame),longFrame.GetTotalTransform(false),plan.OutLoser,plan.JointOrigin),sMaxLong=Math.Max(longEnd,plan.Winner.MaxWidth)+m,shortMax=plan.Winner.MaxWidth+m,qTop=plan.Loser.MaxWidth+m,farWall=plan.Winner.Width-plan.Winner.Thickness-g;
                Need(Math.Abs(plan.Loser.FarTangent-qf)<1e-8&&farWall>seam(qf)+1e-8,"bounded source losing far row/gap segment required");
                OneDrivingDimension(shortMasterFeature,"AFJ_ToolClosureMargin",m);OneDrivingDimension(longLoserFeature,"AFJ_AssemblyFitGap",g);result.GapValue=g;result.SharedMarginValue=m;
                mark("READ_ACTUAL_SOURCE_POINTS_AND_NEAR_GAP_SEGMENT");
                var shortA=Common(plan,plan.Winner.Width-plan.Winner.Thickness,qf);var shortB=Common(plan,shortMax,plan.Loser.Width);
                var longBL=Common(plan,seam(qf),qf-g);var longTR=Common(plan,sMaxLong,qTop);var loserP3World=Common(plan,seam(qf),qf);var loserP4World=Common(plan,farWall,qf);
                var sourceMasterP3=SourceEndpoint(app,shortFrame,shortMasterFeature,shortA,Common(plan,plan.Winner.Width-plan.Winner.Thickness,plan.Loser.Width));var sourceMasterP5=SourceEndpoint(app,shortFrame,shortMasterFeature,shortB,Common(plan,plan.Winner.Width-plan.Winner.Thickness,plan.Loser.Width));
                var sourceLoserP3=SourceEndpoint(app,longFrame,longLoserFeature,loserP3World,loserP4World);var sourceLoserP4=SourceEndpoint(app,longFrame,longLoserFeature,loserP4World,loserP3World);var sourceLoserP6=SourceEndpoint(app,longFrame,longLoserFeature,longTR,Common(plan,farWall,qTop));
                var nearGap=MasterLine(app,longFrame,longLoserFeature,Common(plan,-g,0),Common(plan,0,0),true);object nearGapContext=longFrame.GetCorresponding(nearGap);Need(nearGapContext is SketchSegment,"actual losing near-gap guide context missing");
                SketchPoint[][] nativePoints={new[]{sourceMasterP3,sourceMasterP5},new[]{sourceLoserP3,sourceLoserP4,sourceLoserP6}};
                object[][] sourceContexts={nativePoints[0].Select(p=>shortFrame.GetCorresponding(p)).ToArray(),nativePoints[1].Select(p=>longFrame.GetCorresponding(p)).ToArray()};
                Need(sourceContexts.SelectMany(a=>a).All(o=>o is SketchPoint),"actual source Sketch point Assembly contexts unavailable");
                double[][][] expected={new[]{shortA,Common(plan,shortMax,qf),shortB,Common(plan,plan.Winner.Width-plan.Winner.Thickness,plan.Loser.Width)},new[]{longBL,Common(plan,sMaxLong,qf-g),longTR,Common(plan,seam(qf),qTop)}};
                string[] roles={"U_RULE:NORMAL_BEND_CAP","U_RULE:FAR_BEND_CLEARANCE"};Component2[] targets={shortFrame,longFrame};ModelDoc2[] parts={shortPart,longPart};
                for(int capIndex=0;capIndex<2;capIndex++)
                {
                    string role=roles[capIndex];var target=targets[capIndex];var part=parts[capIndex];var seeds=expected[capIndex];var row=new CapEvidence{Role=role,ExpectedWorld=seeds};result.Caps.Add(row);
                    var operations=plan.Plan.A.Operations.Concat(plan.Plan.B.Operations).Where(o=>o.EvidenceId==role).ToArray();Need(operations.Length==1&&operations[0].Tools.Count==1,"exact cap tool missing "+role);var tool=operations[0].Tools[0];Need(tool.Polygon.Length==4,"cap is not four-vertex rectangle "+role);
                    // Independent reviewed cutter starts at -EnvelopeOverrun.
                    // Sketch lies on original outer Web; no native Cut here.
                    for(int i=0;i<4;i++)Need(Norm(Sub(Add(World(app,tool.Polygon[i],target),Mul(Unit(plan.Depth),AfjManufacturingConventions.CutterEnvelopeOverrun)),seeds[i]))<1e-8,"unchanged reviewed cap polygon differs at "+role+" vertex "+i);
                    ValidateTransactionHistory();int beforePlanes=Planes(part);var beforeIds=Ids(part);int beforeDefinitionCount=result.MasterDefinitionIdentitiesVerified;
                    mark(role+":READ_OWN_NATIVE_WEB_AND_AXIS");double[] axis=capIndex==0?Unit(plan.OutWinner):Unit(plan.OutLoser);double[] supportSeed=capIndex==0?Common(plan,sn,qn):Common(plan,seam(plan.Loser.NearTangent),plan.Loser.NearTangent);
                    // The adjusted bisector endpoint may be inside a wider Web.
                    // Use the measured near Bend tangent for orientation only;
                    // rectangle locations remain Coincident with the source Sketches.
                    var axisSeed=capIndex==0?Common(plan,plan.Winner.NearTangent,qn):supportSeed;
                    var web=FaceDatum(app,target,Unit(plan.Depth),supportSeed,"CAP_OWN_OUTER_WEB");var edge=EdgeDatum(app,target,(Face2)web.Native,axis,axisSeed,"CAP_OWN_WEB_TANGENT_AXIS");var supportOwner=FindFeature(part,web.OwnerFeatureId);Need(supportOwner!=null,"cap source support owner unavailable");RequireOriginalDatum(target,web.OwnerFeatureId);
                    mark(role+":ENTER_TARGET_EDIT");model.ClearSelection2(true);Need(target.Select4(false,null,false),"select cap target");int info=0;assembly.EditPart2(true,false,ref info);editing=true;Need(Same(app,assembly.GetEditTarget(),part)&&Same(app,assembly.GetEditTargetComponent(),target),"exact cap edit target unavailable");
                    model.ClearSelection2(true);Need(((IEntity)web.Context).Select4(false,null),"select actual cap Web");sketch=OpenOwnedSketch(part,target,role);opened=true;Need(sketch!=null&&!sketch.Is3D(),"2D cap Sketch unavailable");Feature original=sketch as Feature;Need(original!=null,"original Assembly-context Sketch Feature missing");row.SketchFeatureId=original.GetID();
                    int referenceType=0;var reference=sketch.GetReferenceEntity(ref referenceType);row.SupportReferenceType=referenceType;row.OwnSupportVerified=reference is Face2&&(Same(app,reference,web.Context)||Same(app,reference,web.Native));Need(row.OwnSupportVerified,"actual own Web attachment unverified");
                    var inverse=target.GetTotalTransform(false).Inverse() as MathTransform;Need(inverse!=null,"target inverse missing");Func<double[],double[]> local=p=>Point(app,Point(app,p,inverse),sketch.ModelToSketchTransform);var localSeeds=seeds.Select(local).ToArray();Need(localSeeds.All(p=>Math.Abs(p[2])<1e-8),"cap seeds not planar in actual target Sketch");
                    // The far guide is inside the rectangle at source P4.s;
                    // using P3.s would overlap the left polygon edge.
                    var gapA=local(Common(plan,farWall,qf-g));var gapB=local(loserP4World);var lines=new SketchSegment[4];SketchSegment guide=null;bool previous=model.SketchManager.AddToDB;
                    mark(role+":CREATE_LINES_BEFORE_RELATIONS");try{model.SketchManager.AddToDB=true;for(int i=0;i<4;i++){var a=localSeeds[i];var b=localSeeds[(i+1)%4];lines[i]=model.SketchManager.CreateLine(a[0],a[1],0,b[0],b[1],0);Need(lines[i]!=null,"cap line null");}if(capIndex==1){Need(Math.Abs(gapA[2])+Math.Abs(gapB[2])<1e-8&&Math.Abs(Norm(Sub(gapA,gapB))-g)<1e-8,"far guide exact g seed invalid");guide=model.SketchManager.CreateLine(gapA[0],gapA[1],0,gapB[0],gapB[1],0);Need(guide!=null,"far guide null");guide.ConstructionGeometry=true;}}finally{model.SketchManager.AddToDB=previous;}
                    mark(role+":REACQUIRE_ORIGINAL_ASSEMBLY_FEATURE_SPECIFIC");var aliases=capIndex==0?lines:lines.Concat(new[]{guide}).ToArray();var starts=capIndex==0?localSeeds:localSeeds.Concat(new[]{gapA}).ToArray();var ends=localSeeds.Skip(1).Concat(new[]{localSeeds[0]}).ToArray();if(capIndex==1)ends=ends.Concat(new[]{gapB}).ToArray();var batch=Reacquire(original,aliases,starts,ends,Enumerable.Repeat(false,4).Concat(capIndex==0?new bool[0]:new[]{true}).ToArray(),result);
                    for(int i=0;i<4;i++)lines[i]=batch.Lines[i];if(capIndex==1)guide=batch.Lines[4];sketch=batch.Sketch;row.ReacquiredSegmentCount=batch.Lines.Length;row.ReacquiredSegmentIds.AddRange(batch.Ids);row.ReacquisitionSource="ORIGINAL_ASSEMBLY_CONTEXT_SKETCH_FEATURE_GETSPECIFICFEATURE2";
                    mark(role+":RECTANGLE_CLOSURE_AND_NATIVE_AXES");for(int i=0;i<4;i++){if(!Same(app,SegmentEnd(lines[i]),SegmentStart(lines[(i+1)%4])))RelationChecked(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,SegmentEnd(lines[i]),SegmentStart(lines[(i+1)%4]));var direction=Unit(Sub(seeds[(i+1)%4],seeds[i]));RelationChecked(sketch,Math.Abs(Dot(direction,axis))>.999999?(int)swConstraintType_e.swConstraintType_PARALLEL:(int)swConstraintType_e.swConstraintType_PERPENDICULAR,lines[i],edge.Context);row.AxisRelations++;}
                    if(capIndex==0)
                    {
                        MasterRelationChecked(app,sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,SegmentStart(lines[0]),sourceContexts[0][0],sourceMasterP3,result);MasterRelationChecked(app,sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,SegmentStart(lines[2]),sourceContexts[0][1],sourceMasterP5,result);row.SourcePointIds.Add(PointId(sourceMasterP3));row.SourcePointIds.Add(PointId(sourceMasterP5));
                    }
                    else
                    {
                        MasterRelationChecked(app,sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,SegmentStart(lines[2]),sourceContexts[1][2],sourceLoserP6,result);
                        var qAxis=Unit(Sub(local(Common(plan,0,1)),local(Common(plan,0,0))));bool qIsX=Math.Abs(Math.Abs(qAxis[0])-1)<1e-7,qIsY=Math.Abs(Math.Abs(qAxis[1])-1)<1e-7;Need(qIsX||qIsY,"point alignment requires measured axis-aligned Sketch basis");
                        // Same s, movement along measured q axis. This is a
                        // relation to actual source P3, not coordinate dim.
                        MasterRelationChecked(app,sketch,qIsX?(int)swConstraintType_e.swConstraintType_HORIZPOINTS:(int)swConstraintType_e.swConstraintType_VERTPOINTS,SegmentStart(lines[0]),sourceContexts[1][0],sourceLoserP3,result);
                        RelationChecked(sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,SegmentStart(guide),lines[0]);MasterRelationChecked(app,sketch,(int)swConstraintType_e.swConstraintType_COINCIDENT,SegmentEnd(guide),sourceContexts[1][1],sourceLoserP4,result);
                        RelationChecked(sketch,(int)swConstraintType_e.swConstraintType_PERPENDICULAR,guide,edge.Context);MasterRelationChecked(app,sketch,(int)swConstraintType_e.swConstraintType_SAMELENGTH,guide,nearGapContext,nearGap,result);
                        row.SourcePointIds.Add(PointId(sourceLoserP3));row.SourcePointIds.Add(PointId(sourceLoserP4));row.SourcePointIds.Add(PointId(sourceLoserP6));row.SourceSegmentIds.Add(SegmentObjectId(nearGap));Need(Norm(Sub(new[]{SegmentStart(guide).X,SegmentStart(guide).Y,SegmentStart(guide).Z},gapA))<1e-8&&Norm(Sub(new[]{SegmentEnd(guide).X,SegmentEnd(guide).Y,SegmentEnd(guide).Z},gapB))<1e-8,"constraints changed exact gap guide");
                    }
                    CheckFullPolygon(lines,localSeeds);row.SeedGeometryUnchanged=true;row.ConstraintStatus=sketch.GetConstrainedStatus();row.Relations=sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swAll);row.Dangling=sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swDangling);row.Overdefining=sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swOverDefining);row.SourceDefinitionIdentitiesVerified=result.MasterDefinitionIdentitiesVerified-beforeDefinitionCount;
                    Need(row.ConstraintStatus==(int)swConstrainedStatus_e.swFullyConstrained&&row.Dangling==0&&row.Overdefining==0,"cap not native full3 with valid graph");Need(row.SourceDefinitionIdentitiesVerified==(capIndex==0?2:4),"cap actual source definition identity count differs");
                    mark(role+":CLOSE_AND_RESTORE_CONTEXT");model.SketchManager.InsertSketch(true);opened=false;assembly.EditAssembly();editing=false;var owned=FindFeature(part,row.SketchFeatureId);row.SketchOwnerVerified=owned!=null&&!beforeIds.Contains(row.SketchFeatureId);Need(row.SketchOwnerVerified,"cap Sketch ownership unverified");NameOwned(part,owned,role);row.SketchName=owned.Name;
                    row.Dimensions=0;for(DisplayDimension dimension=owned.GetFirstDisplayDimension() as DisplayDimension;dimension!=null;dimension=owned.GetNextDisplayDimension(dimension) as DisplayDimension)Need(++row.Dimensions<64,"cap dimension traversal bound");row.NewPlanes=Planes(part)-beforePlanes;row.ExternalReferences=part.Extension.ListExternalFileReferencesCount();Need(row.Dimensions==0&&row.NewPlanes==0&&row.ExternalReferences>0,"zero dimensions/new planes/external reference result not qualified");
                    var closed=owned.GetSpecificFeature2() as Sketch;row.ClosedFullyConstrained=closed!=null&&closed.GetConstrainedStatus()==(int)swConstrainedStatus_e.swFullyConstrained&&closed.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swDangling)==0&&closed.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swOverDefining)==0;Need(row.ClosedFullyConstrained,"closed cap Sketch lost valid full3 graph");Need(Same(app,assembly.GetEditTarget(),model),"assembly context not restored between cap Sketches");row.Status="PASS_NATIVE_CAP_SKETCH_ZERO_DIM_ZERO_PLANE_NO_CUT_NO_FOLLOW_TEST";
                }
                foreach(var cap in result.Caps){var owner=FindFeature(cap.Role==roles[0]?shortPart:longPart,cap.SketchFeatureId);var actual=owner==null?null:owner.GetSpecificFeature2() as Sketch;Need(actual!=null&&actual.GetConstrainedStatus()==(int)swConstrainedStatus_e.swFullyConstrained&&actual.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swDangling)==0&&actual.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swOverDefining)==0,"final two-cap graph no longer valid full3");}result.NoDimensions=true;result.NoNewPlanes=true;result.Dimensions=0;result.NewPlanes=0;result.ConstraintStatus=(int)swConstrainedStatus_e.swFullyConstrained;result.Relations=result.Caps.Sum(c=>c.Relations);result.Dangling=0;result.Overdefining=0;result.OwnSupportVerified=true;result.SketchOwnerVerified=true;result.ReacquiredSegmentCount=result.Caps.Sum(c=>c.ReacquiredSegmentCount);result.ReacquisitionSource="ORIGINAL_ASSEMBLY_CONTEXT_SKETCH_FEATURE_GETSPECIFICFEATURE2";result.AxisRelations=result.Caps.Sum(c=>c.AxisRelations);result.ExternalReferences=result.Caps.Sum(c=>c.ExternalReferences);result.AllPointsFullyConstrained=true;result.SeedGeometryUnchanged=true;result.ContextRestored=Same(app,assembly.GetEditTarget(),model);Need(result.Caps.Count==2&&result.ContextRestored,"two qualified cap Sketches/restored context required");result.Status="PASS_NATIVE_TWO_CAP_SKETCHES_ZERO_DIM_ZERO_PLANE_NO_CUT_NO_FOLLOW_TEST";
            }
            catch(Exception ex){result.Status="FAIL_OR_UNVERIFIED";result.Error="HRESULT=0x"+ex.HResult.ToString("X8")+" "+ex;}
            finally
            {
                try{CleanupPendingSketch();}catch(Exception ex){result.Status="FAIL_OR_UNVERIFIED";result.Error+=" SKETCH_CLOSE "+ex;}
                try{if(assembly!=null&&!Same(app,assembly.GetEditTarget(),model))assembly.EditAssembly();}catch(Exception ex){result.Status="FAIL_OR_UNVERIFIED";result.Error+=" EDIT_RESTORE "+ex;}
                try{if(app!=null&&assembly!=null)result.ContextRestored=Same(app,assembly.GetEditTarget(),model);}catch(Exception ex){result.Status="FAIL_OR_UNVERIFIED";result.Error+=" CONTEXT_RECHECK "+ex;}
            }
            return result;
        }

    }
}


namespace ADDIN.Commands.AutoFrameJoint
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Collections.Generic;
    using System.Web.Script.Serialization;
    using System.Security.Cryptography;
    using System.Text;
    using System.Globalization;
    using SolidWorks.Interop.sldworks;
    using SolidWorks.Interop.swconst;

    // R53 exact native ownership journal. Never saves, reloads or deploys.
    // Incomplete Assembly metadata cleanup is reported as ROLLBACK_UNVERIFIED.
    internal sealed class AfjUOwnedFeatureJournal
    {
        internal sealed class FeatureInfo
        {
            public long Sequence;
            public string Document, Kind, Name, Role;
            public int Id;
            public bool TokenCaptured, Removed, ClosedIdentityConfirmed, ContextMetadata, SheetMetalCompanion, PassiveAutomaticOnly;
        }
        internal sealed class DeletionInfo
        {
            public string Document, Kind, Name, Status;
            public int Id, Options;
            public int[] DisappearedOwnedIds;
        }
        internal sealed class DependentInfo
        {
            public string Document, Kind, Name, Evidence;
            public int Id;
            public bool Owned, Protected;
        }
        internal sealed class RollbackReport
        {
            public string Status = "NOT_STARTED", Error;
            public bool Verified, AssemblyContextRestored, ProtectedFeaturesPreserved;
            public bool OriginalTreesAndSuppressionRestored, ZeroOwnedFeaturesRemain;
            public bool SaveApiCalled = false;
            public string DirtyFlagQualification = "NOT_QUALIFIED_NOT_CLEARED";
            public string UndoStackQualification = "NOT_QUALIFIED_NO_UNDO_CALLED";
            public double BodyDifferenceA = Double.NaN, BodyDifferenceB = Double.NaN;
            public double BodyTolerance = 1e-12;
            public bool DirtyA, DirtyB, DirtyAssembly;
            public readonly List<DeletionInfo> Deletions = new List<DeletionInfo>();
            public readonly List<DependentInfo> BlockingDependents = new List<DependentInfo>();
            public readonly List<FeatureInfo> Owned = new List<FeatureInfo>();
        }
        internal sealed class Checkpoint
        {
            internal readonly object Scope;
            internal readonly object Snapshot;
            internal readonly long JournalSequence;
            internal Checkpoint(object scope, object snapshot, long sequence)
            { Scope = scope; Snapshot = snapshot; JournalSequence = sequence; }
        }
        private sealed class Node
        {
            internal Feature Feature;
            internal int Id, Order;
            internal string Type, Kind, Name, ParentPath;
            internal bool Suppressed;
            // Baseline holder identity is retained privately for diagnostics.
            // It is never serialized or accepted as deletion authority.
            internal byte[] PrivateBaselineHolderToken;
            internal string BaselineHolderTokenStatus;
            internal Dictionary<string, object> PrivateBaselineHolderProbe;
            internal Dictionary<string, object> PrivateBaselineReferenceProbe;
        }
        private sealed class Snapshot
        {
            internal readonly Dictionary<int, Node> Nodes = new Dictionary<int, Node>();
            internal readonly List<int> Order = new List<int>();
        }
        private sealed class Scope
        {
            internal ModelDoc2 Document;
            internal string Label, PrivatePath;
            internal Snapshot Baseline;
            internal Body2 Before;
        }
        private sealed class Entry
        {
            internal Scope Scope;
            internal int Id;
            internal long Sequence;
            internal string Kind, Name, Role;
            internal byte[] PrivateToken;
            internal bool Removed, ClosedIdentityConfirmed;
            internal bool ContextMetadata, SheetMetalCompanion;
            internal int CompanionFactoryCutId=-1,CompanionTemplateParentId=-1;
            internal double CompanionThickness,CompanionBendRadius;
            internal int[] CompanionObservedChildIds;
            internal string CompanionObservedChildFingerprint;
            internal int ContextPartIndex=-1,ContextFactoryCutId=-1;
            internal int[] ContextParentIds;
            internal string ContextParentFingerprint,ContextRunPrefix;
        }
        private readonly ISldWorks App;
        private readonly ModelDoc2 Assembly;
        private readonly Scope[] Scopes;
        private readonly List<Entry> Entries = new List<Entry>();
        private readonly Action<string> StageSink;
        private readonly List<Dictionary<string, object>> PrivateReadDiagnostics = new List<Dictionary<string, object>>();
        private long Sequence;
        private bool RollingBack;
        private const int MaxNodes = 2400, MaxDepth = 16, MaxRelations = 2400, MaxParents = 512;

        internal AfjUOwnedFeatureJournal(ISldWorks app, ModelDoc2 assembly,
            ModelDoc2 partA, ModelDoc2 partB, Body2 beforeA, Body2 beforeB,
            Action<string> stageSink = null)
        {
            Need(app != null && assembly != null && partA != null && partB != null, "Bound documents required");
            App = app; Assembly = assembly; StageSink = stageSink;
            Need(!Same(partA, partB) && !Same(partA, assembly) && !Same(partB, assembly), "Three independent bound documents required");
            RequireAssemblyContext();
            Scopes = new[] {
                NewScope(partA, "A", beforeA),
                NewScope(partB, "B", beforeB),
                NewScope(assembly, "ASSEMBLY", null)
            };
            Mark("BASELINES_CAPTURED");
        }
        private Scope NewScope(ModelDoc2 doc, string label, Body2 before)
        {
            Need(doc.GetActiveSketch2() == null, "No preexisting active Sketch allowed in journal baseline");
            var scope = new Scope { Document = doc, Label = label, PrivatePath = doc.GetPathName() };
            Need(!String.IsNullOrWhiteSpace(scope.PrivatePath), "Saved document identity required");
            if (before != null) scope.Before = AfjTemporaryBoolean.Copy(before);
            scope.Baseline = Read(scope);
            if(before!=null)
            {
                var sheets=scope.Baseline.Nodes.Values.Where(node=>node.Type=="SheetMetal").ToArray();
                Need(sheets.Length>0,"Native U writer requires an original SheetMetal feature before mutation");
                foreach(Node sheet in sheets)
                {
                    var parents=sheet.Feature.GetParents() as Array;
                    Need(parents!=null&&parents.Cast<object>().OfType<Feature>().Any(parent=>
                        parent.GetTypeName2()=="TemplateSheetMetal"&&scope.Baseline.Nodes.ContainsKey(parent.GetID())),
                        "Original SheetMetal template parent cannot be established before mutation; sheet_id="+sheet.Id);
                }
            }
            // Baseline keeps primitive rows and private token bytes only.
            // Never query a retained baseline Feature RCW after mutation.
            foreach (Node original in scope.Baseline.Nodes.Values) original.Feature = null;
            return scope;
        }
        private static void Need(bool value, string reason)
        { if (!value) throw new InvalidOperationException("U_OWNED_ROLLBACK_UNVERIFIED: " + reason); }
        private bool Same(object a, object b)
        { return a != null && b != null && App.IsSame(a, b) == (int)swObjectEquality.swObjectSame; }
        private void Mark(string phase)
        { if (StageSink != null) StageSink("U_JOURNAL." + phase + " UTC=" + DateTime.UtcNow.ToString("o")); }
        private Scope Bound(ModelDoc2 doc)
        {
            var scope = Scopes.SingleOrDefault(s => Same(s.Document, doc));
            Need(scope != null, "Document is outside bound A/B/Assembly scope");
            Need(String.Equals(scope.PrivatePath, doc.GetPathName(), StringComparison.OrdinalIgnoreCase), "Bound document identity/path changed");
            return scope;
        }
        private void RequireAssemblyContext()
        {
            var assy = Assembly as AssemblyDoc;
            Need(assy != null && Same(assy.GetEditTarget(), Assembly), "Exit component edit before journal setup/rollback");
            Need(Assembly.GetActiveSketch2() == null, "Exit active Assembly Sketch before journal setup/rollback");
        }
        private Snapshot Read(Scope scope)
        {
            Mark("READ_BEGIN_" + scope.Label);
            var snapshot = new Snapshot();
            // AFJ-R53: feature identity is flat; only the top-level definition
            // sequence is ordered. GetFeatures(false) itself has no ordering
            // contract. Do not walk native UI subfeatures after Cut consumption.
            var topIds = new List<int>();
            var topSeen = new HashSet<int>();
            var topFeatures = new Dictionary<int,Feature>();
            int count = 0;
            for (var feature = scope.Document.FirstFeature() as Feature; feature != null;
                 feature = feature.GetNextFeature() as Feature)
            {
                Need(++count <= MaxNodes, "Top-level Feature count bound exceeded");
                int id = feature.GetID();
                Need(topSeen.Add(id), "Repeated top-level Feature identity");
                topIds.Add(id); topFeatures.Add(id,feature);
            }
            var raw = scope.Document.FeatureManager.GetFeatures(false) as Array;
            Need(raw != null && raw.Length <= MaxNodes, "Flat Feature inventory unavailable or too large");
            var features = new Dictionary<int, Feature>();
            foreach (object value in raw)
            {
                var feature = value as Feature;
                Need(feature != null, "Flat Feature inventory has an unresolved entry");
                int id = feature.GetID();
                Feature prior;
                if (features.TryGetValue(id, out prior))
                    Need(prior.GetTypeName2() == feature.GetTypeName2() && prior.Name == feature.Name &&
                        prior.IsSuppressed() == feature.IsSuppressed(), "Conflicting duplicate flat Feature ID");
                else features.Add(id, feature);
            }
            // The native flat inventory can temporarily omit a newly committed
            // top-level Cut. Unite both direct native inventories, preserving the
            // definition order from FirstFeature/GetNextFeature only.
            foreach(var pair in topFeatures)
            {
                Feature prior;
                if(features.TryGetValue(pair.Key,out prior))
                    Need(prior.GetTypeName2()==pair.Value.GetTypeName2() && prior.Name==pair.Value.Name &&
                        prior.IsSuppressed()==pair.Value.IsSuppressed(),"Conflicting top/flat Feature identity; id="+pair.Key);
                else features.Add(pair.Key,pair.Value);
            }
            // Supplement only an already-confirmed transaction-owned passive
            // companion from its exact native token. A live native object need
            // not remain enumerable after the next factory call.
            foreach(var entry in Entries.Where(item=>item.Scope==scope && item.SheetMetalCompanion && !item.Removed))
            {
                var resolved=ResolveKnownPassiveNativeFeature(entry);if(resolved==null)continue;
                Feature prior;
                if(features.TryGetValue(entry.Id,out prior))
                    Need(prior.GetTypeName2()==resolved.GetTypeName2()&&prior.Name==resolved.Name&&
                        prior.IsSuppressed()==resolved.IsSuppressed(),"Native inventory conflicts with confirmed passive token; id="+entry.Id);
                else features.Add(entry.Id,resolved);
            }
            // Native SheetMetal templates can be omitted from BOTH public
            // inventories. Discover only their exact native parent relationship,
            // before the baseline is frozen; never infer a template after a Cut.
            foreach(Feature sheet in features.Values.Where(feature=>feature.GetTypeName2()=="SheetMetal").ToArray())
            {
                var parents=sheet.GetParents() as Array;
                Need(parents==null||parents.Length<=MaxParents,"SheetMetal parent inventory bound exceeded");
                if(parents==null)continue;
                foreach(object value in parents)
                {
                    var parent=value as Feature;Need(parent!=null,"SheetMetal has unresolved native parent");
                    if(parent.GetTypeName2()!="TemplateSheetMetal")continue;
                    int parentId=parent.GetID();Feature prior;
                    if(features.TryGetValue(parentId,out prior))
                        Need(prior.GetTypeName2()==parent.GetTypeName2()&&prior.Name==parent.Name&&prior.IsSuppressed()==parent.IsSuppressed(),
                            "Conflicting native TemplateSheetMetal parent identity; id="+parentId);
                    else features.Add(parentId,parent);
                }
            }
            Need(features.Count<=MaxNodes,"Native inventory with SheetMetal parents exceeds bound");
            foreach (int id in topIds.Concat(features.Keys.Where(id => !topSeen.Contains(id)).OrderBy(id => id)))
            {
                Feature feature = features[id];
                string type = feature.GetTypeName2();
                string kind = type == "ICE" ? feature.GetTypeName() : type;
                string ownerPath = topSeen.Contains(id) ? "TOP" : "SUB";
                // Capture native absorption ownership for modeling Features.
                // This is an identity fingerprint, not inferred UI hierarchy.
                if (kind == "ProfileFeature" || kind == "3DProfileFeature" ||
                    kind == "Cut" || kind == "SketchBend" || kind == "SMBaseFlange")
                {
                    var owner = feature.GetOwnerFeature() as Feature;
                    ownerPath += "/OWNER=" + (owner == null ? "NONE" : owner.GetID().ToString());
                }
                var node = new Node { Feature = feature, Id = id, Type = type,
                    Kind = kind, Name = feature.Name, Suppressed = feature.IsSuppressed(),
                    ParentPath = ownerPath, Order = snapshot.Order.Count };
                snapshot.Nodes.Add(id, node); snapshot.Order.Add(id);
                if (scope.Baseline == null && scope.Label == "ASSEMBLY" && type == "InContextFeatHolder")
                {
                    // Reject a stale/out-of-context fixture BEFORE creating a
                    // Sketch. Never normalize or remove user Assembly metadata.
                    int state;
                    var token = scope.Document.Extension.GetPersistReference3(feature) as byte[];
                    Need(token != null && token.Length > 0, "Existing Assembly context holder has no native identity");
                    var resolved = scope.Document.Extension.GetObjectByPersistReference3(token, out state) as Feature;
                    Need(state == 0 && resolved != null && resolved.GetID() == id &&
                        resolved.GetTypeName2() == type && resolved.Name == node.Name,
                        "Existing Assembly context holder is invalid; resolve context before Apply");
                }
            }
            Mark("READ_END_" + scope.Label);
            return snapshot;
        }
        private Feature ResolveKnownPassiveNativeFeature(Entry entry)
        {
            Need(entry.SheetMetalCompanion && !entry.Scope.Baseline.Nodes.ContainsKey(entry.Id) &&
                entry.ClosedIdentityConfirmed && entry.PrivateToken!=null,
                "Only an exact confirmed owned passive token may supplement native inventory");
            int state;var feature=entry.Scope.Document.Extension.GetObjectByPersistReference3(entry.PrivateToken,out state) as Feature;
            if(feature==null)
            {
                Need(state==1||state==4,"Passive native token resolution failed without a qualified absence state; id="+entry.Id+";state="+state);
                return null;
            }
            int resolvedId=feature.GetID();string resolvedType=feature.GetTypeName2(),resolvedName=feature.Name;
            Mark("PASSIVE_TOKEN_OBSERVED_expected="+entry.Id+";state="+state+";resolved_id="+resolvedId+
                ";type="+resolvedType+";name="+resolvedName);
            if(state==0&&resolvedId!=entry.Id)
            {
                // Never insert the newly resolved object under the previous ID.
                // A factory-bound companion replacement can reconcile this only
                // after its fresh identity has independently passed all guards.
                Mark("PASSIVE_TOKEN_REBOUND_NOT_INSERTED_expected="+entry.Id+";resolved_id="+resolvedId);
                return null;
            }
            Need(state==0&&resolvedId==entry.Id&&resolvedType=="SheetMetal"&&resolvedName==entry.Name,
                "Confirmed passive token native identity differs; expected_id="+entry.Id+";state="+state+
                ";resolved_id="+resolvedId+";type="+resolvedType+";name="+resolvedName);
            var parents=feature.GetParents() as Array;
            Need(parents!=null&&parents.Length==1&&parents.GetValue(0) is Feature&&
                ((Feature)parents.GetValue(0)).GetID()==entry.CompanionTemplateParentId&&
                ((Feature)parents.GetValue(0)).GetTypeName2()=="TemplateSheetMetal"&&
                entry.Scope.Baseline.Nodes.ContainsKey(entry.CompanionTemplateParentId),
                "Confirmed passive native template parent changed; id="+entry.Id);
            var data=feature.GetDefinition() as SheetMetalFeatureData;
            Need(data!=null&&Math.Abs(data.Thickness-entry.CompanionThickness)<=1e-8&&
                Math.Abs(data.BendRadius-entry.CompanionBendRadius)<=1e-8,
                "Confirmed passive native t/R changed; id="+entry.Id);
            return feature;
        }
        internal Feature[] GetKnownOwnedPassiveNativeFeatures(ModelDoc2 document)
        {
            Scope scope=Bound(document);var result=new List<Feature>();
            foreach(var entry in Entries.Where(item=>item.Scope==scope&&item.SheetMetalCompanion&&!item.Removed))
            {
                var native=ResolveKnownPassiveNativeFeature(entry);if(native!=null)result.Add(native);
            }
            return result.ToArray();
        }
        private static bool RowSame(Node a, Node b, bool includeParent)
        {
            return a.Id == b.Id && a.Type == b.Type && a.Kind == b.Kind &&
                a.Name == b.Name && a.Suppressed == b.Suppressed &&
                (!includeParent || a.ParentPath == b.ParentPath);
        }
        private void CheckProtected(Scope scope, Snapshot now, bool exactFinal)
        {
            foreach (var original in scope.Baseline.Nodes.Values)
            {
                Node live;
                Need(now.Nodes.TryGetValue(original.Id, out live),
                    "Protected original Feature disappeared in " + scope.Label +
                    "; id=" + original.Id + "; type=" + original.Type + "; kind=" + original.Kind +
                    "; name=" + original.Name + "; parent=" + original.ParentPath +
                    "; order=" + original.Order + "; suppressed=" + original.Suppressed +
                    "; baseline_count=" + scope.Baseline.Nodes.Count + "; live_count=" + now.Nodes.Count);
                Need(RowSame(original, live, exactFinal),
                    "Protected original Feature identity/type/name/suppression changed in " + scope.Label +
                    "; id=" + original.Id + "; before_type=" + original.Type + "; after_type=" + live.Type +
                    "; before_kind=" + original.Kind + "; after_kind=" + live.Kind +
                    "; before_name=" + original.Name + "; after_name=" + live.Name +
                    "; before_parent=" + original.ParentPath + "; after_parent=" + live.ParentPath +
                    "; before_suppressed=" + original.Suppressed + "; after_suppressed=" + live.Suppressed +
                    "; exact_final=" + exactFinal);
            }
            if (exactFinal)
            {
                Need(now.Nodes.Count == scope.Baseline.Nodes.Count, "Original flat Feature count not restored in " + scope.Label);
                Need(now.Order.SequenceEqual(scope.Baseline.Order), "Original top-level order and flat identity sequence not restored in " + scope.Label);
            }
        }
        internal Checkpoint CaptureCheckpoint(ModelDoc2 document)
        {
            Need(!RollingBack, "Cannot checkpoint during rollback");
            Scope scope = Bound(document); var snapshot = Read(scope); CheckProtected(scope, snapshot, false);
            return new Checkpoint(scope, snapshot, Sequence);
        }
        internal FeatureInfo RegisterNewNativeFeature(ModelDoc2 document, Feature feature, string role)
        {
            Need(!RollingBack && feature != null, "A returned new native Feature is required");
            Scope scope = Bound(document); int id = feature.GetID();
            Need(!scope.Baseline.Nodes.ContainsKey(id), "Refusing ownership of an original Feature");
            string type = feature.GetTypeName2(), kind = type == "ICE" ? feature.GetTypeName() : type;
            Entry existing = Entries.SingleOrDefault(e => e.Scope == scope && e.Id == id);
            if (existing != null)
            {
                Need(!existing.Removed && existing.Kind == kind, "Registered Feature changed identity/kind");
                RefreshToken(existing, feature); existing.Name = feature.Name; return Public(existing);
            }
            // Record provisional ID before token retrieval or later name/relations
            // can fail; Part tree ownership is checked after the Sketch closes.
            var entry = new Entry { Scope = scope, Id = id, Sequence = ++Sequence,
                Kind = kind, Name = feature.Name, Role = role ?? "NOT_AVAILABLE" };
            Entries.Add(entry); Mark("REGISTER_" + scope.Label + "_" + id);
            RefreshToken(entry, feature); return Public(entry);
        }
        private void RefreshToken(Entry entry, Feature feature)
        {
            byte[] token = entry.Scope.Document.Extension.GetPersistReference3(feature) as byte[];
            Need(token != null && token.Length > 0, "Owned Feature persistent identity unavailable");
            if (entry.PrivateToken != null)
            {
                int status; var prior = entry.Scope.Document.Extension.GetObjectByPersistReference3(entry.PrivateToken, out status) as Feature;
                Need(status == 0 && prior != null && prior.GetID() == entry.Id && Same(prior, feature), "Owned Feature persistent identity changed");
            }
            entry.PrivateToken = token;
        }
        internal FeatureInfo ConfirmClosedNativeFeature(ModelDoc2 document, Feature feature,
            string role, string requiredPrefix)
        {
            Need(!RollingBack && feature != null, "Closed native Feature required");
            RequireAssemblyContext();
            Scope scope = Bound(document); int id = feature.GetID();
            Entry entry = Entries.SingleOrDefault(e => e.Scope == scope && e.Id == id && !e.Removed);
            Need(entry != null && !scope.Baseline.Nodes.ContainsKey(id), "No matching provisional creation");
            Need(entry.Role == role || entry.Role == "SKETCH:" + role, "Closed Feature role differs");
            Need(!String.IsNullOrWhiteSpace(requiredPrefix) && requiredPrefix.StartsWith("AFJ_", StringComparison.Ordinal) &&
                feature.Name.StartsWith(requiredPrefix + "_", StringComparison.Ordinal), "Closed Feature unique prefix differs");
            string type = feature.GetTypeName2(), kind = type == "ICE" ? feature.GetTypeName() : type;
            Need(kind == entry.Kind, "Closed Feature native kind differs");
            var now = Read(scope); CheckProtected(scope, now, false); Node live;
            Need(now.Nodes.TryGetValue(id, out live) && Same(live.Feature, feature), "Closed Feature is not in bound Part inventory");
            byte[] token = scope.Document.Extension.GetPersistReference3(feature) as byte[];
            Need(token != null && token.Length > 0, "Closed Feature native token unavailable");
            int status; var resolved = scope.Document.Extension.GetObjectByPersistReference3(token, out status) as Feature;
            Need(status == 0 && resolved != null && resolved.GetID() == id && Same(resolved, live.Feature),
                "Closed Feature native token roundtrip failed");
            // The active Assembly-context Sketch interface is provisional. Only
            // its closed native Part identity may establish a deletion token.
            // A confirmed token may never be replaced after losing identity.
            if (entry.ClosedIdentityConfirmed) RefreshToken(entry, feature);
            else { entry.PrivateToken = token; entry.ClosedIdentityConfirmed = true; }
            entry.Name = feature.Name; Mark("CONFIRMED_CLOSED_" + scope.Label + "_" + id);
            return Public(entry);
        }
        private string ContextParents(Feature feature, Component2 componentA, Component2 componentB,
            out int partIndex, out int[] parentIds)
        {
            Scope assemblyScope=Scopes[2];
            var raw=feature.GetParents() as Array;
            Need(raw!=null && raw.Length==2,"Context metadata must have exactly baseline MateGroup and occurrence parents");
            var parents=raw.Cast<object>().OfType<Feature>().ToArray();
            Need(parents.Length==2,"Context metadata has a non-Feature parent");
            parentIds=parents.Select(parent=>parent.GetID()).OrderBy(id=>id).ToArray();
            Need(parentIds.Distinct().Count()==2 && parentIds.All(assemblyScope.Baseline.Nodes.ContainsKey),
                "Context metadata parent is not in immutable Assembly baseline");
            var mate=parents.Where(parent=>parent.GetTypeName2()=="MateGroup").ToArray();
            var occurrence=parents.Where(parent=>parent.GetTypeName2()=="Reference").ToArray();
            Need(mate.Length==1 && occurrence.Length==1,"Context metadata parent types differ from the qualified native representation");
            var component=occurrence[0].GetSpecificFeature2() as Component2;
            Need(component!=null,"Reference parent does not expose a native occurrence");
            bool isA=Same(component,componentA),isB=Same(component,componentB);
            Need(isA!=isB,"Context metadata occurrence is not exactly one bound Frame");
            partIndex=isA?0:1;
            Need(Same(component.GetModelDoc2(),Scopes[partIndex].Document),"Context occurrence maps outside bound Part");
            foreach(Feature parent in parents)
            {
                Node original=assemblyScope.Baseline.Nodes[parent.GetID()];
                Need(parent.GetTypeName2()==original.Type && parent.Name==original.Name && parent.IsSuppressed()==original.Suppressed,
                    "Original context parent changed");
            }
            return String.Join("|",parents.Select(parent=>parent.GetID()+":"+parent.GetTypeName2()+":"+parent.Name)
                .OrderBy(value=>value,StringComparer.Ordinal));
        }
        private Component2[] ContextComponents;
        internal FeatureInfo[] CaptureOwnedAssemblyContextFeatures(Component2 componentA,Component2 componentB,string prefix)
        {
            Need(!RollingBack && componentA!=null && componentB!=null && !Same(componentA,componentB),"Bound occurrences required for context capture");
            RequireAssemblyContext();
            Need(!String.IsNullOrWhiteSpace(prefix) && prefix.StartsWith("AFJ_",StringComparison.Ordinal),"Exact run prefix required");
            if(ContextComponents==null)ContextComponents=new[]{componentA,componentB};
            else Need(Same(ContextComponents[0],componentA)&&Same(ContextComponents[1],componentB),"Context capture pair changed");
            Scope scope=Scopes[2];var now=Read(scope);CheckProtected(scope,now,false);
            var added=now.Nodes.Values.Where(node=>!scope.Baseline.Nodes.ContainsKey(node.Id) && EntryFor(scope,node.Id)==null).ToArray();
            Need(added.Length<=2,"Unexplained Assembly Feature delta exceeds the two bound occurrence holders");
            var captured=new List<FeatureInfo>();
            foreach(Node node in added)
            {
                Need(node.Type=="InContextFeatHolder" && node.Kind=="InContextFeatHolder","New Assembly Feature is not qualified context metadata");
                int partIndex;int[] parents;string fingerprint=ContextParents(node.Feature,componentA,componentB,out partIndex,out parents);
                Scope partScope=Scopes[partIndex];
                Need(Entries.Any(candidate=>candidate.Scope==partScope && !candidate.Removed && candidate.Kind=="ProfileFeature" &&
                    candidate.ClosedIdentityConfirmed && candidate.Name.StartsWith(prefix+"_",StringComparison.Ordinal)),
                    "Context holder has no confirmed transaction-owned Sketch in its exact Part");
                Need(!Entries.Any(candidate=>candidate.ContextMetadata && candidate.ContextPartIndex==partIndex && !candidate.Removed),
                    "More than one unexplained new context holder targets one occurrence");
                var entry=new Entry{Scope=scope,Id=node.Id,Sequence=++Sequence,Kind=node.Kind,Name=node.Name,
                    Role="CONTEXT_METADATA:"+partScope.Label,ContextMetadata=true,ContextPartIndex=partIndex,
                    ContextParentIds=parents,ContextParentFingerprint=fingerprint,ContextRunPrefix=prefix};
                Entries.Add(entry);
                // The holder has no public naming contract. Keep its observed native
                // name; unique transaction prefix is recorded in the private ledger.
                // A valid native token is preferred and must never later be replaced.
                var token=scope.Document.Extension.GetPersistReference3(node.Feature) as byte[];
                if(token!=null && token.Length>0)
                {
                    entry.PrivateToken=token;int state;
                    var resolved=scope.Document.Extension.GetObjectByPersistReference3(token,out state) as Feature;
                    entry.ClosedIdentityConfirmed=state==0 && resolved!=null && resolved.GetID()==entry.Id && Same(resolved,node.Feature);
                }
                Mark("CAPTURE_BOUND_CONTEXT_"+partScope.Label+"_"+node.Id+"_TOKEN_CONFIRMED="+entry.ClosedIdentityConfirmed);
                captured.Add(Public(entry));
            }
            return captured.ToArray();
        }
        internal FeatureInfo[] CaptureCutFactoryAssemblyContextFeatures(Component2 componentA,Component2 componentB,
            string prefix,Checkpoint before,ModelDoc2 cutDocument,Feature registeredCut)
        {
            Need(!RollingBack && registeredCut!=null,"Exact returned Cut required for factory context capture");
            RequireAssemblyContext();Scope scope=Scopes[2],partScope=Bound(cutDocument);
            Need(before!=null && before.Scope==scope && partScope!=scope,"Exact Assembly factory checkpoint required");
            Need(ContextComponents!=null&&Same(ContextComponents[0],componentA)&&Same(ContextComponents[1],componentB),
                "Cut factory context pair differs from closed-Sketch capture");
            int cutId=registeredCut.GetID();Entry cutEntry=EntryFor(partScope,cutId);
            Need(cutEntry!=null&&cutEntry.Kind=="Cut"&&cutEntry.ClosedIdentityConfirmed&&cutEntry.Sequence==before.JournalSequence+1,
                "Cut factory context checkpoint is not adjacent to the confirmed native Cut");
            Need(Entries.Where(entry=>entry.Sequence>cutEntry.Sequence).All(entry=>entry.SheetMetalCompanion&&
                entry.Scope==partScope&&entry.CompanionFactoryCutId==cutId),
                "Unexplained creation occurred between Cut factory and context capture");
            var prior=(Snapshot)before.Snapshot;var now=Read(scope);CheckProtected(scope,now,false);
            foreach(Node old in prior.Nodes.Values)
            {
                Node current;Need(now.Nodes.TryGetValue(old.Id,out current)&&RowSame(old,current,false),
                    "Assembly factory changed a preceding native Feature; id="+old.Id);
            }
            var added=now.Nodes.Values.Where(node=>!prior.Nodes.ContainsKey(node.Id)&&EntryFor(scope,node.Id)==null).ToArray();
            Need(added.Length<=1,"One native Part Cut produced more than one unexplained Assembly holder");
            var captured=new List<FeatureInfo>();
            foreach(Node node in added)
            {
                Need(node.Type=="InContextFeatHolder"&&node.Kind=="InContextFeatHolder","Cut factory Assembly delta is not qualified context metadata");
                int partIndex;int[] parentIds;string fingerprint=ContextParents(node.Feature,componentA,componentB,out partIndex,out parentIds);
                Need(Scopes[partIndex]==partScope,"Cut factory holder refers to another occurrence");
                Need(Entries.Any(entry=>entry.Scope==partScope&&!entry.Removed&&entry.Kind=="ProfileFeature"&&
                    entry.ClosedIdentityConfirmed&&entry.Name.StartsWith(prefix+"_",StringComparison.Ordinal)),
                    "Cut factory holder has no confirmed bound Sketch");
                var children=node.Feature.GetChildren() as Array;
                Need(children==null||children.Length==0,"Cut factory holder has unexplained native children");
                var created=new Entry{Scope=scope,Id=node.Id,Sequence=++Sequence,Kind=node.Kind,Name=node.Name,
                    Role="CONTEXT_CUT_FACTORY:"+partScope.Label+":"+cutId,ContextMetadata=true,
                    ContextPartIndex=partIndex,ContextFactoryCutId=cutId,ContextParentIds=parentIds,
                    ContextParentFingerprint=fingerprint,ContextRunPrefix=prefix};
                Entries.Add(created);var token=scope.Document.Extension.GetPersistReference3(node.Feature) as byte[];
                if(token!=null&&token.Length>0)
                {
                    created.PrivateToken=token;int state;
                    var resolved=scope.Document.Extension.GetObjectByPersistReference3(token,out state) as Feature;
                    created.ClosedIdentityConfirmed=state==0&&resolved!=null&&resolved.GetID()==node.Id&&Same(resolved,node.Feature);
                }
                Mark("CAPTURE_EXACT_CUT_ASSEMBLY_CONTEXT_"+partScope.Label+"_"+node.Id+"_TOKEN_CONFIRMED="+created.ClosedIdentityConfirmed);
                captured.Add(Public(created));
            }
            return captured.ToArray();
        }
        private void RequirePartsRestoredBeforeContextFallback(Dictionary<Scope,Snapshot> current)
        {
            Need(Entries.Where(entry=>entry.Scope!=Scopes[2]).All(entry=>entry.Removed),
                "Context identity fallback is forbidden until every owned Part Feature is removed");
            for(int i=0;i<2;i++)
            {
                CheckProtected(Scopes[i],current[Scopes[i]],true);
                double delta=AfjNativeCutCommit.SymmetricDifference(
                    AfjTemporaryBoolean.Copy(AfjNativeCutCommit.CurrentBody(Scopes[i].Document)),Scopes[i].Before);
                Need(AfjTemporaryBoolean.Finite(delta)&&delta<=1e-12,"Part material must be exactly restored before context fallback");
            }
        }
        private Feature ResolveContextMetadata(Entry entry,Snapshot snapshot,Dictionary<Scope,Snapshot> all)
        {
            Node live;if(!snapshot.Nodes.TryGetValue(entry.Id,out live))return null;
            Need(entry.Scope==Scopes[2] && !entry.Scope.Baseline.Nodes.ContainsKey(entry.Id) &&
                live.Type=="InContextFeatHolder" && live.Kind==entry.Kind && live.Name==entry.Name,
                "Context metadata native identity fingerprint differs");
            Need(ContextComponents!=null,"Exact bound context occurrences unavailable");
            int partIndex;int[] parentIds;string fingerprint=ContextParents(live.Feature,ContextComponents[0],ContextComponents[1],out partIndex,out parentIds);
            Need(partIndex==entry.ContextPartIndex && parentIds.SequenceEqual(entry.ContextParentIds) &&
                fingerprint==entry.ContextParentFingerprint,"Context metadata native parent fingerprint changed");
            var children=live.Feature.GetChildren() as Array;
            Need(children==null||children.Length==0,"Context metadata still has native children");
            if(entry.ClosedIdentityConfirmed)
            {
                int state;var resolved=entry.Scope.Document.Extension.GetObjectByPersistReference3(entry.PrivateToken,out state) as Feature;
                Need(state==0&&resolved!=null&&resolved.GetID()==entry.Id&&Same(resolved,live.Feature),
                    "Previously confirmed context native identity was lost");
                return resolved;
            }
            // This narrow fallback belongs only to context metadata declared at
            // the closed-Sketch boundary. It is never used for a Cut/Sketch or an
            // original/unknown Feature and cannot repair an invalid confirmed token.
            RequirePartsRestoredBeforeContextFallback(all);
            return live.Feature;
        }
        internal FeatureInfo[] CaptureCutFactoryCompanions(ModelDoc2 document,Checkpoint before,
            Feature registeredCut,string role,double expectedThickness,double expectedBendRadius)
        {
            Need(System.Threading.Thread.CurrentThread.GetApartmentState()==System.Threading.ApartmentState.STA,
                "Cut factory companion capture requires owner STA");
            Need(!RollingBack && registeredCut!=null,"Exact returned Cut required for factory companion capture");
            RequireAssemblyContext();Scope scope=Bound(document);
            Need(scope!=Scopes[2] && before!=null && before.Scope==scope,"Part factory checkpoint is required");
            int cutId=registeredCut.GetID();var cutEntry=EntryFor(scope,cutId);
            Need(cutEntry!=null && cutEntry.Kind=="Cut" && cutEntry.ClosedIdentityConfirmed &&
                cutEntry.Sequence==before.JournalSequence+1 && Sequence==cutEntry.Sequence,
                "Companion capture is not adjacent to exactly one registered native Cut factory call");
            var prior=(Snapshot)before.Snapshot;
            Need(!prior.Nodes.ContainsKey(cutId) && !scope.Baseline.Nodes.ContainsKey(cutId),"Factory Cut was not new in exact checkpoint");
            var now=Read(scope);CheckProtected(scope,now,false);
            var automaticChanges=new List<Node>();
            foreach(Node old in prior.Nodes.Values)
            {
                Node current;bool present=now.Nodes.TryGetValue(old.Id,out current);
                if(present&&RowSame(old,current,false))continue;
                string detail="id="+old.Id+";before="+old.Type+":"+old.Name+":suppressed="+old.Suppressed+
                    ";current="+(present?current.Type+":"+current.Name+":suppressed="+current.Suppressed:"MISSING");
                Mark("CUT_FACTORY_PRECEDING_DELTA_"+detail);
                var passive=EntryFor(scope,old.Id);
                Need(passive!=null&&passive.SheetMetalCompanion&&passive.ClosedIdentityConfirmed&&
                    !scope.Baseline.Nodes.ContainsKey(old.Id),"Factory changed/disappeared an original or nonpassive preceding Feature; "+detail);
                Need(!present || (old.Type=="SheetMetal"&&current.Type==old.Type&&current.Kind==old.Kind&&current.Name==old.Name&&
                    !old.Suppressed&&current.Suppressed),"Factory passive metadata change is not an observed retirement/suppression; "+detail);
                automaticChanges.Add(old);
            }
            var extra=now.Nodes.Values.Where(node=>!prior.Nodes.ContainsKey(node.Id) && node.Id!=cutId && EntryFor(scope,node.Id)==null).ToArray();
            Need(extra.Length<=1,"Native Cut factory produced more than one unexplained companion");
            Need(now.Nodes.ContainsKey(cutId),"Returned Cut is absent from live Part inventory");
            var captured=new List<FeatureInfo>();
            foreach(Node node in extra)
            {
                Need(node.Type=="SheetMetal" && node.Kind=="SheetMetal","Cut factory delta is not the qualified SheetMetal companion");
                var parents=node.Feature.GetParents() as Array;
                Need(parents!=null && parents.Length==1,"SheetMetal companion requires exactly one baseline TemplateSheetMetal parent");
                var parent=parents.GetValue(0) as Feature;
                Need(parent!=null && parent.GetTypeName2()=="TemplateSheetMetal" && scope.Baseline.Nodes.ContainsKey(parent.GetID()),
                    "SheetMetal companion parent is not a baseline TemplateSheetMetal");
                Node original=scope.Baseline.Nodes[parent.GetID()];
                Need(parent.Name==original.Name && parent.IsSuppressed()==original.Suppressed,
                    "TemplateSheetMetal parent identity changed");
                var data=node.Feature.GetDefinition() as SheetMetalFeatureData;
                Need(data!=null && AfjTemporaryBoolean.Finite(data.Thickness) && AfjTemporaryBoolean.Finite(data.BendRadius) &&
                    expectedThickness>0 && expectedBendRadius>=0 && Math.Abs(data.Thickness-expectedThickness)<=1e-8 &&
                    Math.Abs(data.BendRadius-expectedBendRadius)<=1e-8,"SheetMetal companion t/R differs from original API-measured profile");
                var children=node.Feature.GetChildren() as Array;
                Need(children==null||children.Length<=MaxParents,"Companion child bound exceeded");
                var observedChildren=new List<Feature>();
                if(children!=null)foreach(object value in children)
                {
                    var child=value as Feature;Need(child!=null,"Companion has an unresolved child");
                    observedChildren.Add(child);
                }
                // Native factory metadata can participate in original model
                // dependencies. Owning this passive delta grants NO authority to
                // select, explicitly delete or cascade into those dependents.
                var info=RegisterNewNativeFeature(document,node.Feature,role+":SheetMetalCompanion");
                var entry=EntryFor(scope,info.Id);Need(entry!=null,"Companion provisional registration missing");
                entry.SheetMetalCompanion=true;entry.CompanionFactoryCutId=cutId;entry.CompanionTemplateParentId=parent.GetID();
                entry.CompanionThickness=expectedThickness;entry.CompanionBendRadius=expectedBendRadius;
                entry.CompanionObservedChildIds=observedChildren.Select(child=>child.GetID()).OrderBy(id=>id).ToArray();
                entry.CompanionObservedChildFingerprint=String.Join("|",observedChildren.Select(child=>
                    child.GetID()+":"+child.GetTypeName2()+":"+child.Name).OrderBy(value=>value,StringComparer.Ordinal));
                int state;var resolved=scope.Document.Extension.GetObjectByPersistReference3(entry.PrivateToken,out state) as Feature;
                Need(state==0 && resolved!=null && resolved.GetID()==entry.Id && Same(resolved,node.Feature),
                    "SheetMetal companion exact native token roundtrip failed");
                entry.ClosedIdentityConfirmed=true;Mark("CAPTURE_EXACT_CUT_SHEETMETAL_COMPANION_"+scope.Label+"_"+entry.Id);
                captured.Add(Public(entry));
            }
            Need(automaticChanges.Count==0 || captured.Count==1,
                "A previous owned passive companion changed without a fresh exact template/t/R/token-qualified replacement");
            foreach(Node old in automaticChanges)
            {
                var passive=EntryFor(scope,old.Id);Need(passive!=null&&passive.SheetMetalCompanion,"Passive companion ledger changed");
                Node live;bool present=now.Nodes.TryGetValue(old.Id,out live);
                if(present)
                {
                    var resolved=Resolve(passive,now);Need(resolved!=null,"Suppressed passive companion lost exact native identity");
                    var definition=resolved.GetDefinition() as SheetMetalFeatureData;
                    var parents=resolved.GetParents() as Array;
                    Need(definition!=null&&Math.Abs(definition.Thickness-passive.CompanionThickness)<=1e-8&&
                        Math.Abs(definition.BendRadius-passive.CompanionBendRadius)<=1e-8&&parents!=null&&parents.Length==1&&
                        parents.GetValue(0) is Feature&&((Feature)parents.GetValue(0)).GetID()==passive.CompanionTemplateParentId,
                        "Suppressed passive companion template/t/R identity changed; id="+old.Id);
                    Mark("PASSIVE_COMPANION_NATIVE_SUPPRESSED_BY_CUT_"+cutId+"_ID_"+old.Id);
                }
                else
                {
                    int state;var resolved=scope.Document.Extension.GetObjectByPersistReference3(passive.PrivateToken,out state) as Feature;
                    if(resolved==null)
                    {
                        Need(state==1||state==4,"Missing passive companion native absence state is unqualified; id="+old.Id+";state="+state);
                        passive.Removed=true;Mark("PASSIVE_COMPANION_NATIVE_RETIRED_BY_CUT_"+cutId+"_ID_"+old.Id+"_STATE_"+state);
                    }
                    else
                    {
                        int resolvedId=resolved.GetID();var replacement=EntryFor(scope,captured.Single().Id);Node replacementNode;
                        Mark("PASSIVE_FACTORY_REPLACEMENT_OBSERVED_old="+old.Id+";state="+state+";resolved_id="+resolvedId+
                            ";type="+resolved.GetTypeName2()+";name="+resolved.Name);
                        Need(state==0&&replacement!=null&&replacement.SheetMetalCompanion&&replacement.ClosedIdentityConfirmed&&
                            replacement.CompanionFactoryCutId==cutId&&replacement.CompanionTemplateParentId==passive.CompanionTemplateParentId&&
                            Math.Abs(replacement.CompanionThickness-passive.CompanionThickness)<=1e-8&&
                            Math.Abs(replacement.CompanionBendRadius-passive.CompanionBendRadius)<=1e-8&&resolvedId==replacement.Id&&
                            now.Nodes.TryGetValue(replacement.Id,out replacementNode)&&Same(resolved,replacementNode.Feature),
                            "Missing passive token does not resolve to the exact newly qualified factory companion; old_id="+old.Id+
                            ";state="+state+";resolved_id="+resolvedId);
                        passive.Removed=true;Mark("PASSIVE_COMPANION_EXACT_NATIVE_FACTORY_REPLACEMENT_old="+old.Id+";new="+replacement.Id+";cut="+cutId);
                    }
                }
            }
            return captured.ToArray();
        }
        internal FeatureInfo[] CaptureNewFeatures(ModelDoc2 document, Checkpoint before,
            IEnumerable<int> declaredFeatureIds, string role)
        {
            Scope scope = Bound(document);
            Need(before != null && before.Scope == scope && before.JournalSequence <= Sequence, "Checkpoint does not belong to this document/journal");
            var declared = new HashSet<int>(declaredFeatureIds ?? Enumerable.Empty<int>());
            var now = Read(scope); CheckProtected(scope, now, false);
            var prior = (Snapshot)before.Snapshot;
            foreach (Node old in prior.Nodes.Values)
            {
                Node live; Need(now.Nodes.TryGetValue(old.Id, out live) && RowSame(old, live, false), "Checkpoint Feature changed/disappeared outside declared creation");
            }
            var added = now.Nodes.Values.Where(n => !prior.Nodes.ContainsKey(n.Id)).ToArray();
            Need(added.All(n => declared.Contains(n.Id) || Entries.Any(e => e.Scope == scope && e.Id == n.Id && !e.Removed)), "Undeclared native delta is not automatically owned");
            Need(declared.All(id => now.Nodes.ContainsKey(id) && !scope.Baseline.Nodes.ContainsKey(id)), "Declared rescue ID missing or protected");
            var rows = new List<FeatureInfo>();
            foreach (Node node in added.OrderBy(n => n.Order))
                if (declared.Contains(node.Id)) rows.Add(RegisterNewNativeFeature(document, node.Feature, role));
            return rows.ToArray();
        }
        private FeatureInfo Public(Entry entry)
        {
            return new FeatureInfo { Sequence = entry.Sequence, Document = entry.Scope.Label,
                Id = entry.Id, Kind = entry.Kind, Name = entry.Name, Role = entry.Role,
                TokenCaptured = entry.PrivateToken != null, Removed = entry.Removed,
                ClosedIdentityConfirmed = entry.ClosedIdentityConfirmed, ContextMetadata = entry.ContextMetadata,
                SheetMetalCompanion = entry.SheetMetalCompanion, PassiveAutomaticOnly = entry.SheetMetalCompanion };
        }
        internal FeatureInfo[] GetPublicOwnedFeatures() { return Entries.Select(Public).ToArray(); }
        internal void WritePrivateJournal(string privateFilePath)
        {
            Need(Path.IsPathRooted(privateFilePath) &&
                privateFilePath.EndsWith(".private.json", StringComparison.OrdinalIgnoreCase),
                "Private path/token evidence requires an explicit absolute .private.json target");
            var rows = Entries.Select(e => new Dictionary<string, object> {
                {"sequence", e.Sequence}, {"document_label", e.Scope.Label},
                {"private_document_path", e.Scope.PrivatePath}, {"feature_id", e.Id},
                {"kind", e.Kind}, {"name", e.Name}, {"role", e.Role},
                {"private_persistent_token_base64", e.PrivateToken == null ? null : Convert.ToBase64String(e.PrivateToken)},
                {"removed", e.Removed}
            }).ToArray();
            var serializer = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
            File.WriteAllText(privateFilePath, serializer.Serialize(new Dictionary<string, object> {
                {"scope", "PRIVATE_NATIVE_OWNERSHIP_NOT_FOR_PUBLIC_REPORT"},
                {"utc", DateTime.UtcNow.ToString("o")}, {"owned", rows}
            }));
        }
        private static string DiagnosticError(Exception error)
        { return "HRESULT=0x" + error.HResult.ToString("X8") + " " + error.GetType().FullName + " " + error.Message; }
        private Dictionary<string, object> ProbeFeature(Feature feature)
        {
            var row = new Dictionary<string, object> { {"nonnull", feature != null} };
            if (feature == null) return row;
            try { row["id"] = feature.GetID(); } catch (Exception ex) { row["id_error"] = DiagnosticError(ex); }
            try { row["type"] = feature.GetTypeName2(); } catch (Exception ex) { row["type_error"] = DiagnosticError(ex); }
            try { row["name"] = feature.Name; } catch (Exception ex) { row["name_error"] = DiagnosticError(ex); }
            try { row["suppressed"] = feature.IsSuppressed(); } catch (Exception ex) { row["suppressed_error"] = DiagnosticError(ex); }
            try { row["rolled_back"] = feature.IsRolledBack(); } catch (Exception ex) { row["rolled_back_error"] = DiagnosticError(ex); }
            row["native_feature_equality_queried"] = false;
            return row;
        }
        private static object[] PrimitiveReferenceArray(object raw)
        {
            var array = raw as Array; if (array == null) return new object[0];
            if (array.Length > MaxParents) return new object[] { "REFERENCE_ARRAY_COUNT_BOUND_EXCEEDED" };
            return array.Cast<object>().Select(value => value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture)).Cast<object>().ToArray();
        }
        private Dictionary<string, object> ProbeExternalReferences(Feature feature)
        {
            var row = new Dictionary<string, object>();
            try
            {
                int count = feature.ListExternalFileReferencesCount(); row["count"] = count;
                Need(count >= 0 && count <= MaxParents, "Diagnostic external reference count bound");
                if (count > 0)
                {
                    object paths, components, features, types, status, entities, featureComponents;
                    int configOption; string configName;
                    feature.ListExternalFileReferences2(out paths, out components, out features, out types,
                        out status, out entities, out featureComponents, out configOption, out configName);
                    row["paths"] = PrimitiveReferenceArray(paths); row["components"] = PrimitiveReferenceArray(components);
                    row["features"] = PrimitiveReferenceArray(features); row["types"] = PrimitiveReferenceArray(types);
                    row["statuses"] = PrimitiveReferenceArray(status); row["entity_descriptions"] = PrimitiveReferenceArray(entities);
                    row["feature_components"] = PrimitiveReferenceArray(featureComponents);
                    row["configuration_option"] = configOption; row["configuration_name"] = configName;
                }
                string canonical = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue }.Serialize(row);
                using (var sha = SHA256.Create()) row["sha256"] = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-", "").ToLowerInvariant();
                row["qualified"] = true;
            }
            catch (Exception ex) { row["qualified"] = false; row["error"] = DiagnosticError(ex); }
            return row;
        }
        private object[] ProbeOriginalReferenceSignatures(Scope scope, Snapshot now)
        {
            var rows = new List<object>();
            foreach (Node original in scope.Baseline.Order.Select(id => scope.Baseline.Nodes[id]))
            {
                if (original.PrivateBaselineReferenceProbe == null) continue;
                Node fresh;
                bool freshMatches = now.Nodes.TryGetValue(original.Id, out fresh) && fresh.Type == original.Type && fresh.Name == original.Name;
                var after = freshMatches ? ProbeExternalReferences(fresh.Feature) : new Dictionary<string, object> {
                    {"qualified", false}, {"error", "ORIGINAL_FEATURE_NOT_RESOLVED_IN_FRESH_CURRENT_TREE_BY_ID_TYPE_NAME"}
                }; object priorHash, currentHash;
                bool qualified = original.PrivateBaselineReferenceProbe.ContainsKey("qualified") &&
                    (bool)original.PrivateBaselineReferenceProbe["qualified"] && after.ContainsKey("qualified") && (bool)after["qualified"];
                bool equal = qualified && original.PrivateBaselineReferenceProbe.TryGetValue("sha256", out priorHash) &&
                    after.TryGetValue("sha256", out currentHash) && Object.Equals(priorHash, currentHash);
                rows.Add(new Dictionary<string, object> { {"id", original.Id}, {"type", original.Type}, {"name", original.Name},
                    {"before", original.PrivateBaselineReferenceProbe}, {"after", after}, {"qualified_both", qualified}, {"identical_signatures", equal} });
            }
            return rows.ToArray();
        }
        private object[] ProbeLinkedFeatures(Array raw)
        {
            if (raw == null) return new object[0];
            if (raw.Length > MaxParents) return new object[] { "FEATURE_LINK_COUNT_BOUND_EXCEEDED" };
            var rows = new List<object>();
            foreach (object value in raw)
            {
                var feature = value as Feature;
                if (feature == null) { rows.Add(new Dictionary<string, object> { {"is_feature", false}, {"nonnull", value != null} }); continue; }
                var row = new Dictionary<string, object> { {"is_feature", true} };
                try { row["id"] = feature.GetID(); } catch (Exception ex) { row["id_error"] = DiagnosticError(ex); }
                try { row["type"] = feature.GetTypeName2(); } catch (Exception ex) { row["type_error"] = DiagnosticError(ex); }
                try { row["name"] = feature.Name; } catch (Exception ex) { row["name_error"] = DiagnosticError(ex); }
                rows.Add(row);
            }
            return rows.ToArray();
        }
        private Dictionary<string, object> ProbeBaselineHolder(Scope scope, Node original, string phase = "BASELINE_BEFORE_FIRST_NATIVE_MUTATION")
        {
            Need(scope.Baseline == null && original.Feature != null, "Holder baseline diagnostic must run before baseline RCWs are cleared");
            var row = new Dictionary<string, object> {
                {"phase", phase},
                {"token_capture_status", original.BaselineHolderTokenStatus},
                {"token_present", original.PrivateBaselineHolderToken != null && original.PrivateBaselineHolderToken.Length > 0}
            };
            if (original.PrivateBaselineHolderToken != null && original.PrivateBaselineHolderToken.Length > 0)
            {
                try
                {
                    int status; object resolved = scope.Document.Extension.GetObjectByPersistReference3(original.PrivateBaselineHolderToken, out status);
                    row["immediate_persist_resolve_status"] = status;
                    row["immediate_persist_resolved_nonnull"] = resolved != null;
                    row["immediate_persist_resolved_is_feature"] = resolved is Feature;
                    row["immediate_persist_resolved_feature"] = ProbeFeature(resolved as Feature);
                }
                catch (Exception ex) { row["immediate_persist_resolve_error"] = DiagnosticError(ex); }
            }
            try { row["direct_parents"] = ProbeLinkedFeatures(original.Feature.GetParents() as Array); }
            catch (Exception ex) { row["direct_parents_error"] = DiagnosticError(ex); }
            try { row["direct_children"] = ProbeLinkedFeatures(original.Feature.GetChildren() as Array); }
            catch (Exception ex) { row["direct_children_error"] = DiagnosticError(ex); }
            row["external_references"] = ProbeExternalReferences(original.Feature);
            row["retained_feature"] = ProbeFeature(original.Feature);
            try
            {
                object specific = original.Feature.GetSpecificFeature2();
                row["specific_nonnull"] = specific != null; row["specific_is_feature"] = specific is Feature;
                row["specific_feature"] = ProbeFeature(specific as Feature);
            }
            catch (Exception ex) { row["specific_feature_error"] = DiagnosticError(ex); }
            try { row["owner_feature"] = ProbeFeature(original.Feature.GetOwnerFeature()); }
            catch (Exception ex) { row["owner_feature_error"] = DiagnosticError(ex); }
            return row;
        }
        private object[] MissingOriginalDiagnostics(Scope scope, Snapshot now)
        {
            Node[] missing = scope.Baseline.Order.Where(id => !now.Nodes.ContainsKey(id)).Select(id => scope.Baseline.Nodes[id]).ToArray();
            if (missing.Length == 0) return new object[0];
            Feature[] managerFeatures = null; string managerError = null;
            try
            {
                var raw = scope.Document.FeatureManager.GetFeatures(false) as Array;
                if (raw != null && raw.Length <= MaxNodes) managerFeatures = raw.Cast<object>().OfType<Feature>().ToArray();
                else managerError = raw == null ? "NULL_ARRAY" : "FEATURE_COUNT_BOUND_EXCEEDED";
            }
            catch (Exception ex) { managerError = DiagnosticError(ex); }
            var rows = new List<object>();
            foreach (Node original in missing)
            {
                var row = new Dictionary<string, object> {
                    {"id", original.Id}, {"type", original.Type}, {"name", original.Name}, {"parent_path", original.ParentPath},
                    {"retained_baseline_rcw_queried", false},
                    {"baseline_holder_token_status", original.BaselineHolderTokenStatus ?? "NOT_CAPTURED_BY_DIAGNOSTIC_SCOPE"},
                    {"baseline_holder_probe", original.PrivateBaselineHolderProbe},
                    {"missing_retained_holder_semantics_queried", false},
                    {"baseline_holder_token_present", original.PrivateBaselineHolderToken != null && original.PrivateBaselineHolderToken.Length > 0},
                    {"feature_manager_get_features_false_error", managerError}
                };
                if (original.PrivateBaselineHolderToken != null && original.PrivateBaselineHolderToken.Length > 0)
                {
                    try
                    {
                        int status; object resolved = scope.Document.Extension.GetObjectByPersistReference3(original.PrivateBaselineHolderToken, out status);
                        row["persist_resolve_status"] = status; row["persist_resolved_nonnull"] = resolved != null;
                        row["persist_resolved_is_feature"] = resolved is Feature;
                        row["persist_resolved_feature"] = ProbeFeature(resolved as Feature);
                    }
                    catch (Exception ex) { row["persist_resolve_error"] = DiagnosticError(ex); }
                }
                try
                {
                    var assembly = scope.Document as AssemblyDoc; var part = scope.Document as PartDoc;
                    Feature byName = assembly != null ? assembly.FeatureByName(original.Name) as Feature :
                        part != null ? part.FeatureByName(original.Name) as Feature : null;
                    row["feature_by_exact_name"] = ProbeFeature(byName);
                }
                catch (Exception ex) { row["feature_by_exact_name_error"] = DiagnosticError(ex); }
                if (managerFeatures != null)
                {
                    var matches = new List<object>(); var errors = new List<object>();
                    foreach (Feature candidate in managerFeatures)
                    {
                        try
                        {
                            int candidateId = candidate.GetID();
                            string candidateName = candidate.Name;
                            if (candidateId == original.Id || String.Equals(candidateName, original.Name, StringComparison.Ordinal))
                                matches.Add(ProbeFeature(candidate));
                        }
                        catch (Exception ex) { if (errors.Count < 32) errors.Add(DiagnosticError(ex)); }
                    }
                    row["feature_manager_get_features_false_count"] = managerFeatures.Length;
                    row["feature_manager_get_features_false_matches_id_or_name_ONLY"] = matches.ToArray();
                    row["feature_manager_candidate_errors"] = errors.ToArray();
                }
                rows.Add(row);
            }
            return rows.ToArray();
        }
        // Private diagnostic API. It records only primitive tree rows, with
        // no live RCW, path, Body or persistent-reference token payload.
        // This does not change ownership or permit deletion of a missing node.
        internal void WritePrivateSnapshotDiff(string privateFilePath)
        {
            Need(Path.IsPathRooted(privateFilePath) &&
                privateFilePath.EndsWith(".private.json", StringComparison.OrdinalIgnoreCase),
                "Snapshot diff requires an explicit absolute .private.json target");
            Func<Node, object> dto = node => new Dictionary<string, object> {
                {"id", node.Id}, {"type", node.Type}, {"kind", node.Kind}, {"name", node.Name},
                {"parent_path", node.ParentPath}, {"order", node.Order}, {"suppressed", node.Suppressed},
                {"baseline_holder_probe", node.PrivateBaselineHolderProbe}
            };
            var rows = Scopes.Select(scope => {
                var now = Read(scope);
                return new Dictionary<string, object> {
                    {"document_label", scope.Label},
                    {"baseline", scope.Baseline.Order.Select(id => dto(scope.Baseline.Nodes[id])).ToArray()},
                    {"current", now.Order.Select(id => dto(now.Nodes[id])).ToArray()},
                    {"missing_original_ids", scope.Baseline.Order.Where(id => !now.Nodes.ContainsKey(id)).ToArray()},
                    {"missing_original_diagnostics", MissingOriginalDiagnostics(scope, now)},
                    {"original_feature_reference_signatures", ProbeOriginalReferenceSignatures(scope, now)},
                    {"new_ids", now.Order.Where(id => !scope.Baseline.Nodes.ContainsKey(id)).ToArray()},
                    {"changed_original_ids", scope.Baseline.Order.Where(id => now.Nodes.ContainsKey(id) &&
                        !RowSame(scope.Baseline.Nodes[id], now.Nodes[id], true)).ToArray()}
                };
            }).ToArray();
            var serializer = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
            File.WriteAllText(privateFilePath, serializer.Serialize(new Dictionary<string, object> {
                {"scope", "PRIVATE_NATIVE_SCOPE_DIFF_NOT_OWNERSHIP_AUTHORITY"},
                {"diagnostic_version", "4SAFE_NO_RETAINED_BASELINE_RCW_QUERY_NO_DIAGNOSTIC_FEATURE_ISSAME"}, {"utc", DateTime.UtcNow.ToString("o")},
                {"trees", rows}, {"read_diagnostics", PrivateReadDiagnostics.ToArray()}
            }));
        }
        private string PublicError(Exception error)
        {
            string text = "HRESULT=0x" + error.HResult.ToString("X8") + " " + error;
            foreach (Scope scope in Scopes)
            {
                if (!String.IsNullOrEmpty(scope.PrivatePath))
                    text = text.Replace(scope.PrivatePath, "<" + scope.Label + "_DOCUMENT>");
                string folder = Path.GetDirectoryName(scope.PrivatePath);
                if (!String.IsNullOrEmpty(folder)) text = text.Replace(folder, "<BOUND_PRIVATE_FOLDER>");
            }
            return text;
        }
        private Entry EntryFor(Scope scope, int id)
        { return Entries.SingleOrDefault(e => e.Scope == scope && e.Id == id && !e.Removed); }
        private Feature Resolve(Entry entry, Snapshot snapshot)
        {
            Node live; if (!snapshot.Nodes.TryGetValue(entry.Id, out live)) return null;
            Need(!entry.Scope.Baseline.Nodes.ContainsKey(entry.Id), "Owned ID now belongs to protected baseline");
            Need(live.Kind == entry.Kind, "Live owned Feature kind differs");
            Need(entry.ClosedIdentityConfirmed && entry.PrivateToken != null, "No confirmed closed native token; refusing guessed ID deletion");
            int state; var resolved = entry.Scope.Document.Extension.GetObjectByPersistReference3(entry.PrivateToken, out state) as Feature;
            Need(state == 0 && resolved != null && resolved.GetID() == entry.Id && Same(resolved, live.Feature), "Cannot resolve exact owned Feature identity");
            return resolved;
        }
        private bool MapsTo(Feature candidate, Feature target)
        { return candidate != null && target != null && Same(candidate, target); }
        private Feature DefinitionFeature(object entity)
        {
            var feature = entity as Feature; if (feature != null) return feature;
            var segment = entity as SketchSegment;
            if (segment != null) return segment.GetSketch() as Feature;
            var point = entity as SketchPoint;
            if (point != null) return point.GetSketch() as Feature;
            var face = entity as Face2;
            return face == null ? null : face.GetFeature() as Feature;
        }
        private List<DependentInfo> Dependents(Entry deleting, Feature target, Dictionary<Scope, Snapshot> current)
        {
            var found = new List<DependentInfo>();
            Action<Scope, Node, string> add = (scope, node, reason) =>
            {
                if (scope == deleting.Scope && node.Id == deleting.Id) return;
                if (found.Any(r => r.Document == scope.Label && r.Id == node.Id && r.Evidence == reason)) return;
                found.Add(new DependentInfo { Document = scope.Label, Id = node.Id, Kind = node.Kind,
                    Name = node.Name, Owned = EntryFor(scope, node.Id) != null,
                    Protected = scope.Baseline.Nodes.ContainsKey(node.Id), Evidence = reason });
            };
            Array children = target.GetChildren() as Array;
            Need(children == null || children.Length <= MaxParents, "Child enumeration bound exceeded");
            if (children != null) foreach (var child in children.Cast<object>().OfType<Feature>())
            {
                bool mapped = false;
                foreach (var pair in current) foreach (Node node in pair.Value.Nodes.Values)
                    if (MapsTo(child, node.Feature)) { add(pair.Key, node, "NATIVE_CHILD"); mapped = true; }
                Need(mapped, "Native child outside declared scopes or unresolved context identity");
            }
            foreach (var pair in current) foreach (Node node in pair.Value.Nodes.Values)
            {
                if (pair.Key == deleting.Scope && node.Id == deleting.Id) continue;
                Array parents = node.Feature.GetParents() as Array;
                Need(parents == null || parents.Length <= MaxParents, "Parent enumeration bound exceeded");
                if (parents != null && parents.Cast<object>().OfType<Feature>().Any(p => MapsTo(p, target)))
                    add(pair.Key, node, "DIRECT_PARENT");
                if (node.Type == "ProfileFeature" || node.Type == "3DProfileFeature")
                {
                    var sketch = node.Feature.GetSpecificFeature2() as Sketch;
                    Need(sketch != null, "Cannot inspect dependent Sketch");
                    Array relations = sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll) as Array;
                    Need(relations == null || relations.Length <= MaxRelations, "Relation enumeration bound exceeded");
                    if (relations != null) foreach (var relation in relations.Cast<object>().OfType<SketchRelation>())
                    {
                        Array entities = relation.GetDefinitionEntities2() as Array;
                        Need(entities == null || entities.Length <= 32, "Definition entity bound exceeded");
                        if (entities != null && entities.Cast<object>().Select(DefinitionFeature).Any(p => MapsTo(p, target)))
                            add(pair.Key, node, "SKETCH_RELATION_DEFINITION_ENTITY");
                    }
                }
                // External names are a conservative guard in addition to actual
                // parent/relation identities, never ownership authority.
                if (node.Feature.ListExternalFileReferencesCount() > 0)
                {
                    object paths, components, features, types, status, entities, featureComponents;
                    int configOption; string configName;
                    node.Feature.ListExternalFileReferences2(out paths, out components, out features,
                        out types, out status, out entities, out featureComponents, out configOption, out configName);
                    Array modelPaths = paths as Array, refs = entities as Array;
                    if (modelPaths != null)
                    {
                        Need(modelPaths.Length <= MaxParents, "External reference enumeration bound exceeded");
                        for (int i = 0; i < modelPaths.Length; i++)
                        {
                            string path = Convert.ToString(modelPaths.GetValue(i));
                            if (!String.Equals(path, deleting.Scope.PrivatePath, StringComparison.OrdinalIgnoreCase)) continue;
                            string description = refs != null && i < refs.Length ? Convert.ToString(refs.GetValue(i)) : "";
                            if (String.IsNullOrWhiteSpace(description))
                                add(pair.Key, node, "EXTERNAL_DOCUMENT_REFERENCE_TARGET_UNRESOLVED");
                            else if (!String.IsNullOrEmpty(target.Name) && description.IndexOf(target.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                                add(pair.Key, node, "EXTERNAL_REFERENCE_NAME_MATCH_REQUIRES_NATIVE_IDENTITY");
                        }
                    }
                }
            }
            return found;
        }
        private void Activate(ModelDoc2 document)
        {
            RequireAssemblyContext();
            if (Same(App.ActiveDoc, document)) return;
            int error = 0; var active = App.ActivateDoc3(document.GetTitle(), false,
                (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref error) as ModelDoc2;
            Need(Same(active, document) && (error & ~(int)swActivateDocError_e.swDocNeedsRebuildWarning) == 0, "Cannot activate exact rollback document");
        }
        internal RollbackReport Rollback()
        {
            var report = new RollbackReport(); RollingBack = true;
            try
            {
                RequireAssemblyContext();
                Need(Scopes.All(s => s.Document.GetActiveSketch2() == null), "Caller must close owned Sketch before rollback");
                Mark("ROLLBACK_BEGIN");
                // All Cuts before all other Features. Within each phase, reverse
                // the actual cross-document registration/creation sequence.
                var order = Entries.OrderBy(e => e.ContextMetadata ? 3 : e.SheetMetalCompanion ? 2 : e.Kind == "Cut" ? 0 : 1).ThenByDescending(e => e.Sequence).ToArray();
                Need(order.Length <= 256, "Owned feature journal bound exceeded");
                foreach (Entry entry in order)
                {
                    var before = Scopes.ToDictionary(s => s, Read);
                    foreach (var pair in before) CheckProtected(pair.Key, pair.Value, false);
                    if(entry.SheetMetalCompanion)
                    {
                        // It may disappear automatically when an owned Cut is
                        // deleted with option0. Never select/delete this metadata
                        // explicitly: original dependents are outside our authority.
                        Need(!before[entry.Scope].Nodes.ContainsKey(entry.Id) && entry.Removed,
                            "Passive automatic-only SheetMetal companion survives owned Cut/Sketch rollback; id="+entry.Id+
                            "; observed_children="+entry.CompanionObservedChildFingerprint+"; explicit deletion is forbidden");
                        continue;
                    }
                    Feature target = entry.ContextMetadata ? ResolveContextMetadata(entry,before[entry.Scope],before) : Resolve(entry, before[entry.Scope]);
                    if (target == null)
                    {
                        Need(entry.Removed, "Owned Feature disappeared without a recorded owned deletion");
                        continue;
                    }
                    var dependents = Dependents(entry, target, before);
                    if (dependents.Count != 0)
                    {
                        report.BlockingDependents.AddRange(dependents);
                        Need(false, "Live owned/protected/undeclared dependents remain before deletion; no cascade assumed or permitted");
                    }
                    Activate(entry.Scope.Document);
                    Mark("DELETE_BEGIN_" + entry.Scope.Label + "_" + entry.Id);
                    entry.Scope.Document.ClearSelection2(true);
                    Need(target.Select2(false, 0), "Cannot select exact owned Feature");
                    Need(entry.Scope.Document.Extension.DeleteSelection2(0), "Native option0 owned deletion returned false");
                    var after = Scopes.ToDictionary(s => s, Read);
                    var disappearedHere = new List<int>();
                    foreach (Scope scope in Scopes)
                    {
                        CheckProtected(scope, after[scope], false);
                        foreach (int id in before[scope].Nodes.Keys.Where(id => !after[scope].Nodes.ContainsKey(id)))
                        {
                            Entry removed = EntryFor(scope, id);
                            Need(removed != null, "Unregistered/protected Feature disappeared during option0 deletion");
                            removed.Removed = true;
                            if (scope == entry.Scope) disappearedHere.Add(id);
                        }
                        Need(after[scope].Nodes.Keys.All(id => before[scope].Nodes.ContainsKey(id)), "Native deletion created an unexplained Feature");
                    }
                    Need(entry.Removed, "Owned deletion made no progress");
                    report.Deletions.Add(new DeletionInfo { Document = entry.Scope.Label, Id = entry.Id,
                        Kind = entry.Kind, Name = entry.Name, Options = 0, Status = "REMOVED_REGISTERED_OWNED_ONLY",
                        DisappearedOwnedIds = disappearedHere.ToArray() });
                    Mark("DELETE_END_" + entry.Scope.Label + "_" + entry.Id);
                }
                foreach (Scope scope in Scopes) CheckProtected(scope, Read(scope), true);
                report.ZeroOwnedFeaturesRemain = Entries.All(e => e.Removed);
                Need(report.ZeroOwnedFeaturesRemain, "Registered owned Feature remains");
                report.BodyDifferenceA = AfjNativeCutCommit.SymmetricDifference(
                    AfjTemporaryBoolean.Copy(AfjNativeCutCommit.CurrentBody(Scopes[0].Document)), Scopes[0].Before);
                report.BodyDifferenceB = AfjNativeCutCommit.SymmetricDifference(
                    AfjTemporaryBoolean.Copy(AfjNativeCutCommit.CurrentBody(Scopes[1].Document)), Scopes[1].Before);
                Need(AfjTemporaryBoolean.Finite(report.BodyDifferenceA) && AfjTemporaryBoolean.Finite(report.BodyDifferenceB) &&
                    report.BodyDifferenceA <= report.BodyTolerance && report.BodyDifferenceB <= report.BodyTolerance,
                    "Initial Body B-rep not restored within explicit tolerance");
                report.OriginalTreesAndSuppressionRestored = true;
                report.ProtectedFeaturesPreserved = true;
                Activate(Assembly); RequireAssemblyContext(); report.AssemblyContextRestored = true;
                report.Verified = true; report.Status = "PASS_NATIVE_OWNED_FLAT_IDENTITIES_TOP_ORDER_SUPPRESSION_BREP_RESTORED";
            }
            catch (Exception ex)
            {
                report.Status = "ROLLBACK_UNVERIFIED";
                report.Error = PublicError(ex);
            }
            finally
            {
                try { Activate(Assembly); RequireAssemblyContext(); report.AssemblyContextRestored = true; }
                catch (Exception ex) { report.Verified = false; report.Status = "ROLLBACK_UNVERIFIED"; report.Error += " CONTEXT_RESTORE " + PublicError(ex); }
                try { report.DirtyA = Scopes[0].Document.GetSaveFlag(); report.DirtyB = Scopes[1].Document.GetSaveFlag(); report.DirtyAssembly = Assembly.GetSaveFlag(); }
                catch (Exception ex) { report.Error += " DIRTY_OBSERVATION " + PublicError(ex); }
                report.Owned.AddRange(Entries.Select(Public)); Mark("ROLLBACK_END_" + report.Status);
            }
            return report;
        }
    }
}
