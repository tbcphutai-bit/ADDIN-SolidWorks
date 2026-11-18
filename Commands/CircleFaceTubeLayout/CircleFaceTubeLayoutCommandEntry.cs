using System;
using System.Diagnostics;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands.CircleFaceTubeLayout
{
    /// <summary>
    /// Standalone entry point for Circle Along Face V3 (Tạo ống 架台).
    /// Runs completely independently of BomTaskPaneControl and PropertyManagerPage
    /// so no state, selection filter, or lifecycle is shared with other commands.
    /// </summary>
    public static class CircleFaceTubeLayoutCommandEntry
    {
        private static readonly object syncRoot = new object();
        private static Form activeDialog;
        private static bool running;

        public static bool IsOpen
        {
            get
            {
                lock (syncRoot)
                {
                    return running || (activeDialog != null && !activeDialog.IsDisposed);
                }
            }
        }

        internal static void AttachDialog(Form dialog)
        {
            lock (syncRoot)
            {
                activeDialog = dialog;
            }
        }

        internal static void DetachDialog(Form dialog)
        {
            lock (syncRoot)
            {
                if (ReferenceEquals(activeDialog, dialog))
                    activeDialog = null;
            }
        }

        public static void CloseActiveSession()
        {
            Form dialog;
            lock (syncRoot)
            {
                dialog = activeDialog;
                activeDialog = null;
            }
            if (dialog == null || dialog.IsDisposed) return;
            try
            {
                if (dialog.InvokeRequired)
                    dialog.BeginInvoke(new Action(() => SafeCloseDialog(dialog)));
                else
                    SafeCloseDialog(dialog);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[CircleFaceTubeLayout] CloseActiveSession: " + ex);
            }
        }

        private static void SafeCloseDialog(Form dialog)
        {
            try
            {
                if (!dialog.IsDisposed)
                {
                    dialog.DialogResult = DialogResult.Cancel;
                    dialog.Close();
                }
            }
            catch { }
        }

        public static void Open(ISldWorks app, IWin32Window owner = null)
        {
            lock (syncRoot)
            {
                if (running || (activeDialog != null && !activeDialog.IsDisposed))
                    return;
                running = true;
            }

            try
            {
                new CircleFaceTubeLayoutCommand(app).Run(owner);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[CircleFaceTubeLayout] Open error: " + ex);
                MessageBox.Show(owner,
                    "Không thể chạy Circle Along Face: " + ex.Message,
                    "Circle Along Face V3 - Smart Overhang",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                lock (syncRoot)
                {
                    running = false;
                    activeDialog = null;
                }
            }
        }
    }
}
