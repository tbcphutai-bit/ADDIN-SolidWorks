using System;
using System.Diagnostics;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using ADDIN.UI.AutoFrameJoint;

namespace ADDIN.Commands.AutoFrameJoint
{
    /// <summary>
    /// Native PropertyManager entry for exactly two Part occurrences.
    /// Read-only Analyze, original Ghost, and user-triggered P1/P2 TEMPORARY
    /// Boolean both-frame cut candidates. Does NOT apply any cut to Part,
    /// mate, feature or Save. Candidate trim rules are UNVERIFIED.
    /// </summary>
    public static class AutoFrameJointCommandEntry
    {
        // Parse only explicit field prefixes from the already-computed report.
        // Never repeat B-rep analysis simply to display PMP summary.
        private static string ReportField(string report, string prefix)
        {
            if (String.IsNullOrEmpty(report)) return String.Empty;
            string[] lines = report.Split(new[] { '\r', '\n' },
                                         StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
                    return trimmed.Substring(prefix.Length).Trim();
            }
            return String.Empty;
        }

        private static AutoFrameJointSelectionPage activePage;
        private static bool opening;

        public static void Open(ISldWorks app)
        {
            if (app == null)
                throw new ArgumentNullException(nameof(app));
            if (opening || (activePage != null && activePage.IsOpen))
                return;

            opening = true;
            var manager = AutoFrameJointGhostSessionManager.Shared;
            Guid pageToken = Guid.Empty;
            bool sessionStarted = false;
            bool cleanupNotifiedByPage = false;

            try
            {
                // Capture the source Assembly BEFORE creating the PMP.
                ModelDoc2 sourceAssembly = app.ActiveDoc as ModelDoc2;
                if (sourceAssembly == null ||
                    sourceAssembly.GetType() !=
                        (int)swDocumentTypes_e.swDocASSEMBLY)
                    throw new InvalidOperationException(
                        "Hãy mở Assembly trước khi chạy Auto Frame Joint.");

                string beginMessage;
                if (!manager.TryBegin(app, sourceAssembly, out pageToken,
                                      out beginMessage))
                {
                    string recovery;
                    if (manager.TryDescribeRecovery(out recovery) &&
                        !String.IsNullOrWhiteSpace(recovery))
                        beginMessage += " " + recovery;

                    throw new InvalidOperationException(beginMessage);
                }
                sessionStarted = true;

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
                            string jointState = ReportField(report, "JOINT_STATUS:");
                            if (!String.IsNullOrEmpty(jointState))
                            {
                                string angle = ReportField(report, "JOINT_ANGLE_DEG:");
                                string xyz = ReportField(report, "JOINT_XYZ_MM:");
                                string gap = ReportField(report, "JOINT_FINITE_AXIS_GAP_MM:");
                                string inside = ReportField(report,
                                    "JOINT_NEAR_POINTS_INSIDE_SPANS:");
                                string role = ReportField(report, "ROLE:");
                                return "Đã tính giao góc 3D – CHƯA CẮT PART." + "\n" +
                                    "Góc 2 trục: " + angle + "°" + "\n" +
                                    "Điểm gần nhất XYZ: " + xyz + " mm" + "\n" +
                                    "Độ lệch 2 trục: " + gap + " mm" + "\n" +
                                    "Trong chiều dài: " + inside +
                                    " | " + jointState + "\n" +
                                    "Dài/ngắn: " + role;
                            }
                            if (report.Contains("ROLE: A = LONG / B = SHORT") ||
                                report.Contains("ROLE: B = LONG / A = SHORT"))
                                return "Đã ước lượng dài/ngắn; chưa tính được giao điểm.";
                            return "Chưa xác nhận kết quả – xem Output.";
                        },
                        (frameA, frameB) =>
                        {
                            // Called ONLY when the user clicks Xem trạng thái A/B.
                            // Do not call this from selection-change callbacks.
                            string status;
                            bool ok = manager.TryShow(pageToken, frameA,
                                                      frameB, out status);
                            Trace.WriteLine("[AFJ-GHOST] " +
                                            (ok ? "SHOW " : "ERROR ") + status);
                            return status;
                        },
                        (frameA, frameB, candidate) =>
                        {
                            // Explicit button only. Boolean construction and
                            // Display3 are on SOLIDWORKS UI STA via Ghost owner.
                            string status;
                            bool ok = manager.TryShowCutCandidate(
                                pageToken, frameA, frameB, candidate, out status);
                            Trace.WriteLine("[AFJ-CUT-CANDIDATE] " +
                                (ok ? "SHOW " : "ERROR ") + status);
                            return status;
                        },
                        () =>
                        {
                            // PropertyManager.AfterClose calls this BEFORE
                            // onClosed; leave the stable Shared manager alive
                            // if Hide fails or the source Assembly is inactive.
                            cleanupNotifiedByPage = true;
                            string status;
                            bool ok = manager.CloseFromPage(pageToken, out status);
                            Trace.WriteLine("[AFJ-GHOST] AfterClose " +
                                            (ok ? "CLEAN " : "PENDING ") + status);
                        },
                        p =>
                        {
                            if (Object.ReferenceEquals(activePage, p))
                                activePage = null;
                        });

                activePage = page;
                page.Show();
            }
            catch
            {
                // Page creation/Show may throw before AfterClose is invoked.
                // Never abandon a registered Session in this case.
                if (sessionStarted && !cleanupNotifiedByPage)
                {
                    try
                    {
                        string status;
                        bool ok = manager.CloseFromPage(pageToken, out status);
                        Trace.WriteLine("[AFJ-GHOST] Open rollback " +
                                        (ok ? "CLEAN " : "PENDING ") + status);
                    }
                    catch (Exception cleanupError)
                    {
                        Trace.WriteLine("[AFJ-GHOST] Open rollback failed: " +
                                        cleanupError);
                    }
                }

                if (activePage != null && !activePage.IsOpen)
                    activePage = null;

                throw;
            }
            finally
            {
                opening = false;
            }
        }
    }
}
