using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;
using System.IO;
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

        public string RunMirrorPackageSelfTest(string manifestPathOrJson)
        {
            return Commands.CreateMirrorPartPackage.RunSelfTest(swApp, manifestPathOrJson);
        }

        public bool ConnectToSW(object ThisSW, int cookie)
        {
            swApp = (ISldWorks)ThisSW;
            InstanceSwApp = swApp;
            addinID = cookie;
            swApp.SetAddinCallbackInfo(0, this, addinID);

            LoadTaskPane();

            return true;
        }

        public bool DisconnectFromSW()
        {
            UnloadTaskPane();
            if (swApp != null)
            {
                TryReleaseComObject(swApp);
                swApp = null;
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

                swTaskPane = swApp.CreateTaskpaneView2(imagePath, "TAI TOOL");

                if (swTaskPane != null)
                {
                    uiControl = (BomTaskPaneControl)swTaskPane.AddControl(SWTASKPANE_PROGID, "");

                    if (uiControl != null)
                    {
                        uiControl.Init(swApp);
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
            if (uiControl != null)
            {
                try
                {
                    uiControl.ShutdownFromSolidWorks();
                    uiControl.Dispose();
                }
                catch
                {
                }

                uiControl = null;
            }

            if (swTaskPane != null)
            {
                try
                {
                    swTaskPane.DeleteView();
                }
                catch
                {
                }

                TryReleaseComObject(swTaskPane);
                swTaskPane = null;
            }
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
    }
}
