using System;
using System.Diagnostics;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using ADDIN.UI.AutoFrameJoint;

namespace ADDIN.Commands.AutoFrameJoint
{
    /// <summary>
    /// One safe entry point to open native PMP from a future TaskPane button.
    /// Command lifecycle stays alive while the SOLIDWORKS PropertyManager is visible.
    /// UI selection and read-only analysis only; no Cut, Save or Preview mutation.
    /// </summary>
    public static class AutoFrameJointCommandEntry
    {
        private static AutoFrameJointSelectionPage activePage;
        private static bool opening;

        public static void Open(ISldWorks app)
        {
            if (app == null)
                throw new ArgumentNullException(nameof(app));
            if (opening || (activePage != null && activePage.IsOpen))
                return;

            opening = true;
            try
            {
                // Capture the exact host Assembly before SOLIDWORKS opens PMP.
                // Never accept another active document in the callback.
                ModelDoc2 sourceAssembly = app.ActiveDoc as ModelDoc2;
                if (sourceAssembly == null ||
                    sourceAssembly.GetType() !=
                        (int)swDocumentTypes_e.swDocASSEMBLY)
                    throw new InvalidOperationException(
                        "Hãy mở Assembly trước khi chạy Auto Frame Joint.");

                AutoFrameJointSelectionPage page =
                    new AutoFrameJointSelectionPage(
                        app,
                        (frameA, frameB) =>
                        {
                            string report = AutoFrameJointReadOnlyProbe
                                .AnalyzePair(app, sourceAssembly, frameA, frameB);
                            Trace.WriteLine("[AFJ-READONLY] " + report);
                            if (report.StartsWith("ERROR", StringComparison.Ordinal))
                                return "Lỗi phân tích – xem Output.";
                            if (report.StartsWith("UNSUPPORTED", StringComparison.Ordinal) ||
                                report.StartsWith("UNAVAILABLE", StringComparison.Ordinal))
                                return "Frame chưa được hỗ trợ – xem Output.";
                            if (report.StartsWith("NEEDS_SELECTION", StringComparison.Ordinal))
                                return "Hãy chọn lại hai Frame hợp lệ.";
                            if (report.Contains("RESULT: NEEDS_REVIEW") ||
                                report.Contains("ROLE: NEEDS_REVIEW") ||
                                report.Contains("ROLE: EQUAL_OR_AMBIGUOUS"))
                                return "Chưa xác định rõ trục / chiều dài – xem Output.";
                            if (report.Contains("ROLE: A = LONG / B = SHORT") ||
                                report.Contains("ROLE: B = LONG / A = SHORT"))
                                return "Đã ước lượng dài/ngắn – chưa có vết cắt.";
                            // Unrecognized reports must not be misread as success.
                            return "Chưa xác nhận kết quả – xem Output.";
                        },
                        () =>
                        {
                            // No ghost bodies are created in this stage.
                            // A later read-only preview manager will own Hide()/cleanup.
                        },
                        p =>
                        {
                            if (Object.ReferenceEquals(activePage, p))
                                activePage = null;
                        });

                activePage = page;
                try
                {
                    page.Show();
                }
                catch
                {
                    if (Object.ReferenceEquals(activePage, page))
                        activePage = null;
                    throw;
                }
            }
            finally
            {
                opening = false;
            }
        }
    }
}
