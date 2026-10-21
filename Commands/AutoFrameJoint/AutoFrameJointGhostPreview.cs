using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.AutoFrameJoint
{
    /// <summary>
    /// Read-only visual overlay of EXISTING Frame geometry.
    /// No miter/cut computation occurs here. Call only on the SOLIDWORKS UI
    /// thread from a future explicitly approved PropertyManager button.
    ///
    /// Intentionally NOT connected to AutoFrameJointCommandEntry yet.
    /// The native A/B selector must pass its host smoke test first.
    /// </summary>
    public sealed class AutoFrameJointGhostPreview
    {
        private const int MaxBodiesPerComponent = 12;

        private readonly ISldWorks app;
        private readonly ModelDoc2 sourceAssembly;
        private readonly List<ShownBody> shown = new List<ShownBody>();
        private bool busy;

        private sealed class ShownBody
        {
            internal readonly Body2 Copy;
            internal readonly ModelDoc2 Part;

            internal ShownBody(Body2 copy, ModelDoc2 part)
            {
                Copy = copy;
                Part = part;
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
            if (busy)
            {
                message = "Preview đang được xử lý.";
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
                int countB = ShowComponent(frameB, colorB, 0.65);
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
        /// PRELIMINARY TWO-FRAME SOLID CUT CANDIDATE (P1/P2).
        /// Computes exact overlap of the two ACTUAL transformed B-reps and
        /// allocates that overlap across one of two estimated bisector planes.
        /// Boolean CUT is performed ONLY on TEMPORARY COPIES of both bodies.
        ///
        /// Neither candidate is an approved manufacturing rule for Web/Wall/Lip.
        /// Both after-cut temporary copies are displaced +Z in WORLD space so
        /// the user can see their new faces beside untouched original parts.
        /// The original Component, Part and Assembly are never modified.
        /// </summary>
        public bool TryShowCutCandidate(Component2 frameA, Component2 frameB,
                                        int candidate, out string message)
        {
            message = "Chưa tạo preview vết cắt.";
            if (busy) { message = "Preview đang xử lý."; return false; }
            if (candidate != 1 && candidate != 2)
            { message = "Chỉ có phương án P1 hoặc P2."; return false; }

            busy = true;
            try
            {
                string previousError;
                if (!ClearInternal(out previousError))
                {
                    message = "Không thể dọn preview cũ: " + previousError;
                    return false;
                }
                if (!IsOriginalAssemblyActive() || frameA == null || frameB == null ||
                    app.IsSame(frameA, frameB) != (int)swObjectEquality.swObjectNotSame)
                {
                    message = "Assembly/Frame A-B không còn hợp lệ.";
                    return false;
                }
                if (frameA.GetParent() != null || frameB.GetParent() != null ||
                    frameA.IsSuppressed() || frameB.IsSuppressed() ||
                    frameA.IsHidden(false) || frameB.IsHidden(false))
                {
                    message = "Chỉ hỗ trợ 2 Part cấp đầu, không Hidden/Suppressed.";
                    return false;
                }

                // Re-evaluate at the USER'S explicit Preview click, not during
                // Selectionbox events. The analysis yields only provisional
                // bisector normals: neither is automatically a final trim rule.
                string analysis = AutoFrameJointReadOnlyProbe.AnalyzePair(
                    app, sourceAssembly, frameA, frameB);
                if (!analysis.Contains("JOINT_STATUS: FINITE_AXES_NEAREST") ||
                    !analysis.Contains("JOINT_NEAR_POINTS_INSIDE_SPANS: YES"))
                {
                    message = "Chưa xác nhận trục giao hữu hạn. Không dựng vết cắt giả.";
                    return false;
                }
                string normalLabel = "JOINT_BISECTOR_NORMAL_CANDIDATE_" +
                                     candidate.ToString() + ":";
                double[] origin = ReadVectorMm(analysis, "JOINT_XYZ_MM:");
                double[] normal = ReadVector(analysis, normalLabel, 1.0);
                if (origin == null || normal == null)
                {
                    message = "Thiếu điểm/normal dự đoán của mặt phân giác.";
                    return false;
                }

                ModelDoc2 partA, partB;
                MathTransform transformA, transformB;
                Body2 baseA = GetSingleWorldSolid(frameA, out partA, out transformA);
                Body2 baseB = GetSingleWorldSolid(frameB, out partB, out transformB);

                // The Operations2 operands are invalidated by SOLIDWORKS.
                // Always Boolean fresh temporary copies, never original B-rep.
                List<Body2> overlap = BooleanParts(
                    CopyBody(baseA), CopyBody(baseB),
                    (int)swBodyOperationType_e.SWBODYINTERSECT, "solid overlap");
                if (overlap.Count == 0)
                {
                    message = "Hai solid KHÔNG có thể tích giao thoa thực. Không có preview cắt.";
                    return false;
                }

                double overlapVolume = VolumeTotal(overlap);
                if (!(overlapVolume > 1e-12))
                {
                    message = "Thể tích giao thoa quá nhỏ hoặc không hợp lệ.";
                    return false;
                }
                double span = Math.Max(1.0, 4.0 * GetMaxBoxDiagonal(overlap));
                MathTransform shift = CreateWorldShift(0.12); // WORLD Z +120 mm
                MathTransform inverseA = transformA.Inverse() as MathTransform;
                MathTransform inverseB = transformB.Inverse() as MathTransform;
                if (inverseA == null || inverseB == null)
                    throw new InvalidOperationException("Không đảo được hệ tọa độ Part.");

                var removedA = new List<Body2>();
                var removedB = new List<Body2>();
                foreach (Body2 shared in overlap)
                {
                    Body2 boxPositive = CreateBisectorHalfBox(origin, normal, span, true);
                    Body2 boxNegative = CreateBisectorHalfBox(origin, normal, span, false);

                    // Candidate P2 uses the SECOND theoretical normal.
                    // The overlap is allocated to A on positive side and B on
                    // negative side; this is not a user-approved Web/Wall/Lip rule.
                    removedA.AddRange(BooleanParts(CopyBody(shared), boxPositive,
                        (int)swBodyOperationType_e.SWBODYINTERSECT, "A material"));
                    removedB.AddRange(BooleanParts(CopyBody(shared), boxNegative,
                        (int)swBodyOperationType_e.SWBODYINTERSECT, "B material"));
                }

                double removedVolumeA = VolumeTotal(removedA);
                double removedVolumeB = VolumeTotal(removedB);
                double minMeaningful = Math.Max(1e-12, overlapVolume * 0.00001);
                if (removedVolumeA <= minMeaningful ||
                    removedVolumeB <= minMeaningful ||
                    Math.Abs(removedVolumeA + removedVolumeB - overlapVolume) >
                        Math.Max(1e-11, overlapVolume * 0.01))
                {
                    message = "Mặt phân giác P" + candidate +
                        " không chia hợp lệ vùng giao thoa cho CẢ HAI Frame.";
                    return false;
                }

                // Generate ACTUAL candidate after-cut 3D solids from two real
                // B-reps. CUT operates only on disposable world-space copies.
                List<Body2> keptA = SubtractMaterial(baseA, removedA, "Frame A");
                List<Body2> keptB = SubtractMaterial(baseB, removedB, "Frame B");
                double keptVolumeA = VolumeTotal(keptA);
                double keptVolumeB = VolumeTotal(keptB);
                if (!(keptVolumeA > 0) || !(keptVolumeB > 0))
                {
                    message = "Kết quả sau cắt bị rỗng. Preview bị từ chối.";
                    return false;
                }

                // Candidate preview displaced in WORLD Z, not document edits.
                // Color-code the new temporary after-cut bodies and red/orange
                // removed pieces. Original Assembly stays exactly in place.
                DisplayCandidateBodies(keptA, frameA, partA, inverseA, shift,
                    ColorTranslator.ToWin32(Color.FromArgb(0, 180, 235)), 0.33);
                DisplayCandidateBodies(keptB, frameB, partB, inverseB, shift,
                    ColorTranslator.ToWin32(Color.FromArgb(255, 165, 65)), 0.33);
                DisplayCandidateBodies(removedA, frameA, partA, inverseA, shift,
                    ColorTranslator.ToWin32(Color.FromArgb(235, 35, 85)), 0.08);
                DisplayCandidateBodies(removedB, frameB, partB, inverseB, shift,
                    ColorTranslator.ToWin32(Color.FromArgb(170, 35, 235)), 0.08);
                RedrawSafely();

                message = String.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "P{0}: PREVIEW CẮT THỬ CẢ HAI FRAME trên B-rep thực. " +
                    "A bỏ {1:0.###} mm³; B bỏ {2:0.###} mm³. " +
                    "BẢN SAO sau cắt nâng Z +120 mm; hồng/tím là phần bỏ. " +
                    "CHƯA XÁC NHẬN quy tắc Web/Wall/Lip, không cắt Part.",
                    candidate, removedVolumeA * 1e9, removedVolumeB * 1e9);
                Trace.WriteLine("[AFJ-CUT-CANDIDATE] " + message);
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[AFJ-CUT-CANDIDATE] Failed: " + ex);
                string cleanupError;
                bool cleared = ClearInternal(out cleanupError);
                message = "Không tạo được candidate preview: " + ex.Message +
                          (cleared ? "" : " | CLEANUP PENDING: " + cleanupError);
                return false;
            }
            finally { busy = false; }
        }

        private static string ReportValue(string report, string prefix)
        {
            if (String.IsNullOrEmpty(report)) return "";
            foreach (string line in report.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
                    return trimmed.Substring(prefix.Length).Trim();
            }
            return "";
        }

        private static double[] ReadVectorMm(string report, string prefix)
        { return ReadVector(report, prefix, 0.001); }

        private static double[] ReadVector(string report, string prefix, double scale)
        {
            string raw = ReportValue(report, prefix).Trim('(', ')', ' ');
            string[] tokens = raw.Split(',');
            if (tokens.Length != 3) return null;
            double[] v = new double[3];
            for (int i = 0; i < 3; i++)
            {
                if (!Double.TryParse(tokens[i].Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out v[i]))
                    return null;
                v[i] *= scale;
                if (Double.IsNaN(v[i]) || Double.IsInfinity(v[i])) return null;
            }
            if (scale == 1.0)
            {
                double length = Math.Sqrt(v[0] * v[0] +
                                          v[1] * v[1] + v[2] * v[2]);
                if (length < 0.999 || length > 1.001) return null;
                for (int i = 0; i < 3; i++) v[i] /= length;
            }
            return v;
        }

        private static Body2 CopyBody(Body2 original)
        {
            Body2 copy = original == null ? null : original.Copy() as Body2;
            if (copy == null || !copy.IsTemporaryBody())
                throw new InvalidOperationException("Không Copy được B-rep tạm.");
            return copy;
        }

        private Body2 GetSingleWorldSolid(Component2 component,
                                           out ModelDoc2 part,
                                           out MathTransform transform)
        {
            part = component.GetModelDoc2() as ModelDoc2;
            if (part == null ||
                part.GetType() != (int)swDocumentTypes_e.swDocPART)
                throw new InvalidOperationException("Part không tải/không hợp lệ.");

            var cfgMgr = part.ConfigurationManager as ConfigurationManager;
            var cfg = cfgMgr == null ? null :
                      cfgMgr.ActiveConfiguration as Configuration;
            if (cfg == null ||
                !String.Equals(cfg.Name, component.ReferencedConfiguration,
                               StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Cấu hình Component/Part không khớp, không tự chuyển.");

            object info;
            Array raw = component.GetBodies3(
                (int)swBodyType_e.swSolidBody, out info) as Array;
            if (raw == null || raw.Length != 1)
                throw new InvalidOperationException(
                    "Chỉ hỗ trợ đúng 1 Solid Body trên mỗi Frame.");
            Body2 original = raw.GetValue(0) as Body2;
            transform = component.GetTotalTransform(false);
            if (original == null || transform == null)
                throw new InvalidOperationException("Body/Transform không đọc được.");
            Body2 world = CopyBody(original);
            if (!world.ApplyTransform(transform))
                throw new InvalidOperationException("Không chuyển Body về Assembly XYZ.");
            return world;
        }

        private static List<Body2> BooleanParts(Body2 target, Body2 tool,
                                                 int operation, string label)
        {
            if (target == null || tool == null)
                throw new InvalidOperationException("Thiếu B-rep: " + label);
            int error;
            object raw = target.Operations2(operation, tool, out error);
            if (error != 0)
                throw new InvalidOperationException("Boolean " + label +
                    " thất bại, code=" + error);
            var result = new List<Body2>();
            Array array = raw as Array;
            if (array == null) return result;
            if (array.Length > MaxBodiesPerComponent)
                throw new InvalidOperationException(
                    "Boolean có quá nhiều kết quả: " + label);
            foreach (object value in array)
            {
                Body2 body = value as Body2;
                if (body == null || !body.IsTemporaryBody())
                    throw new InvalidOperationException("Kết quả Boolean không hợp lệ.");
                result.Add(body);
            }
            return result;
        }

        private static double VolumeTotal(List<Body2> bodies)
        {
            double volume = 0.0;
            foreach (Body2 body in bodies)
            {
                double[] props = body.GetMassProperties(1.0) as double[];
                if (props == null || props.Length < 4 ||
                    Double.IsNaN(props[3]) || Double.IsInfinity(props[3]) ||
                    props[3] <= 0.0)
                    throw new InvalidOperationException("Không xác định được thể tích Solid.");
                volume += props[3];
            }
            return volume;
        }

        private static double GetMaxBoxDiagonal(List<Body2> bodies)
        {
            double diagonal = 0.0;
            foreach (Body2 body in bodies)
            {
                double[] box = body.GetBodyBox() as double[];
                if (box == null || box.Length < 6)
                    throw new InvalidOperationException("Thiếu bounding box.");
                double dx = box[3] - box[0];
                double dy = box[4] - box[1];
                double dz = box[5] - box[2];
                double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (Double.IsNaN(length) || Double.IsInfinity(length))
                    throw new InvalidOperationException("Bounding box không hữu hạn.");
                diagonal = Math.Max(diagonal, length);
            }
            return diagonal;
        }

        private Body2 CreateBisectorHalfBox(double[] p, double[] n,
                                             double span, bool positive)
        {
            Modeler modeler = app.GetModeler() as Modeler;
            if (modeler == null)
                throw new InvalidOperationException("Không lấy được IModeler.");
            double side = positive ? 1.0 : -1.0;
            double[] box = new double[9] {
                p[0], p[1], p[2],
                side * n[0], side * n[1], side * n[2],
                span, span, span
            };
            Body2 result = modeler.CreateBodyFromBox3(box);
            if (result == null || !result.IsTemporaryBody())
                throw new InvalidOperationException("Không tạo được nửa không gian.");
            return result;
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

        private static List<Body2> SubtractMaterial(
            Body2 original, List<Body2> removed, string label)
        {
            List<Body2> remain = new List<Body2> { CopyBody(original) };
            foreach (Body2 cutter in removed)
            {
                var next = new List<Body2>();
                foreach (Body2 body in remain)
                    next.AddRange(BooleanParts(body, CopyBody(cutter),
                        (int)swBodyOperationType_e.SWBODYCUT, label));
                if (next.Count == 0)
                    throw new InvalidOperationException(
                        "Cắt tạm " + label + " tạo kết quả rỗng.");
                if (next.Count > MaxBodiesPerComponent)
                    throw new InvalidOperationException(
                        label + " sinh quá nhiều mảnh solid.");
                remain = next;
            }
            return remain;
        }

        private void DisplayCandidateBodies(List<Body2> bodies,
                       Component2 component, ModelDoc2 part,
                       MathTransform inverse, MathTransform shift,
                       int color, double transparency)
        {
            foreach (Body2 world in bodies)
            {
                // Display3(component) places local solid in Assembly.
                // world+shift --> inverse component --> Display3(component).
                Body2 local = CopyBody(world);
                if (!local.ApplyTransform(shift) ||
                    !local.ApplyTransform(inverse))
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
                shown.Add(new ShownBody(local, part));
                int status = local.Display3(component, color,
                    (int)swTempBodySelectOptions_e.swTempBodySelectOptionNone);
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
                ShownBody tracked = new ShownBody(copy, part);
                shown.Add(tracked);

                // Non-selectable preview avoids swModifyBlock.
                // Display in the selected Component context; do not apply
                // another component transform.
                int result = copy.Display3(
                    component, colorRef,
                    (int)swTempBodySelectOptions_e.swTempBodySelectOptionNone);
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
                if (IsOriginalAssemblyActive())
                    sourceAssembly.GraphicsRedraw2();
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[AFJ-GHOST] Graphics redraw warning: " + ex);
            }
        }

        private bool ClearInternal(out string error)
        {
            error = null;
            bool attempted = shown.Count > 0;
            List<ShownBody> failed = new List<ShownBody>();
            for (int i = shown.Count - 1; i >= 0; i--)
            {
                ShownBody entry = shown[i];
                try
                {
                    // Official Hide signature accepts ModelDoc2 of the Part.
                    entry.Copy.Hide(entry.Part);
                }
                catch (Exception ex)
                {
                    failed.Add(entry);
                    Trace.WriteLine("[AFJ-GHOST] Hide failed: " + ex);
                    error = "Hide thất bại: " + ex.Message;
                }
            }
            shown.Clear();
            failed.Reverse();
            shown.AddRange(failed);
            if (attempted)
                RedrawSafely();
            return failed.Count == 0;
        }
    }
}
