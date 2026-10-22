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
    /// Temporary original-body overlay and parametric profile cut preview.
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
        /// Parametric section rules with the shorter Frame covering. Mode 1 shows KEEP bodies;
        /// mode 2 also shows CUT material. Both modes use the SAME rule.
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
            try
            {
                if (!IsOriginalAssemblyActive() || frameA == null || frameB == null ||
                    app.IsSame(frameA, frameB) != (int)swObjectEquality.swObjectNotSame)
                { message = "Assembly/Frame A-B không còn hợp lệ."; return false; }
                if (frameA.GetParent() != null || frameB.GetParent() != null ||
                    frameA.IsSuppressed() || frameB.IsSuppressed() ||
                    frameA.IsHidden(false) || frameB.IsHidden(false))
                { message = "Chỉ hỗ trợ 2 Part cấp đầu, không Hidden/Suppressed."; return false; }

                var phase = Stopwatch.StartNew();
                PreviewSourceState before = TryCaptureSourceState(frameA, frameB);
                reused = cachedSolution != null && before != null && SameSourceState(cachedSource, before);
                Trace.WriteLine("[AFJ-R52-PERF] phase=source_state ms=" + phase.ElapsedMilliseconds + " cache=" + (reused ? "HIT" : "MISS"));
                ThrowIfClosing();
                if (!IsOriginalAssemblyActive())
                { InvalidateCachedSolution("assembly no longer active"); message = "Assembly đã thay đổi; không cập nhật Preview."; return false; }
                if (reused && displayedCandidate == candidate)
                {
                    message = displayedMessage;
                    Trace.WriteLine("[AFJ-R52-PERF] phase=display mode=UNCHANGED no_solve=True no_hide=True no_display3=True");
                    return true;
                }

                AfjJointSolution result = reused ? cachedSolution : null;
                PreviewSourceState after = before;
                if (!reused)
                {
                    InvalidateCachedSolution("source changed or unverified");
                    Trace.WriteLine("[AFJ-R51] source=profile-skin-cutter; MVID=" + typeof(AutoFrameJointGhostPreview).Assembly.ManifestModule.ModuleVersionId);
                    Trace.WriteLine("[AFJ-SOLVER] PROFILE -> RULE -> PLAN A/B -> BOOLEAN -> VALIDATION");
                    Trace.WriteLine("[AFJ-R52] stage=SOLVER_BEGIN");
                    phase.Restart();
                    result = AfjJointSolver.Solve(app, sourceAssembly, frameA, frameB);
                    Trace.WriteLine("[AFJ-R52-PERF] phase=solve ms=" + phase.ElapsedMilliseconds);
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
                    displayedMessage = message;
                }
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
                            ? "UNVERIFIED_RULE: Chưa xác minh được quy tắc cho biên dạng, mặt che hoặc phần phôi của cặp Frame này. Xem log AFJ-SOLVER."
                            : "Không tạo được Preview hình học. Xem log AFJ-SOLVER.";
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
            message = "PREVIEW_REQUIRED: hãy tạo Preview trước khi OK.";
            if (busy || closeRequested || cachedSolution == null || cachedSource == null ||
                frameA == null || frameB == null || !IsOriginalAssemblyActive()) return false;
            if (frameA.GetParent()!=null || frameB.GetParent()!=null || frameA.IsSuppressed() || frameB.IsSuppressed() ||
                frameA.IsHidden(false) || frameB.IsHidden(false))
            { message="APPLY: cần hai Part cấp đầu đang hiển thị và resolved."; return false; }
            PreviewSourceState state = cachedSource;
            AfjJointSolution result = cachedSolution;
            if (!SameSourceState(state, TryCaptureSourceState(frameA, frameB)))
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

        internal void RequestClose()
        {
            closeRequested = true; // Managed latch, including reentrant close during a native operation.
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
            int web=lines.IndexOf(AfjParametricRules.WebLine(result));
            var depth=Enumerable.Repeat(99, lines.Count).ToArray();
            depth[web]=0;
            for(int pass=0;pass<lines.Count;pass++)for(int i=0;i<lines.Count;i++)for(int j=0;j<lines.Count;j++)if(Adjacent(lines[i], lines[j]))depth[j]=Math.Min(depth[j], depth[i]+1);
            result.Regions=depth.Select(d=>d==0?"WEB":d==1?"WALL":"RETURN_LIP_OR_STEP").ToArray();
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
        internal AfjBooleanResult A, B;
        internal ModelDoc2 PartA, PartB;
        internal MathTransform TransformA, TransformB;
        internal double BeforeOverlap, AfterOverlap, AfterGap, LengthA, LengthB;
        internal bool CoverA;
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
                Plan=generated.Plan, PartA=pa, PartB=pb, TransformA=ta, TransformB=tb, LengthA=la, LengthB=lb, CoverA=coverA
            }
            ;
            result.A=AfjTemporaryBoolean.Execute(sw, a, generated.Plan.A);
            result.B=AfjTemporaryBoolean.Execute(sw, b, generated.Plan.B);
            if(result.A.Kept.Count!=1||result.B.Kept.Count!=1)throw new InvalidOperationException("UNVERIFIED_RULE: each Frame must remain one connected solid");
            SelectedEnd(result.A, ta, generated.JointOrigin, coverA?generated.OutWinner:generated.OutLoser);
            SelectedEnd(result.B, tb, generated.JointOrigin, coverA?generated.OutLoser:generated.OutWinner);
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
        internal string Derivation;
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
        internal static double[] WebLine(AfjProfile profile)
        {
            var candidates=new List<double[]>();
            foreach(double[] line in profile.Lines)
            {
                double[] a=P(line, 0), b=P(line, 1), u=Unit(Sub(b, a));
                var first=profile.Lines.Where(x=>x!=line&&OtherEnd(x, a)!=null).ToArray();
                var last=profile.Lines.Where(x=>x!=line&&OtherEnd(x, b)!=null).ToArray();
                if(first.Length!=1||last.Length!=1)continue;
                double[] v=Unit(Sub(OtherEnd(first[0], a), a)), w=Unit(Sub(OtherEnd(last[0], b), b));
                if(Math.Abs(Dot(u, v))<1e-7&&Dot(v, w)>.999999)candidates.Add(line);
            }
            if(candidates.Count!=1)throw new InvalidOperationException("UNVERIFIED_RULE: no unique Web between parallel outward Walls");
            return candidates[0];
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
                    if(path.Count>2)throw new InvalidOperationException("UNVERIFIED_RULE: multi-step/double Lip needs separate coverage rule");
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
                if(side==0&&y>1e-7||side==1&&y<s.Width-1e-7)throw new InvalidOperationException("UNVERIFIED_RULE: inward Return needs collision/coverage rule");
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
        static double[] Outgoing(AfjProfile p, Body2 body, MathTransform t, AfjProfile other, MathTransform ot)
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
        static double[] Native(double[] p, MathTransform transform)
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
        static double Extreme(Body2 body, MathTransform t, double[] worldDirection, double[] joint)
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
            Trace.WriteLine("[AFJ-PROFILE] API/BREP skins verified: Web 0/t, Wall 0/t/W-t/W, four base Bend radius/center matches; t=" + t.ToString("R", CultureInfo.InvariantCulture) + " R=" + r.ToString("R", CultureInfo.InvariantCulture));
        }

        internal static AfjParametricPlan Generate(AfjProfile profileA, AfjProfile profileB, Body2 bodyA, Body2 bodyB, MathTransform ta, MathTransform tb, bool aCovers, double webGap)
        {
            // The cover side is a manufacturing policy. It is not inferred from filenames,
            // fixture coordinates, stock length, A/B selection order or a universal KCC tag.
            AfjProfile pl=aCovers?profileB:profileA, pw=aCovers?profileA:profileB;
            Body2 bl=aCovers?bodyB:bodyA, bw=aCovers?bodyA:bodyB;
            MathTransform tl=aCovers?tb:ta, tw=aCovers?ta:tb;
            double[] ul=Outgoing(pl, bl, tl, pw, tw), uw=Outgoing(pw, bw, tw, pl, tl);
            if(Math.Abs(Dot(ul, uw))>1e-7)throw new InvalidOperationException("UNVERIFIED_RULE: only orthogonal axes have native evidence");
            AfjUSection l=Section(pl, tl, uw), w=Section(pw, tw, ul);
            if(Math.Abs(l.Thickness-w.Thickness)>1e-8||Math.Abs(l.Radius-w.Radius)>1e-8)throw new InvalidOperationException("UNVERIFIED_RULE: unequal thickness/Bend R pair needs additional native evidence");
            if(Dot(l.WidthDirection, uw)<.999999||Dot(w.WidthDirection, ul)<.999999||Dot(l.DepthDirection, w.DepthDirection)<.999999)throw new InvalidOperationException("UNVERIFIED_RULE: incompatible folded skin orientation");
            VerifyPhysicalSkins(l, bl, tl);
            VerifyPhysicalSkins(w, bw, tw);
            double[] d=Unit(l.DepthDirection), o=Add(l.Origin, Scale(ul, Dot(Sub(w.Origin, l.Origin), ul)));
            if(Math.Abs(Dot(Sub(w.Origin, o), d))>1e-7)throw new InvalidOperationException("UNVERIFIED_RULE: Web skins are not coplanar");
            if(l.NearReturn||w.NearReturn)throw new InvalidOperationException("UNVERIFIED_RULE: near/inward Return needs a separate coverage collision rule");
            double outerDepthL=l.FarHeight+(l.FarReturn?l.Thickness:0), outerDepthW=w.FarHeight+(w.FarReturn?w.Thickness:0);
            bool unequalHeights=Math.Abs(l.NearHeight-w.NearHeight)>1e-7||Math.Abs(outerDepthL-outerDepthW)>1e-7;
            if (unequalHeights)
            {
                // Extend the existing cover policy only where the covering
                // plain-U Walls reach all losing-wall cut edges. Return levels
                // and a lower covering Wall need a separate verified rule.
                if (l.FarReturn || w.FarReturn || w.NearHeight + 1e-7 < l.NearHeight ||
                    outerDepthW + 1e-7 < outerDepthL)
                    throw new InvalidOperationException("UNVERIFIED_RULE: actual covering Wall is too low or unequal Return levels lack evidence");
            }
            double sf=w.FarTangent, qf=l.FarTangent, sn=w.NearTangent, qn=qf-(sf-sn);
            if(qn<=l.NearTangent+1e-8)qn=l.Thickness;
            // Constrain the seam to the inner near Wall when the ideal bisector enters its Bend zone.
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
                , Derivation="Common seam from Bend tangencies; near seam constrained to inner Wall; Wall assembly gap=0.1 mm; outer Return coverage; analytic NormalCut cap; web gap="+webGap.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
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
        private sealed class PartEdit
        {
            internal ModelDoc2 Part;
            internal Body2 Before;
            internal string[] Tree, AfterTree;
            internal int AfterStamp;
            internal bool SheetMetal, Recording, GroupFinished, Started;
            internal string Prefix;
            internal readonly HashSet<int> OwnedIds = new HashSet<int>();
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
        private static Feature WriteTool(ISldWorks app, PartEdit edit, AfjToolDefinition spec,
                                         bool normalCut, string suffix, out bool reverse)
        {
            ModelDoc2 part = edit.Part;
            double[] n = AfjParametricRules.Unit(spec.Direction);
            double[] p = spec.Polygon[0];
            double[] u = AfjParametricRules.Unit(AfjParametricRules.Sub(spec.Polygon[1], p));
            double[] v = AfjParametricRules.Unit(Cross(n, u));
            part.ClearSelection2(true);
            // Fixed datum is deliberate: this plan is a reviewed geometry snapshot.
            // Direction is still read back from the native sketch's transform below.
            object plane = part.CreatePlaneFixed2(p,
                AfjParametricRules.Add(p, AfjParametricRules.Scale(u, .01)),
                AfjParametricRules.Add(p, AfjParametricRules.Scale(v, .01)), false);
            if (plane == null) throw new InvalidOperationException("APPLY: fixed cutter plane unavailable");
            Feature planeFeature = Own(edit, FindSpecific(app, part, plane), suffix + "_Plane");
            part.ClearSelection2(true);
            if (!planeFeature.Select2(false, 0)) throw new InvalidOperationException("APPLY: cannot select cutter plane");
            Sketch sketch = null;
            Feature profile = null;
            bool oldAddToDb = false, changedAddToDb = false;
            try
            {
                part.SketchManager.InsertSketch(true);
                sketch = part.GetActiveSketch2() as Sketch;
                if (sketch == null || sketch.Is3D()) throw new InvalidOperationException("APPLY: expected an active 2D sketch");
                profile = Own(edit, FindSpecific(app, part, sketch), suffix + "_Profile");
                oldAddToDb = part.SketchManager.AddToDB;
                changedAddToDb = true;
                part.SketchManager.AddToDB = true;
                MathTransform transform = sketch.ModelToSketchTransform;
                MathTransform inverse = transform.Inverse() as MathTransform;
                if (inverse == null) throw new InvalidOperationException("APPLY: native sketch transform unavailable");
                double[] o = Point(app, new double[3], inverse);
                double[] z = AfjParametricRules.Unit(AfjParametricRules.Sub(Point(app, new[] {0d,0d,1d}, inverse), o));
                double alignment = AfjParametricRules.Dot(z, n);
                reverse = ReverseForAlignment(alignment);
                double[][] points = spec.Polygon.Select(x => Point(app, x, transform)).ToArray();
                for (int i = 0; i < points.Length; i++)
                {
                    double[] a = points[i], b = points[(i+1)%points.Length];
                    if (Math.Abs(a[2]) > 1e-8 || Math.Abs(b[2]) > 1e-8)
                        throw new InvalidOperationException("APPLY: polygon is not on native sketch plane");
                    SketchSegment segment;
                    if (spec.ArcCenters != null && spec.ArcCenters[i] != null)
                    {
                        double[] c = Point(app, spec.ArcCenters[i], transform);
                        double cross = (a[0]-c[0])*(b[1]-c[1])-(a[1]-c[1])*(b[0]-c[0]);
                        if (Math.Abs(cross) < 1e-14 || Math.Abs(c[2]) > 1e-8)
                            throw new InvalidOperationException("APPLY: unsupported arc");
                        segment = part.SketchManager.CreateArc(c[0],c[1],0,a[0],a[1],0,b[0],b[1],0,(short)(cross>0?1:-1));
                    }
                    else segment = part.SketchManager.CreateLine(a[0],a[1],0,b[0],b[1],0);
                    if (segment == null) throw new InvalidOperationException("APPLY: sketch entity returned null");
                }
            }
            finally
            {
                try { if (changedAddToDb) part.SketchManager.AddToDB = oldAddToDb; }
                finally { if (sketch != null) EndSketch(app, part, sketch); }
            }
            part.ClearSelection2(true);
            if (!profile.Select2(false, 0)) throw new InvalidOperationException("APPLY: cannot select native cut profile");
            Feature cut = part.FeatureManager.FeatureCut4(true, false, reverse,
                (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind,
                spec.Length, spec.Length, false,false,false,false,0,0,false,false,false,false,
                normalCut,true,true,false,false,false,(int)swStartConditions_e.swStartSketchPlane,0,false,normalCut);
            cut = Own(edit, cut, suffix + "_Cut");
            bool warning;
            int error = cut.GetErrorCode2(out warning);
            string featureType=cut.GetTypeName2();
            string underlyingType=featureType=="ICE"?cut.GetTypeName():featureType;
            Trace.WriteLine("[AFJ-APPLY] NATIVE_FEATURE type="+featureType+" underlying="+underlyingType+" error="+error);
            if (error != 0 || underlyingType != "Cut")
                throw new InvalidOperationException("APPLY: native Cut feature error=" + error+" type="+featureType+" underlying="+underlyingType);
            return cut;
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
                    bool normal = UseNativeNormalCut(operation,tool);
                    bool reverse;
                    Feature feature = WriteTool(app, edit, tool, normal, "_" + (++index).ToString("00", CultureInfo.InvariantCulture), out reverse);
                    Body2 current = CurrentBody(edit.Part);
                    if (current.IsSheetMetal() != edit.SheetMetal) throw new InvalidOperationException("APPLY: sheet metal body identity lost");
                    double delta = VerifyBody(current, target.Kept.Single());
                    var data = feature.GetDefinition() as ExtrudeFeatureData2;
                    if (data == null || data.GetEndCondition(true) != (int)swEndConditions_e.swEndCondBlind ||
                        Math.Abs(data.GetDepth(true)-tool.Length)>1e-8 || data.NormalCut != normal || data.FlipSideToCut || data.ReverseDirection != reverse)
                        throw new InvalidOperationException("APPLY: native Cut definition differs from plan");
                    var evidence = new AfjNativeCutEvidence { Frame=frame, EvidenceId=operation.EvidenceId,
                        FeatureName=feature.Name, FeatureType=feature.GetTypeName2(), UnderlyingType=feature.GetTypeName2()=="ICE"?feature.GetTypeName():feature.GetTypeName2(), DepthPolicy=tool.DepthPolicy,
                        EndCondition=data.GetEndCondition(true), DepthMetres=data.GetDepth(true), NormalCut=data.NormalCut,
                        FlipSideToCut=data.FlipSideToCut, ReverseDirection=data.ReverseDirection,
                        RemovedCubicMetres=target.RemovedVolume, SymmetricDifferenceCubicMetres=delta };
                    report.Cuts.Add(evidence);
                    Trace.WriteLine("[AFJ-APPLY] CUT_VERIFIED frame=" + frame + " id=" + operation.EvidenceId + " feature=" + feature.Name + " delta_m3=" + delta.ToString("R",CultureInfo.InvariantCulture));
                }
                VerifyBody(CurrentBody(edit.Part), expected);
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
            message = "Chưa áp dụng Cut.";
            PartEdit a = null, b = null;
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
                if (result.Plan==null || (result.Plan.RuleId!="ORTHOGONAL_U_COVER" && result.Plan.RuleId!="ORTHOGONAL_U_TALLER_COVER"))
                    throw new InvalidOperationException("UNVERIFIED_RULE: native Cut writer has no qualified rule mapping");
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
                a = new PartEdit { Part=result.PartA, Before=ticket.BeforeA, Tree=Tree(result.PartA), SheetMetal=CurrentBody(result.PartA).IsSheetMetal(), Prefix=run+"_A" };
                b = new PartEdit { Part=result.PartB, Before=ticket.BeforeB, Tree=Tree(result.PartB), SheetMetal=CurrentBody(result.PartB).IsSheetMetal(), Prefix=run+"_B" };
                if (!ticket.IsSourceCurrent()) throw new InvalidOperationException("PREVIEW_STALE: source changed before mutation");
                ApplyPart(app, a, result.Plan.A, result.A.Kept.Single(), "A", report);
                ApplyPart(app, b, result.Plan.B, result.B.Kept.Single(), "B", report);
                Activate(app, assembly);
                double[] actualA = ticket.FrameA.GetTotalTransform(false).ArrayData as double[];
                double[] actualB = ticket.FrameB.GetTotalTransform(false).ArrayData as double[];
                double[] oldA = result.TransformA.ArrayData as double[], oldB = result.TransformB.ArrayData as double[];
                if (actualA==null || actualB==null || actualA.Length!=16 || actualB.Length!=16 ||
                    actualA.Where((x,i)=>!AfjTemporaryBoolean.Finite(x)||Math.Abs(x-oldA[i])>1e-10).Any() ||
                    actualB.Where((x,i)=>!AfjTemporaryBoolean.Finite(x)||Math.Abs(x-oldB[i])>1e-10).Any())
                    throw new InvalidOperationException("APPLY: component placement changed from reviewed Preview");
                VerifyBody(CurrentBody(a.Part),result.A.Kept.Single());
                VerifyBody(CurrentBody(b.Part),result.B.Kept.Single());
                var wa = AfjJointSolver.World(new[]{AfjTemporaryBoolean.Copy(CurrentBody(a.Part))}, result.TransformA);
                var wb = AfjJointSolver.World(new[]{AfjTemporaryBoolean.Copy(CurrentBody(b.Part))}, result.TransformB);
                report.InterferenceCubicMetres = AfjJointSolver.Overlap(wa, wb);
                report.GapMetres = AfjJointSolver.Gap(assembly, wa, wb);
                if (report.InterferenceCubicMetres>1e-12 || Math.Abs(report.GapMetres-AfjManufacturingConventions.AssemblyFitGap)>1e-6)
                    throw new InvalidOperationException("APPLY: native pair failed overlap/fit-gap validation");
                report.Status = "APPLY_VERIFIED";
                message = "Đã tạo " + report.Cuts.Count + " Cut-Extrude trên hai Frame, khớp Preview. Hãy kiểm tra và lưu Part khi muốn giữ thay đổi.";
                return true;
            }
            catch (Exception ex)
            {
                report.Error = "HRESULT=0x" + ex.HResult.ToString("X8") + " " + ex;
                Trace.WriteLine("[AFJ-APPLY] " + report.Error);
                bool rb = b == null || Rollback(app,b,report);
                bool ra = a == null || Rollback(app,a,report);
                report.RollbackVerified = rb && ra;
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
