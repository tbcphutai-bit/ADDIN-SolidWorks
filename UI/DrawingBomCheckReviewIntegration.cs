using System;
using System.IO;
using System.Windows.Forms;
using ADDIN.Commands;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN
{
    public partial class BomTaskPaneControl
    {
        private UI.DrawingBomCheckReviewForm drawingBomReviewForm;

        private void ShowDrawingBomCheckReview(DrawingBatchCheckResult result)
        {
            if (drawingBomCommandInProgress || result == null || result.ProcessedCount == 0) return;
            if (drawingBomReviewForm != null && !drawingBomReviewForm.IsDisposed) drawingBomReviewForm.Close();
            drawingBomReviewForm = new UI.DrawingBomCheckReviewForm(result, NavigateDrawingCheckResult);
            drawingBomReviewForm.PositionOnScreen(Screen.FromControl(this).WorkingArea);
            drawingBomReviewForm.FormClosed += (sender, args) => drawingBomReviewForm = null;
            drawingBomReviewForm.Show(this);
            drawingBomReviewForm.Activate();
        }

        private void NavigateDrawingCheckResult(DrawingCheckItemResult item, DrawingBomFieldResult field)
        {
            if (drawingBomCommandInProgress || item == null) return;
            if (field != null && field.HasDrawingLocation)
            {
                drawingBomChecker?.NavigateToDrawingField(item.DrawingPath, field);
                return;
            }
            try
            {
                if (string.IsNullOrWhiteSpace(item.DrawingPath) || !File.Exists(item.DrawingPath))
                    throw new FileNotFoundException("Không tìm thấy Drawing đã kiểm tra.");
                var model = swApp.GetOpenDocumentByName(item.DrawingPath) as ModelDoc2;
                if (model == null)
                {
                    int errors = 0, warnings = 0;
                    model = swApp.OpenDoc6(item.DrawingPath, (int)swDocumentTypes_e.swDocDRAWING,
                        (int)(swOpenDocOptions_e.swOpenDocOptions_Silent | swOpenDocOptions_e.swOpenDocOptions_ReadOnly), "", ref errors, ref warnings) as ModelDoc2;
                    if (model == null) throw new InvalidOperationException("Không mở được Drawing; error=" + errors);
                }
                int activationErrors = 0;
                swApp.ActivateDoc3(model.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref activationErrors);
                model.ViewZoomtofit2();
            }
            catch (Exception error) { lblStatus.Text = "CHECK DRAWING: " + error.Message; }
        }

        private void CloseDrawingBomCheckReview()
        {
            if (drawingBomReviewForm != null && !drawingBomReviewForm.IsDisposed) drawingBomReviewForm.Close();
        }
    }
}
