using System;
using System.Diagnostics;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public class ChinhTiLeDrawingView
    {
        private readonly ISldWorks swApp;

        public ChinhTiLeDrawingView(ISldWorks app)
        {
            swApp = app;
        }

        public void FitSelectedViewByAspectRule()
        {
            try
            {
                Debug.WriteLine(new string('=', 90));
                Debug.WriteLine("FitSelectedViewByAspectRule START");

                ModelDoc2 model = swApp?.ActiveDoc as ModelDoc2;
                if (model == null)
                {
                    MessageBox.Show("Khong co tai lieu nao dang mo.", "Fix ti le", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (model.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
                {
                    MessageBox.Show("Macro nay chi chay trong Drawing.", "Fix ti le", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                DrawingDoc drawing = model as DrawingDoc;
                SelectionMgr selMgr = model.SelectionManager as SelectionMgr;
                if (drawing == null || selMgr == null)
                    return;

                if (selMgr.GetSelectedObjectCount2(-1) == 0)
                {
                    MessageBox.Show("Hay chon 1 drawing view truoc.", "Fix ti le", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                SolidWorks.Interop.sldworks.View view =
                    selMgr.GetSelectedObject6(1, -1) as SolidWorks.Interop.sldworks.View;

                if (view == null)
                {
                    MessageBox.Show("Selection thu 1 phai la drawing view.", "Fix ti le", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                double viewW;
                double viewH;
                if (!TryGetViewSize(view, out viewW, out viewH) || viewW <= 0 || viewH <= 0)
                {
                    MessageBox.Show("Kich thuoc view khong hop le.", "Fix ti le", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                double aspect = viewW / viewH;
                Debug.WriteLine("View name = " + view.Name);
                Debug.WriteLine("Initial aspect = " + aspect);
                Debug.WriteLine("Initial scale = " + view.ScaleDecimal);

                if (aspect > 5.0)
                    FitLongView(model, drawing, view);
                else
                    FitShortView(model, drawing, view);

                Debug.WriteLine("FitSelectedViewByAspectRule END");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ERR " + ex.Message);
                MessageBox.Show("Loi: " + ex.Message, "Fix ti le", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void FitLongView(ModelDoc2 model, DrawingDoc drawing, SolidWorks.Interop.sldworks.View view)
        {
            double sheetW;
            double sheetH;
            if (!TryGetSheetSize(drawing, out sheetW, out sheetH))
                return;

            double viewW;
            double viewH;
            if (!TryGetViewSize(view, out viewW, out viewH))
                return;

            double aspect = viewW / viewH;
            if (aspect <= 5.0)
            {
                MessageBox.Show("Case nay dang chi xu ly cho aspect > 5.", "Fix ti le", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            double targetViewWidth = 0.7 * sheetW;
            double newScale = view.ScaleDecimal * (targetViewWidth / viewW);
            SetViewScale(view, newScale);
            model.GraphicsRedraw2();

            if (!TryGetViewSize(view, out viewW, out viewH))
                return;

            SetViewOutlineCenter(view, sheetW / 2.0, sheetH / 2.0);
            model.ClearSelection2(true);
            model.ForceRebuild3(false);
            model.GraphicsRedraw2();
        }

        private void FitShortView(ModelDoc2 model, DrawingDoc drawing, SolidWorks.Interop.sldworks.View view)
        {
            double sheetW;
            double sheetH;
            if (!TryGetSheetSize(drawing, out sheetW, out sheetH))
                return;

            double viewW;
            double viewH;
            if (!TryGetViewSize(view, out viewW, out viewH))
                return;

            double aspect = viewW / viewH;
            if (aspect > 5.0)
            {
                MessageBox.Show("Case nay chi xu ly cho aspect <= 5.", "Fix ti le", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            double maxViewHeight = sheetH / 2.3;
            double maxViewWidth = 0.8 * sheetW;
            double scaleFactor = Math.Min(maxViewHeight / viewH, maxViewWidth / viewW);
            double newScale = view.ScaleDecimal * scaleFactor;

            SetViewScale(view, newScale);
            model.GraphicsRedraw2();

            if (!TryGetViewSize(view, out viewW, out viewH))
                return;

            SetViewOutlineCenter(view, sheetW / 2.0, sheetH / 2.0);
            model.ClearSelection2(true);
            model.ForceRebuild3(false);
            model.GraphicsRedraw2();
        }

        private void SetViewScale(SolidWorks.Interop.sldworks.View view, double scale)
        {
            double numerator;
            double denominator;
            NormalizeScaleToIntegerRatio(scale, out numerator, out denominator);

            view.UseSheetScale = 0;
            view.ScaleRatio = new double[] { numerator, denominator };
            view.ScaleDecimal = numerator / denominator;
        }

        private void NormalizeScaleToIntegerRatio(double scale, out double numerator, out double denominator)
        {
            numerator = 1.0;
            denominator = 1.0;

            if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
                return;

            if (scale >= 1.0)
            {
                numerator = Math.Max(1.0, Math.Floor(scale));
                denominator = 1.0;
                return;
            }

            numerator = 1.0;
            denominator = Math.Max(1.0, Math.Ceiling(1.0 / scale));
        }

        private void SetViewOutlineCenter(SolidWorks.Interop.sldworks.View view, double targetX, double targetY)
        {
            view.PositionLocked = false;

            double[] outline = ToDoubleArray(view.GetOutline(), 4);
            double[] position = ToDoubleArray(view.Position, 2);
            if (outline == null || position == null)
                return;

            double currentX = (outline[0] + outline[2]) / 2.0;
            double currentY = (outline[1] + outline[3]) / 2.0;

            position[0] = position[0] + (targetX - currentX);
            position[1] = position[1] + (targetY - currentY);
            view.Position = position;
        }

        private bool TryGetSheetSize(DrawingDoc drawing, out double sheetW, out double sheetH)
        {
            sheetW = 0;
            sheetH = 0;

            Sheet sheet = drawing.GetCurrentSheet() as Sheet;
            if (sheet == null)
                return false;

            double[] props = ToDoubleArray(sheet.GetProperties(), 7);
            if (props == null)
                return false;

            sheetW = props[5];
            sheetH = props[6];
            return sheetW > 0 && sheetH > 0;
        }

        private bool TryGetViewSize(SolidWorks.Interop.sldworks.View view, out double width, out double height)
        {
            width = 0;
            height = 0;

            double[] outline = ToDoubleArray(view.GetOutline(), 4);
            if (outline == null)
                return false;

            width = Math.Abs(outline[2] - outline[0]);
            height = Math.Abs(outline[3] - outline[1]);
            return true;
        }

        private double[] ToDoubleArray(object value, int minLength)
        {
            double[] doubleArray = value as double[];
            if (doubleArray != null && doubleArray.Length >= minLength)
                return doubleArray;

            object[] objectArray = value as object[];
            if (objectArray == null || objectArray.Length < minLength)
                return null;

            double[] result = new double[objectArray.Length];
            for (int i = 0; i < objectArray.Length; i++)
                result[i] = Convert.ToDouble(objectArray[i]);

            return result;
        }
    }
}
