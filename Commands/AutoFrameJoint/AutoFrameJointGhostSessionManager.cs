using System;
using System.Diagnostics;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.AutoFrameJoint
{
    /// <summary>
    /// Stages the lifetime contract for a future native PropertyManager Preview.
    /// Source only: this class is deliberately NOT connected to the running PMP.
    /// The Shared instance is a stable OWNER, never a replaceable static ghost.
    /// </summary>
    public sealed class AutoFrameJointGhostSessionManager
    {
        public enum SessionState
        {
            Ready,
            Showing,
            PendingCleanup,
            DeferredDocument,
            Closed
        }

        private sealed class Session
        {
            internal readonly Guid Token;
            internal readonly ISldWorks App;
            internal readonly ModelDoc2 Assembly;
            internal readonly AutoFrameJointGhostPreview Ghost;
            internal readonly int UiThreadId;
            internal SessionState State;
            internal bool CloseRequested;

            internal Session(ISldWorks app, ModelDoc2 assembly)
            {
                Token = Guid.NewGuid();
                App = app;
                Assembly = assembly;
                UiThreadId = Thread.CurrentThread.ManagedThreadId;
                Ghost = new AutoFrameJointGhostPreview(app, assembly);
                State = SessionState.Ready;
            }
        }

        private static readonly AutoFrameJointGhostSessionManager shared =
            new AutoFrameJointGhostSessionManager();

        // Shared is never overwritten by a new PMP. A pending COM body keeps
        // its Assembly, Part, and Ghost references until cleanup succeeds.
        public static AutoFrameJointGhostSessionManager Shared
        {
            get { return shared; }
        }

        private Session current;

        private AutoFrameJointGhostSessionManager() { }

        public bool HasSession { get { return current != null; } }

        /// <summary>
        /// This counts tracked bodies, NOT bodies proven to be visible.
        /// Never clear the sole Session reference while this is true.
        /// </summary>
        public bool HasPendingBodies
        {
            get { return current != null && current.Ghost.IsShowing; }
        }

        public SessionState? CurrentState
        {
            get { return current == null ? (SessionState?)null : current.State; }
        }

        /// <summary>
        /// Read-only recovery guidance for an existing Preview session.
        /// IMPORTANT: inactive Assembly does NOT mean the document was closed.
        /// This method never discards a session, never calls Hide and never
        /// activates or closes a SOLIDWORKS document.
        ///
        /// The return value indicates whether diagnostics could be read;
        /// it does not mean that all temporary bodies were hidden.
        /// </summary>
        public bool TryDescribeRecovery(out string message)
        {
            Session session = current;
            if (session == null)
            {
                message = "Không có Ghost Session tồn đọng.";
                return true;
            }

            if (!IsOnOwnerThread(session))
            {
                message = "Chỉ kiểm tra Ghost Session trên đúng luồng SOLIDWORKS.";
                return false;
            }

            bool active = IsBoundAssemblyActive(session.App, session.Assembly);
            bool tracked = session.Ghost.IsShowing;

            if (tracked && !active)
            {
                message = "Phiên Ghost còn Temporary Body được giữ tham chiếu, " +
                          "nhưng Assembly nguồn không active hoặc không truy cập được. " +
                          "Không thể kết luận tài liệu đã đóng. Không xóa session. " +
                          "Nếu Assembly vẫn mở, hãy chuyển về đúng Assembly rồi " +
                          "thử dọn lại; nếu đã đóng, dừng Preview và báo kỹ thuật.";
                return true;
            }

            if (tracked && session.CloseRequested)
            {
                message = "PropertyManager đã đóng nhưng còn Temporary Body " +
                          "chưa dọn. Khi đang ở đúng Assembly, gọi " +
                          "RetryClosedCleanup từ luồng SOLIDWORKS.";
                return true;
            }

            if (tracked)
            {
                message = "Preview hiện còn body được quản lý. " +
                          "Dùng TryClear của đúng phiên trước khi tạo phiên khác.";
                return true;
            }

            if (session.CloseRequested)
            {
                message = "Phiên đã yêu cầu đóng và không còn body được theo dõi. " +
                          "Có thể gọi RetryClosedCleanup để hoàn tất vòng đời.";
                return true;
            }

            message = "Phiên PropertyManager vẫn đang mở. " +
                      "Không khởi tạo một phiên khác chồng lên phiên này.";
            return true;
        }

        /// <summary>
        /// Called once from the SOLIDWORKS UI thread when a future PMP starts.
        /// A second PMP cannot replace an existing or pending session.
        /// </summary>
        public bool TryBegin(ISldWorks app, ModelDoc2 assembly,
                             out Guid token, out string message)
        {
            token = Guid.Empty;
            if (current != null)
            {
                message = current.CloseRequested
                    ? "Còn phiên Ghost đang chờ dọn. Không thể thay thế Assembly cũ."
                    : "Auto Frame Joint đang có một phiên Preview khác.";
                return false;
            }

            // Refuse a non-STA call BEFORE making any SOLIDWORKS COM call.
            // Never create or access this Session via Task.Run/ThreadPool.
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                message = "Hãy gọi Ghost Session trên luồng STA của SOLIDWORKS.";
                return false;
            }

            if (app == null || assembly == null ||
                !IsBoundAssemblyActive(app, assembly))
            {
                message = "Chỉ khởi tạo Preview với Assembly đang mở.";
                return false;
            }

            current = new Session(app, assembly);
            token = current.Token;
            message = "Phiên Ghost đã sẵn sàng; chưa hiển thị body.";
            return true;
        }

        /// <summary>
        /// Future explicit "Xem trạng thái A/B" button callback only.
        /// Never call from OnSelectionboxListChanged.
        /// </summary>
        public bool TryShow(Guid token, Component2 frameA, Component2 frameB,
                            out string message)
        {
            Session session;
            if (!FindOwnedSession(token, out session, out message))
                return false;

            if (session.CloseRequested)
            {
                message = "PropertyManager đang đóng. Không thể tạo Preview.";
                return false;
            }
            if (session.State == SessionState.PendingCleanup ||
                session.State == SessionState.DeferredDocument)
            {
                message = "Còn Preview chưa dọn. Hãy dọn hoặc thử lại khi đúng Assembly.";
                return false;
            }
            if (!IsBoundAssemblyActive(session.App, session.Assembly))
            {
                session.State = SessionState.DeferredDocument;
                message = "Không đúng Assembly của phiên Ghost. Không thay đổi tài liệu.";
                return false;
            }

            bool success;
            try
            {
                success = session.Ghost.TryShow(frameA, frameB, out message);
            }
            catch (Exception ex)
            {
                // Defensive boundary; original Ghost still owns its COM bodies.
                Trace.WriteLine("[AFJ-SESSION] Unexpected TryShow error: " + ex);
                message = "Lỗi Preview. Cần kiểm tra Output; chưa dọn COM.";
                success = false;
            }

            session.State = success
                ? SessionState.Showing
                : (session.Ghost.IsShowing ? SessionState.PendingCleanup
                                           : SessionState.Ready);
            return success;
        }

        /// <summary>
        /// Explicit KEEP / KEEP-CUT mode for the same verified temporary cut plan.
        /// Reuses the ORIGINAL ghost session for safe Hide() on Page close.
        /// Never modifies Part or Assembly.
        /// </summary>
        public bool TryShowCutCandidate(Guid token, Component2 frameA,
                                        Component2 frameB, int candidate,
                                        out string message)
        {
            Trace.WriteLine("[AFJ-R52] stage=SESSION_PREVIEW_ENTER token=" + token.ToString("N") + " candidate=" + candidate);
            Session session;
            if (!FindOwnedSession(token, out session, out message))
            { Trace.WriteLine("[AFJ-R52] stage=SESSION_REJECT reason=" + message); return false; }
            if (session.CloseRequested ||
                session.State == SessionState.PendingCleanup ||
                session.State == SessionState.DeferredDocument)
            {
                Trace.WriteLine("[AFJ-R52] stage=SESSION_REJECT state=" + session.State + " closing=" + session.CloseRequested);
                message = "Ghost pending cleanup or page closing.";
                return false;
            }
            if (!IsBoundAssemblyActive(session.App, session.Assembly))
            {
                Trace.WriteLine("[AFJ-R52] stage=SESSION_REJECT reason=BOUND_ASSEMBLY_INACTIVE");
                session.State = SessionState.DeferredDocument;
                message = "Assembly thay đổi; không tạo Preview.";
                return false;
            }

            bool success;
            try
            {
                Trace.WriteLine("[AFJ-R52] stage=SESSION_GHOST_CALL token=" + token.ToString("N"));
                success = session.Ghost.TryShowCutCandidate(
                    frameA, frameB, candidate, out message);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[AFJ-SESSION] Cut candidate error: " + ex);
                message = "Lỗi temporary geometry; xem Output.";
                success = false;
            }
            session.State = success
                ? SessionState.Showing
                : (session.Ghost.IsShowing ? SessionState.PendingCleanup
                                          : SessionState.Ready);
            Trace.WriteLine("[AFJ-R52] stage=SESSION_PREVIEW_END success=" + success + " state=" + session.State + " tracked=" + session.Ghost.IsShowing + " actual_visibility=UNVERIFIED");
            return success;
        }

        /// <summary>
        /// Explicit reset while the PMP remains open. Also handles pending
        /// Hide failures after returning to the original Assembly.
        /// </summary>
        public bool TryClear(Guid token, out string message)
        {
            Session session;
            if (!FindOwnedSession(token, out session, out message))
                return false;
            if (session.CloseRequested)
            {
                message = "Phiên đang đóng. Sử dụng RetryClosedCleanup.";
                return false;
            }
            return Cleanup(session, out message);
        }

        /// <summary>
        /// Future PropertyManager AfterClose callback. If Hide fails or the
        /// document changed, Shared keeps all references for a later retry.
        /// </summary>
        public bool CloseFromPage(Guid token, out string message)
        {
            Session session;
            if (!FindOwnedSession(token, out session, out message))
                return false;

            session.CloseRequested = true;
            return Cleanup(session, out message);
        }

        /// <summary>
        /// Manual, user-triggered cleanup retry after a failed AfterClose.
        /// No timer, background worker, cross-document activation or looping.
        /// </summary>
        public bool RetryClosedCleanup(out string message)
        {
            if (current == null)
            {
                message = "Không có phiên Ghost cần dọn.";
                return true;
            }
            if (!IsOnOwnerThread(current))
            {
                message = "Phải dọn Preview trên đúng luồng SOLIDWORKS.";
                return false;
            }
            if (!current.CloseRequested)
            {
                message = "Phiên PropertyManager vẫn đang hoạt động.";
                return false;
            }

            return Cleanup(current, out message);
        }

        private bool Cleanup(Session session, out string message)
        {
            // No bodies were registered; no COM Hide calls are needed.
            if (!session.Ghost.IsShowing)
            {
                FinishEmpty(session);
                message = "Không có Temporary Body còn chờ dọn.";
                return true;
            }

            if (!IsBoundAssemblyActive(session.App, session.Assembly))
            {
                session.State = SessionState.DeferredDocument;
                message = "Assembly nguồn không hoạt động. Giữ body để thử dọn sau.";
                return false;
            }

            string error;
            bool cleared;
            try
            {
                cleared = session.Ghost.TryClear(out error);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[AFJ-SESSION] Unexpected TryClear error: " + ex);
                session.State = SessionState.PendingCleanup;
                message = "Lỗi COM khi dọn Preview. Giữ phiên để xử lý sau.";
                return false;
            }

            if (!cleared || session.Ghost.IsShowing)
            {
                session.State = SessionState.PendingCleanup;
                message = "Chưa dọn xong Temporary Body: " +
                          (String.IsNullOrEmpty(error) ? "xem Output." : error);
                return false;
            }

            FinishEmpty(session);
            message = "Đã yêu cầu Hide tất cả Temporary Body của phiên.";
            return true;
        }

        private void FinishEmpty(Session session)
        {
            if (session.Ghost.IsShowing)
                throw new InvalidOperationException(
                    "Cannot discard a Ghost Session while bodies are tracked.");

            if (session.CloseRequested)
            {
                session.State = SessionState.Closed;
                // Session is only released after all tracked bodies hide.
                if (Object.ReferenceEquals(current, session))
                    current = null;
            }
            else
            {
                session.State = SessionState.Ready;
            }
        }

        private bool FindOwnedSession(Guid token, out Session session,
                                      out string message)
        {
            session = current;
            if (session == null || token == Guid.Empty ||
                session.Token != token)
            {
                message = "Phiên Ghost đã hết hạn hoặc không trùng token.";
                return false;
            }
            if (!IsOnOwnerThread(session))
            {
                message = "Callback Ghost phải thực hiện trên đúng luồng SOLIDWORKS.";
                return false;
            }
            message = null;
            return true;
        }

        private static bool IsOnOwnerThread(Session session)
        {
            return Thread.CurrentThread.ManagedThreadId == session.UiThreadId;
        }

        private static bool IsBoundAssemblyActive(ISldWorks app,
                                                  ModelDoc2 assembly)
        {
            try
            {
                if (app == null || assembly == null)
                    return false;
                ModelDoc2 active = app.ActiveDoc as ModelDoc2;
                return active != null &&
                    active.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY &&
                    app.IsSame(active, assembly) ==
                        (int)swObjectEquality.swObjectSame;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[AFJ-SESSION] Assembly identity error: " + ex);
                return false;
            }
        }
    }
}
