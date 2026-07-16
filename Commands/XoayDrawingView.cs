using System;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public class XoayDrawingView
    {
        private readonly ISldWorks swApp;
        private SolidWorks.Interop.sldworks.View lastView;

        public XoayDrawingView(ISldWorks app)
        {
            swApp = app;
        }

        public void RememberView(SolidWorks.Interop.sldworks.View view)
        {
            if (view != null)
                lastView = view;
        }

        public void ClearRememberedView()
        {
            lastView = null;
        }

        public void RotateClockwise90()
        {
            RotateSelectedOrLastView(Math.PI / 2.0);
        }

        public void RotateCounterClockwise90()
        {
            RotateSelectedOrLastView(-Math.PI / 2.0);
        }

        public void AlignSelectedCurveHorizontal()
        {
            ModelDoc2 drawingModel = swApp?.ActiveDoc as ModelDoc2;
            if (drawingModel == null ||
                drawingModel.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                MessageBox.Show("Vui long mo Drawing.", "HorizontalAlignment", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SelectionMgr selMgr = drawingModel.SelectionManager as SelectionMgr;
            if (selMgr == null)
                return;

            SolidWorks.Interop.sldworks.View view;
            double angle;
            if (!TryGetSelectedCurveAngle(selMgr, out view, out angle))
            {
                MessageBox.Show("Vui long chon mot canh hoac curve trong drawing view.", "HorizontalAlignment", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (view == null)
                view = lastView;

            if (view == null)
            {
                MessageBox.Show("Vui long chon drawing view.", "HorizontalAlignment", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            lastView = view;
            view.Angle = view.Angle - NormalizeAngleToNearestHorizontal(angle);
            drawingModel.ForceRebuild3(false);
            drawingModel.GraphicsRedraw2();
        }

        private void RotateSelectedOrLastView(double angleDelta)
        {
            ModelDoc2 drawingModel = swApp?.ActiveDoc as ModelDoc2;
            if (drawingModel == null ||
                drawingModel.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                MessageBox.Show("Vui long mo Drawing.", "Rotate view", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SolidWorks.Interop.sldworks.View view = GetSelectedDrawingView(drawingModel);
            if (view != null)
                lastView = view;

            view = view ?? lastView;
            if (view == null)
            {
                MessageBox.Show("Vui long chon drawing view.", "Rotate view", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            view.Angle = view.Angle + angleDelta;
            drawingModel.ForceRebuild3(false);
            drawingModel.GraphicsRedraw2();
        }

        private SolidWorks.Interop.sldworks.View GetSelectedDrawingView(ModelDoc2 drawingModel)
        {
            SelectionMgr selMgr = drawingModel.SelectionManager as SelectionMgr;
            if (selMgr == null)
                return null;

            int count = selMgr.GetSelectedObjectCount2(-1);
            for (int i = 1; i <= count; i++)
            {
                SolidWorks.Interop.sldworks.View view =
                    selMgr.GetSelectedObject6(i, -1) as SolidWorks.Interop.sldworks.View;

                if (view != null)
                    return view;
            }

            return null;
        }

        private bool TryGetSelectedCurveAngle(
            SelectionMgr selMgr,
            out SolidWorks.Interop.sldworks.View view,
            out double angle)
        {
            view = null;
            angle = 0;

            int count = selMgr.GetSelectedObjectCount2(-1);
            for (int i = 1; i <= count; i++)
            {
                object selectedObject = selMgr.GetSelectedObject6(i, -1);
                view = selMgr.GetSelectedObjectsDrawingView2(i, -1);
                if (view != null)
                    lastView = view;

                double x1;
                double y1;
                double x2;
                double y2;
                if (!TryGetCurveEndPoints(selectedObject, view, out x1, out y1, out x2, out y2))
                    continue;

                if (Math.Abs(x2 - x1) < 0.000000001 &&
                    Math.Abs(y2 - y1) < 0.000000001)
                    continue;

                angle = Math.Atan2(y2 - y1, x2 - x1);
                return true;
            }

            return false;
        }

        private bool TryGetCurveEndPoints(
            object selectedObject,
            SolidWorks.Interop.sldworks.View view,
            out double x1,
            out double y1,
            out double x2,
            out double y2)
        {
            x1 = 0;
            y1 = 0;
            x2 = 0;
            y2 = 0;

            bool pointsAreModelCoordinates = false;
            Curve curve = null;

            Edge edge = selectedObject as Edge;
            if (edge != null)
            {
                curve = edge.GetCurve() as Curve;
                pointsAreModelCoordinates = true;
            }

            if (curve == null)
            {
                SketchSegment segment = selectedObject as SketchSegment;
                if (segment != null)
                    curve = segment.GetCurve() as Curve;
            }

            if (curve == null)
                curve = selectedObject as Curve;

            if (curve == null)
                return false;

            double startParam;
            double endParam;
            bool isClosed;
            bool isPeriodic;
            if (!curve.GetEndParams(out startParam, out endParam, out isClosed, out isPeriodic))
                return false;

            double[] startPoint = curve.Evaluate(startParam) as double[];
            double[] endPoint = curve.Evaluate(endParam) as double[];
            if (startPoint == null || endPoint == null ||
                startPoint.Length < 3 || endPoint.Length < 3)
                return false;

            if (pointsAreModelCoordinates && view != null)
            {
                if (!TryTransformModelPointToView(view, startPoint, out x1, out y1) ||
                    !TryTransformModelPointToView(view, endPoint, out x2, out y2))
                    return false;

                return true;
            }

            x1 = startPoint[0];
            y1 = startPoint[1];
            x2 = endPoint[0];
            y2 = endPoint[1];
            return true;
        }

        private bool TryTransformModelPointToView(
            SolidWorks.Interop.sldworks.View view,
            double[] modelPoint,
            out double x,
            out double y)
        {
            x = 0;
            y = 0;

            MathTransform transform = view.ModelToViewTransform;
            if (transform == null)
                return false;

            MathUtility mathUtility = swApp.IGetMathUtility();
            if (mathUtility == null)
                return false;

            MathPoint point = mathUtility.CreatePoint(new double[]
            {
                modelPoint[0],
                modelPoint[1],
                modelPoint[2]
            }) as MathPoint;

            if (point == null)
                return false;

            MathPoint transformedPoint = point.MultiplyTransform(transform) as MathPoint;
            if (transformedPoint == null)
                return false;

            double[] data = transformedPoint.ArrayData as double[];
            if (data == null || data.Length < 2)
                return false;

            x = data[0];
            y = data[1];
            return true;
        }

        private double NormalizeAngleToNearestHorizontal(double angle)
        {
            while (angle > Math.PI / 2.0)
                angle -= Math.PI;

            while (angle < -Math.PI / 2.0)
                angle += Math.PI;

            return angle;
        }
    }
}
