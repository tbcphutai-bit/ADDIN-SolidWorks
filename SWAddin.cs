using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;
using System.IO;
using System.Diagnostics;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;

namespace ADDIN
{
    [Guid("B1C7F4B2-9A1E-4B5C-B234-A1B2C3D4E5F7")]
    [ComVisible(true)]
    public interface ISwAddinTestFacade
    {
        string RunMirrorPackageSelfTest(string manifestPathOrJson);
    }

    [Guid("D1C7F4B2-9A1E-4B5C-B234-A1B2C3D4E5F6")]
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public class SwAddin : ISwAddin, ISwAddinTestFacade
    {
        public ISldWorks swApp;
        public static ISldWorks InstanceSwApp;
        private int addinID;
        private ITaskpaneView swTaskPane;
        private BomTaskPaneControl uiControl;
        private bool disconnecting;

        public void OpenStandaloneCurveMacro(string selectedPartNumbers)
        {
            if (uiControl == null) throw new InvalidOperationException("TAI TOOL task pane chưa nạp.");
            // Return from the COM callback before pumping SOLIDWORKS/WinForms commands.
            uiControl.BeginInvoke(new Action(() => {
                try {
                    swTaskPane.ShowView();
                    uiControl.OpenStandaloneCurveMacro(selectedPartNumbers);
                } catch (Exception ex) { Trace.WriteLine("Standalone macro launcher: " + ex); }
            }));
        }

        public string GetStandaloneCurveMacroState()
        {
            if (uiControl == null) throw new InvalidOperationException("TAI TOOL task pane chưa nạp.");
            return uiControl.GetStandaloneCurveMacroState();
        }

        public string RunMirrorPackageSelfTest(string manifestPathOrJson)
        {
            return Commands.CreateMirrorPartPackage.RunSelfTest(swApp, manifestPathOrJson);
        }

        public bool ConnectToSW(object ThisSW, int cookie)
        {
            var startup = Stopwatch.StartNew();
            Trace.WriteLine("[TAI STARTUP] Begin " + DateTime.Now.ToString("O") +
                "; Assembly=" + typeof(SwAddin).Assembly.Location +
                "; MVID=" + typeof(SwAddin).Module.ModuleVersionId);
            swApp = (ISldWorks)ThisSW;
            InstanceSwApp = swApp;
            addinID = cookie;
            swApp.SetAddinCallbackInfo(0, this, addinID);

            Trace.WriteLine("[TAI STARTUP] Callback registration ms=" + startup.ElapsedMilliseconds);
            LoadTaskPane();
            Trace.WriteLine("[TAI STARTUP] ConnectToSW total ms=" + startup.ElapsedMilliseconds);

            return true;
        }

        public bool DisconnectFromSW()
        {
            if (disconnecting) return true;
            disconnecting = true;
            try { UnloadTaskPane(); }
            finally
            {
                // ThisSW is borrowed from the host and shared by commands and
                // other managed clients. Explicitly decrementing that RCW can
                // disconnect a still-live client; never FinalRelease it.
                if (ReferenceEquals(InstanceSwApp, swApp)) InstanceSwApp = null;
                swApp = null;
                disconnecting = false;
            }
            return true;
        }
        public const string SWTASKPANE_PROGID = "ADDIN.BomTaskPaneControl";
        private void LoadTaskPane()
        {
            try
            {
                string assemblyLocation = typeof(SwAddin).Assembly.CodeBase.Replace(@"file:///", "").Replace("/", @"\");
                string assemblyDir = Path.GetDirectoryName(assemblyLocation);
                string imagePath = Path.Combine(assemblyDir, "icons20.png");

                var phase = Stopwatch.StartNew();
                swTaskPane = swApp.CreateTaskpaneView2(imagePath, "TAI TOOL");
                Trace.WriteLine("[TAI STARTUP] CreateTaskpaneView2 ms=" + phase.ElapsedMilliseconds);

                if (swTaskPane != null)
                {
                    phase.Restart();
                    uiControl = (BomTaskPaneControl)swTaskPane.AddControl(SWTASKPANE_PROGID, "");
                    Trace.WriteLine("[TAI STARTUP] AddControl + UI constructor ms=" + phase.ElapsedMilliseconds);

                    if (uiControl != null)
                    {
                        phase.Restart();
                        uiControl.Init(swApp);
                        Trace.WriteLine("[TAI STARTUP] UI Init ms=" + phase.ElapsedMilliseconds);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("Loi nap bang: " + ex.Message);
            }
        }

        private void UnloadTaskPane()
        {
            // Retire fields before native/WinForms teardown can re-enter this
            // callback. Delete the native view once, then dispose any remaining
            // managed control; its Disposed handler may call shutdown again.
            var control = uiControl; uiControl = null;
            var view = swTaskPane; swTaskPane = null;
            if (control != null)
            {
                try
                {
                    control.ShutdownFromSolidWorks();
                }
                catch
                {
                }

            }

            if (view != null)
            {
                try
                {
                    view.DeleteView();
                }
                catch
                {
                }

                TryReleaseComObject(view);
            }
            if (control != null && !control.IsDisposed) try { control.Dispose(); } catch (Exception ex) { Trace.WriteLine("Taskpane disposal: " + ex); }
        }

        private static void TryReleaseComObject(object comObject)
        {
            if (comObject == null)
                return;

            try
            {
                if (Marshal.IsComObject(comObject))
                    Marshal.ReleaseComObject(comObject);
            }
            catch
            {
            }
        }

        private string GeneratePerfectIcon()
        {
            try
            {
                string tempPath = Path.Combine(Path.GetTempPath(), "KikukawaIcon.png");
                using (Bitmap bmp = new Bitmap(16, 16))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Navy);
                        using (Font f = new Font("Arial", 12, FontStyle.Bold))
                        {
                            g.DrawString("K", f, Brushes.White, new PointF(0, 0));
                        }
                    }
                    bmp.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);
                }
                return tempPath;
            }
            catch { return ""; }
        }

        #region Registration
        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            try
            {
                Microsoft.Win32.RegistryKey hklm = Microsoft.Win32.Registry.LocalMachine;
                Microsoft.Win32.RegistryKey addinKey = hklm.CreateSubKey(@"SOFTWARE\SolidWorks\Addins\" + t.GUID.ToString("B"));
                addinKey.SetValue(null, 1);
                addinKey.SetValue("Description", "Kikukawa BOM Management Tool");
                addinKey.SetValue("Title", "TAI TOOL");
            }
            catch { }
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type t)
        {
            try
            {
                Microsoft.Win32.Registry.LocalMachine.DeleteSubKey(@"SOFTWARE\SolidWorks\Addins\" + t.GUID.ToString("B"), false);
            }
            catch { }
        }
        #endregion

        public string RunRepair3DSplineSelfTest(string expectedDocumentPath)
        {
            var model = swApp == null ? null : swApp.ActiveDoc as ModelDoc2;
            if (model == null || string.IsNullOrWhiteSpace(expectedDocumentPath) ||
                !string.Equals(Path.GetFullPath(model.GetPathName()),
                    Path.GetFullPath(expectedDocumentPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Active document differs from the designated test document.");
            string logPath = Path.ChangeExtension(expectedDocumentPath, ".repair3dspline.log");
            using (var listener = new TextWriterTraceListener(logPath))
            {
                bool previousAutoFlush = Trace.AutoFlush;
                Trace.AutoFlush = true;
                Trace.Listeners.Add(listener);
                try
                {
                    Trace.WriteLine("[Repair3DSplineSelfTest] Assembly=" + typeof(SwAddin).Assembly.Location +
                        "; MVID=" + typeof(SwAddin).Module.ModuleVersionId + "; Document=" + model.GetPathName());
                    string result = "NO_RESULT";
                    new Commands.Repair3DSplineCommand(swApp, (message, success) =>
                        result = "SUCCESS=" + success + "; " + message).Run(uiControl);
                    Trace.WriteLine(result); Trace.Flush(); return result;
                }
                finally { Trace.Listeners.Remove(listener); Trace.AutoFlush = previousAutoFlush; }
            }
        }

        public string RunAutoSplineSelfTest(string expectedDocumentPath, int mode)
        {
            var model = swApp == null ? null : swApp.ActiveDoc as ModelDoc2;
            if (model == null || string.IsNullOrWhiteSpace(expectedDocumentPath) ||
                !string.Equals(Path.GetFullPath(model.GetPathName()), Path.GetFullPath(expectedDocumentPath),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Active document differs from the designated test document.");
            using (var listener = new TextWriterTraceListener(Path.ChangeExtension(expectedDocumentPath, ".autospline.log")))
            {
                bool previousAutoFlush = Trace.AutoFlush;
                Trace.AutoFlush = true;
                Trace.Listeners.Add(listener);
                try
                {
                    Trace.WriteLine("[AutoSplineSelfTest] Assembly=" + typeof(SwAddin).Assembly.Location +
                        "; MVID=" + typeof(SwAddin).Module.ModuleVersionId + "; mode=" + mode);
                    string result = "NO_RESULT";
                    var command = new Commands.EdgeToEqualSplineCommand(swApp,
                        (message, success) => result = "SUCCESS=" + success + "; " + message);
                    if (mode == 5) command.RunMinimumPointPreview();
                    else if (mode == 3) command.RunCreate3DFromTwoEdges(uiControl);
                    else if (mode == 4) command.RunCreate2D(uiControl);
                    else if (mode == 1 || mode == 2) command.Run(uiControl, mode == 2);
                    else throw new ArgumentOutOfRangeException(nameof(mode));
                    Trace.WriteLine(result); Trace.Flush(); return result;
                }
                finally { Trace.Listeners.Remove(listener); Trace.AutoFlush = previousAutoFlush; }
            }
        }
        // Read-only native A/B identity. A harness loading another DLL into its
        // own process must never be mistaken for the host's active add-in.
        public string GetBendCheckBinaryIdentity()
        {
            string path=typeof(SwAddin).Assembly.Location;
            using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            using(var hash=System.Security.Cryptography.SHA256.Create())
                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new {Path=path,
                    Sha256=BitConverter.ToString(hash.ComputeHash(file)).Replace("-","").ToLowerInvariant(),
                    Mvid=typeof(SwAddin).Module.ModuleVersionId,Pid=Process.GetCurrentProcess().Id});
        }
    }
}
