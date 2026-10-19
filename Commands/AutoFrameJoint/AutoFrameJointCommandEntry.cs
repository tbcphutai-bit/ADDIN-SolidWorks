using System;
using System.Diagnostics;
using SolidWorks.Interop.sldworks;
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
                AutoFrameJointSelectionPage page =
                    new AutoFrameJointSelectionPage(
                        app,
                        (frameA, frameB) =>
                        {
                            string report = AutoFrameJointReadOnlyProbe
                                .AnalyzePair(app, frameA, frameB);
                            Trace.WriteLine("[AFJ-READONLY] " + report);
                            if (report.StartsWith("ERROR", StringComparison.Ordinal))
                                return "Lỗi phân tích – xem Output.";
                            if (report.StartsWith("UNSUPPORTED", StringComparison.Ordinal) ||
                                report.StartsWith("UNAVAILABLE", StringComparison.Ordinal))
                                return "Frame chưa được hỗ trợ – xem Output.";
                            if (report.Contains("RESULT: NEEDS_REVIEW"))
                                return "Cần kiểm tra hình học – xem Output.";
                            if (report.StartsWith("NEEDS_SELECTION", StringComparison.Ordinal))
                                return "Hãy chọn hai Frame hợp lệ.";
                            // Report is preliminary: not a verified corner cut.
                            return "Đã đọc sơ bộ – chưa có vết cắt.";
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
