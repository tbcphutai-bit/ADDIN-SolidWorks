using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public class XoaBalloonMauVang
    {
        private readonly ISldWorks swApp;

        public XoaBalloonMauVang(ISldWorks app)
        {
            swApp = app;
        }

        public int DeleteDanglingBalloons()
        {
            ModelDoc2 drawingModel = swApp?.ActiveDoc as ModelDoc2;

            if (drawingModel == null ||
                drawingModel.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                MessageBox.Show(
                    "Vui long mo Drawing.",
                    "Xoa Balloon mau vang",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return 0;
            }

            DrawingDoc drawing = drawingModel as DrawingDoc;
            if (drawing == null)
                return 0;

            int countDeleted = 0;

            SolidWorks.Interop.sldworks.View view =
                drawing.GetFirstView() as SolidWorks.Interop.sldworks.View;

            // Bỏ Sheet View
            if (view != null)
                view = view.GetNextView() as SolidWorks.Interop.sldworks.View;

            while (view != null)
            {
                countDeleted += DeleteDanglingBalloonsInView(
                    drawingModel,
                    view);

                view =
                    view.GetNextView() as SolidWorks.Interop.sldworks.View;
            }

            drawingModel.ClearSelection2(true);

            //MessageBox.Show(
               // "Da xoa " + countDeleted + " Balloon mau vang.",
               // "Xoa Balloon mau vang",
              //  MessageBoxButtons.OK,
                //MessageBoxIcon.Information);

            return countDeleted;
        }

        private int DeleteDanglingBalloonsInView(
            ModelDoc2 drawingModel,
            SolidWorks.Interop.sldworks.View view)
        {
            object[] annotations = view.GetAnnotations() as object[];

            if (annotations == null || annotations.Length == 0)
                return 0;

            int countDeleted = 0;

            foreach (object item in annotations)
            {
                Annotation annotation = item as Annotation;

                if (annotation == null)
                    continue;

                // Balloon thuộc nhóm Note
                if (annotation.GetType() !=
                    (int)swAnnotationType_e.swNote)
                    continue;

                // Chỉ lấy annotation bị dangling
                if (!annotation.IsDangling())
                    continue;

                Note note =
                    annotation.GetSpecificAnnotation() as Note;

                if (note == null)
                    continue;

                bool isBomBalloon = false;

                try
                {
                    isBomBalloon = note.IsBomBalloon();
                }
                catch
                {
                    continue;
                }

                // Không phải Balloon thì tuyệt đối không xóa
                if (!isBomBalloon)
                    continue;

                if (!annotation.Select3(false, null))
                    continue;

                drawingModel.EditDelete();
                countDeleted++;
            }

            drawingModel.ClearSelection2(true);

            return countDeleted;
        }
    }
}