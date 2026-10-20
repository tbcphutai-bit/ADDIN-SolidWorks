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

                // COLORREF is 0x00BBGGRR, not System.Drawing.Color.ToArgb().
                int colorA = ColorTranslator.ToWin32(Color.FromArgb(0, 180, 235));
                int colorB = ColorTranslator.ToWin32(Color.FromArgb(255, 155, 60));

                int countA = ShowComponent(frameA, colorA, 0.65);
                int countB = ShowComponent(frameB, colorB, 0.65);
                message = String.Format(
                    "Đã hiển thị {0} body A + {1} body B. Đây là hình gốc, CHƯA CẮT.",
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
                    : "Lỗi Preview; còn body cần dọn: " + cleanError;
                return false;
            }
            finally
            {
                busy = false;
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

                // Non-selectable preview avoids swModifyBlock.
                // Display in the selected Component context; no transform is
                // applied here to avoid double-positioning a component body.
                int result = copy.Display3(
                    component, colorRef,
                    (int)swTempBodySelectOptions_e.swTempBodySelectOptionNone);
                if (result != 0)
                    throw new InvalidOperationException(
                        "IBody2.Display3 thất bại, mã lỗi=" + result);

                // Retain a managed reference until Hide() completes.
                shown.Add(new ShownBody(copy, part));
                count++;
            }
            return count;
        }

        /// <summary>
        /// Hide before releasing managed references. Failed hides are RETAINED
        /// for another cleanup attempt; never lose a displayed Body2 reference.
        /// </summary>
        private bool ClearInternal(out string error)
        {
            error = null;
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
            return failed.Count == 0;
        }
    }
}
