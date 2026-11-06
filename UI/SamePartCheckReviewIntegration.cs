using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ADDIN.Commands;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN
{
    public partial class BomTaskPaneControl
    {
        private UI.SamePartCheckReviewForm samePartReviewForm;

        private void ShowSamePartCheckReview(SamePartCheckResult result)
        {
            if (drawingBomCommandInProgress || result == null || result.CheckedCount == 0) return;
            if (samePartReviewForm != null && !samePartReviewForm.IsDisposed) samePartReviewForm.Close();
            samePartReviewForm = new UI.SamePartCheckReviewForm(result, NavigateSamePartLocation);
            samePartReviewForm.PositionOnScreen(Screen.FromControl(this).WorkingArea);
            samePartReviewForm.FormClosed += (sender, args) => samePartReviewForm = null;
            samePartReviewForm.Show(this);
            samePartReviewForm.Activate();
        }

        private void NavigateSamePartLocation(SamePartItemResult item, string targetType)
        {
            if (drawingBomCommandInProgress || item == null) return;

            if (string.Equals(targetType, "folder", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(item.PartPath) && File.Exists(item.PartPath))
                    {
                        Process.Start("explorer.exe", "/select,\"" + item.PartPath + "\"");
                    }
                    else if (!string.IsNullOrWhiteSpace(item.PartPath) && Directory.Exists(Path.GetDirectoryName(item.PartPath)))
                    {
                        Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(item.PartPath) + "\"");
                    }
                }
                catch (Exception ex)
                {
                    lblStatus.Text = "CHECK SAME PART: Không mở được thư mục - " + ex.Message;
                }
                return;
            }

            if (string.Equals(targetType, "assembly", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string assemPath = item.AssemblyPaths?.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));
                    if (string.IsNullOrWhiteSpace(assemPath))
                    {
                        lblStatus.Text = "CHECK SAME PART: Chi tiết không có thông tin Assembly chứa nó.";
                        return;
                    }
                    var model = swApp.GetOpenDocumentByName(assemPath) as ModelDoc2;
                    if (model == null)
                    {
                        int errors = 0, warnings = 0;
                        model = swApp.OpenDoc6(assemPath, (int)swDocumentTypes_e.swDocASSEMBLY,
                            (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings) as ModelDoc2;
                        if (model == null)
                            throw new InvalidOperationException("Không mở được Assembly; error=" + errors);
                    }
                    int activationErrors = 0;
                    swApp.ActivateDoc3(model.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref activationErrors);
                    model.ViewZoomtofit2();
                }
                catch (Exception ex)
                {
                    lblStatus.Text = "CHECK SAME PART: " + ex.Message;
                }
                return;
            }

            // Default: targetType == "part"
            try
            {
                if (string.IsNullOrWhiteSpace(item.PartPath) || !File.Exists(item.PartPath))
                    throw new FileNotFoundException("Không tìm thấy file Part: " + item.PartPath);

                var model = swApp.GetOpenDocumentByName(item.PartPath) as ModelDoc2;
                if (model == null)
                {
                    int errors = 0, warnings = 0;
                    model = swApp.OpenDoc6(item.PartPath, (int)swDocumentTypes_e.swDocPART,
                        (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings) as ModelDoc2;
                    if (model == null)
                        throw new InvalidOperationException("Không mở được Part; error=" + errors);
                }

                int activationErrors = 0;
                swApp.ActivateDoc3(model.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref activationErrors);

                if (!string.IsNullOrWhiteSpace(item.Configuration))
                {
                    try { model.ShowConfiguration2(item.Configuration); }
                    catch { }
                }

                model.ViewZoomtofit2();
                lblStatus.Text = "Đã mở Part " + Path.GetFileName(item.PartPath) + (string.IsNullOrWhiteSpace(item.Configuration) ? "" : " [" + item.Configuration + "]");
            }
            catch (Exception ex)
            {
                lblStatus.Text = "CHECK SAME PART: " + ex.Message;
            }
        }

        private void CloseSamePartCheckReview()
        {
            if (samePartReviewForm != null && !samePartReviewForm.IsDisposed)
                samePartReviewForm.Close();
        }
    }
}
