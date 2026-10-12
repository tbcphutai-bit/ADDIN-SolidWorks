// Adapted from user-supplied DXF.zip; see SOURCE.md.
using ADDIN.Commands.DrawingDxfExporting;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Windows.Shapes;
using Path = System.IO.Path;


public static class ViewHelper
{
    /// <summary>
    /// Tính toán tâm của Sheet Metal Boundary Box và chuyển đổi sang hệ tọa độ Drawing Sheet.
    /// </summary>
    public static double[] GetSheetCenterFromSMBBox(ISldWorks swApp, IDrawingDoc drw, IView swView, IMathUtility mathUtil)
    {
        if (swView == null || drw == null) return null;

        string viewName = swView.Name;
        string partPath = "";

        // 1. Lấy đường dẫn file tham chiếu
        var refDoc = swView.ReferencedDocument;
        if (refDoc != null)
        {
            partPath = refDoc.GetPathName();
        }

        if (string.IsNullOrEmpty(partPath)) return null;

        try
        {
            // 2. Hiện Sketch Bounding Box
            BoudingBoxBank(swApp,drw, partPath, viewName, true);

            // 3. Lấy dữ liệu hiển thị
            IDisplayData swDispData = (IDisplayData)swView.GetSMBoundaryBoxDisplayData2();

            if (swDispData == null) return null;

            int lineCount = swDispData.GetLineCount();
            if (lineCount == 0) return null;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            double zVal = 0;

            // 4. Duyệt qua các line để tìm Min/Max
            for (int i = 0; i < lineCount; i++)
            {
                // Mảng trả về có cấu trúc: [Color, Type, Style, Weight, X1, Y1, Z1, X2, Y2, Z2]
                object lineObj = swDispData.GetLineAtIndex3(i);

                if (lineObj is double[] lineData && lineData.Length >= 10)
                {
                    // Start Point (Index 4, 5, 6)
                    double x1 = lineData[4];
                    double y1 = lineData[5];

                    if (x1 < minX) minX = x1;
                    if (x1 > maxX) maxX = x1;
                    if (y1 < minY) minY = y1;
                    if (y1 > maxY) maxY = y1;

                    // End Point (Index 7, 8, 9)
                    double x2 = lineData[7];
                    double y2 = lineData[8];

                    if (x2 < minX) minX = x2;
                    if (x2 > maxX) maxX = x2;
                    if (y2 < minY) minY = y2;
                    if (y2 > maxY) maxY = y2;

                    zVal = lineData[6]; // Lấy Z từ Start Point
                }
            }

            if (minX == double.MaxValue) return null;

            // 5. Tính tâm trong không gian Model
            double midX = (minX + maxX) / 2.0;
            double midY = (minY + maxY) / 2.0;

            double[] ptArr = new double[] { midX, midY, zVal };
            //MathPoint swMathPt = (MathPoint)mathUtil.CreatePoint(ptArr);

            //// 6. Chuyển đổi sang hệ tọa độ Sheet
            //MathTransform swViewXform = swView.ModelToViewTransform;
            //if (swViewXform != null)
            //{
            //    swMathPt = (MathPoint)swMathPt.MultiplyTransform(swViewXform);
            //}

            return ptArr;
        }
        catch
        {
            return null;
        }
        finally
        {
            // 7. Ẩn lại Sketch
            if (!string.IsNullOrEmpty(partPath))
            {
                BoudingBoxBank(swApp, drw, partPath, viewName, false);
            }
        }
    }
    public static void BoudingBoxBank(ISldWorks swApp,IDrawingDoc swDraw, string filename, string atiView, bool v)
    {
        bool activationStatus = swDraw.ActivateView(atiView);
        if (!activationStatus) return;
        var swView = (IView)swDraw.ActiveDrawingView;
        var refDoc = swView.ReferencedDocument;
        var path = refDoc.GetPathName();
        if (refDoc == null) return;
        string bboxFeatureName = DxfModelAccess.GetBoundingBoxSketchName(refDoc);
       var name =  GetPartNameUltraSafe( swApp,swView);
        var FullSketchName = bboxFeatureName + "@" + name + "@" + atiView;
        ModelDoc2 swDrawModel = (ModelDoc2)swDraw;
        bool status = swDrawModel.Extension.SelectByID2(FullSketchName, "SKETCH", 0, 0, 0, false, 0, null, 0);
        if (status)
        {
            if (v)
            {
                swDrawModel.UnblankSketch();
            }
            else
            {
                swDrawModel.BlankSketch();
            }
            return;
        }
    }

    public static string GetPartNameUltraSafe(ISldWorks swApp, IView swView)
    {
        if (swView == null) return null;
        if (!swView.IsModelLoaded())
        {
            Debug.Print("View đang Detached. Đang load model...");
            swView.LoadModel();
        }

        // Nếu View đang Lightweight, Resolve nó
        if (swView.IsLightweight())
        {
            swView.SetLightweightToResolved();
        }

        // --- BƯỚC 2: THỬ LẤY BẰNG CÁCH THÔNG THƯỜNG (ROOT) ---
        Component2 swComp = null;
        DrawingComponent swRootDrawComp = swView.RootDrawingComponent;

        if (swRootDrawComp != null)
        {
            swComp = (Component2)swRootDrawComp.Component;
        }

        // --- BƯỚC 3: NẾU VẪN NULL (PLAN B) - DÙNG VISIBLE COMPONENTS ---
        // Cách này cực kỳ hiệu quả nếu Root bị lỗi
        if (swComp == null)
        {
            Debug.Print("Root Component bị Null -> Chuyển sang quét Visible Components.");

            // Lấy danh sách các part con nhìn thấy được trong view
            object[] visibleComps = (object[])swView.GetVisibleComponents();

            if (visibleComps != null && visibleComps.Length > 0)
            {
                // Lấy thằng đầu tiên tìm thấy (thường là thằng to nhất hoặc chính nó)
                swComp = (Component2)visibleComps[0];
            }
        }

        // --- BƯỚC 4: XỬ LÝ KẾT QUẢ ---
        if (swComp != null)
        {
            string apiName = swComp.Name2; // VD: Part-18

            // Code convert sang dạng <18> cho đẹp
            string uiName = apiName;
            int lastHyphen = apiName.LastIndexOf('-');
            if (lastHyphen > 0)
            {
                string baseName = apiName.Substring(0, lastHyphen);
                string instanceId = apiName.Substring(lastHyphen + 1);
                uiName = $"{baseName}<{instanceId}>";
            }

            Debug.Print("--- KẾT QUẢ ---");
            Debug.Print("API Name: " + apiName);
            Debug.Print("Hiển thị: " + uiName);
            return apiName;
        }
        else
        {
            // --- BƯỚC CUỐI CÙNG (PLAN C): LẤY STRING KHÔNG CẦN COMPONENT ---
            // Nếu file 3D bị lỗi/mất, ta vẫn lấy được tên file text
            Debug.Print("Vẫn không lấy được Component Object. Lấy tên từ đường dẫn:");

            string modelName = swView.GetReferencedModelName(); // DispId 119
            string config = swView.ReferencedConfiguration;

            if (!string.IsNullOrEmpty(modelName))
            {
                string fName = Path.GetFileNameWithoutExtension(modelName);
                Debug.Print($"File Name: {fName} (Config: {config})");
                // Lưu ý: Cách này không lấy được số <18> vì số đó sinh ra lúc Runtime
            }
        }
        return null;
    }
}