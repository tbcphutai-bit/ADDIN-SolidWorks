using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public class XoaDimMauVang
    {
        private readonly ISldWorks swApp;

        public XoaDimMauVang(ISldWorks app)
        {
            swApp = app;
        }

        public int DeleteDanglingDimensions()
        {
            ModelDoc2 drawingModel = swApp?.ActiveDoc as ModelDoc2;
            if (drawingModel == null ||
                drawingModel.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                MessageBox.Show("Vui long mo Drawing.", "Xoa DIM mau vang", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            DrawingDoc drawing = drawingModel as DrawingDoc;
            if (drawing == null)
                return 0;

            int countDeleted = 0;
            SolidWorks.Interop.sldworks.View view =
                drawing.GetFirstView() as SolidWorks.Interop.sldworks.View;

            if (view != null)
                view = view.GetNextView() as SolidWorks.Interop.sldworks.View;

            while (view != null)
            {
                countDeleted += DeleteDanglingDimensionsInView(drawingModel, view);
                view = view.GetNextView() as SolidWorks.Interop.sldworks.View;
            }

            // MessageBox.Show("Da xoa " + countDeleted + " dimension mau vang.", "Xoa DIM mau vang", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return countDeleted;
        }

        private int DeleteDanglingDimensionsInView(ModelDoc2 drawingModel, SolidWorks.Interop.sldworks.View view)
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

                if (annotation.GetType() != (int)swAnnotationType_e.swDisplayDimension)
                    continue;

                if (!annotation.IsDangling())
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
