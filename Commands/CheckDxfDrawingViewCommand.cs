using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.DxfChecking
{
    internal sealed class CheckDxfDrawingViewCommand
    {
        private readonly ISldWorks app;
        private double[] comparedBuffer;
        public ModelDoc2 Drawing { get; private set; }
        public IView View { get; private set; }
        public string ViewName { get; private set; }
        public string DrawingPath { get; private set; }
        public string SheetName { get; private set; }
        public double DisplayScale { get; private set; }
        public int IgnoredEntityCount { get; private set; }
        public IReadOnlyList<DxfEdge> PreviewDrawingEdges { get; private set; }
        public IReadOnlyList<DxfEdge> PreviewDxfEdges { get; private set; }

        public CheckDxfDrawingViewCommand(ISldWorks application)
        {
            app = application;
            Drawing = app == null ? null : app.ActiveDoc as ModelDoc2;
            if (Drawing == null || Drawing.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
                throw new InvalidOperationException("CHECK DXF VIEW chỉ dùng trong Drawing.");
            var selection = Drawing.SelectionManager as SelectionMgr;
            if (selection == null || selection.GetSelectedObjectCount2(-1) != 1)
                throw new InvalidOperationException("Hãy chọn đúng một Flat Pattern Drawing View trước khi bấm CHECK DXF VIEW.");
            View = selection.GetSelectedObject6(1, -1) as IView;
            if (View == null || !View.IsFlatPatternView()) throw new InvalidOperationException("Lựa chọn phải là Flat Pattern Drawing View, không phải cạnh hay Part.");
            ViewName = View.GetName2(); DrawingPath = Drawing.GetPathName(); DisplayScale = View.ScaleDecimal;
            SheetName = ((Sheet)((DrawingDoc)Drawing).GetCurrentSheet()).GetName();
        }

        internal CheckDxfDrawingViewCommand(ISldWorks application, ModelDoc2 drawing, IView view)
        {
            app = application;
            if (app == null || drawing == null || drawing.GetType() != (int)swDocumentTypes_e.swDocDRAWING ||
                app.IsSame(app.ActiveDoc, drawing) != (int)swObjectEquality.swObjectSame)
                throw new InvalidOperationException("Drawing của dòng BOM chưa được mở và kích hoạt.");
            if (view == null || !view.IsFlatPatternView()) throw new InvalidOperationException("Drawing không có Flat Pattern View hợp lệ.");
            Drawing = drawing; View = view; ViewName = view.GetName2(); DrawingPath = drawing.GetPathName(); DisplayScale = view.ScaleDecimal;
            SheetName = ((Sheet)((DrawingDoc)drawing).GetCurrentSheet()).GetName();
        }
        public string SuggestedFile()
        {
            if (string.IsNullOrEmpty(DrawingPath)) return "";
            string folder = Path.GetDirectoryName(DrawingPath);
            string stem = Path.GetFileNameWithoutExtension(DrawingPath);
            // No exporter/path registry exists in this source. Suggest exact names only;
            // the user confirms the actual exported file in the result window.
            foreach (string name in new[] { ViewName, stem + "_" + ViewName, stem })
            {
                if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
                string path = Path.Combine(folder, name + ".dxf"); if (File.Exists(path)) return path;
            }
            return "";
        }

        public DxfCheckResult Run(string path, DrawingDxfCheckOptions options)
        {
            PreviewDrawingEdges = PreviewDxfEdges = null;
            options.Validate(); VerifyContext();
            if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".dxf", StringComparison.OrdinalIgnoreCase)) throw new FileNotFoundException("Hãy chọn DXF vừa xuất từ View này.", path);
            comparedBuffer = null;
            var source = DrawingComparisonSource.Capture(Drawing, View, path, SheetName);
            DrawingViewPreparation.Prepare(Drawing, View, source);
            var extractor = new FlatDrawingViewGeometryExtractor();
            var reference = extractor.Extract(View);
            DisplayScale = View.ScaleDecimal;
            var exported = new DxfGeometryReader().Read(path, options.IgnoredDxfLayers, out int ignored);
            IgnoredEntityCount = ignored;
            var result = ContourMatcher.Check(reference, exported, options);
            source.VerifySavedFiles();
            result.Source = source; result.Normalization = extractor.Normalization;
            comparedBuffer = extractor.LastBuffer;
            // The result window draws the same geometry used by this comparison.
            // Preview alignment is restricted to the result's X/Y translation.
            PreviewDrawingEdges = reference.AsReadOnly();
            PreviewDxfEdges = exported.AsReadOnly();
            return result;
        }

        public void VerifyComparedGeometry()
        {
            VerifyContext(); var current = View.GetPolyLinesAndCurves(1) as double[];
            if (comparedBuffer == null || current == null || current.Length != comparedBuffer.Length ||
                current.Where((value, i) => Math.Abs(value - comparedBuffer[i]) > 1e-10).Any())
                throw new InvalidOperationException("Hình học Drawing View đã thay đổi. Hãy kiểm tra lại trước khi đánh dấu lỗi.");
        }

        public void VerifyContext()
        {
            var active = app.ActiveDoc as ModelDoc2;
            if (active == null || app.IsSame(active, Drawing) != (int)swObjectEquality.swObjectSame || active.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
                throw new InvalidOperationException("Hãy quay lại Drawing đã chọn, hoặc đóng cửa sổ và chọn View khác.");
            // Reading the captured view also rejects deleted/unloaded COM objects.
            if (View.GetName2() != ViewName || !View.IsFlatPatternView()) throw new InvalidOperationException("Drawing View đã thay đổi. Hãy chọn lại View.");
            if (((Sheet)((DrawingDoc)Drawing).GetCurrentSheet()).GetName() != SheetName) throw new InvalidOperationException("Hãy quay lại Sheet đã chọn trước khi kiểm tra.");
        }
    }

    internal sealed class DxfGeometryReader
    {
        public List<DxfEdge> Read(string path, string ignoredLayers, out int ignored)
        {
            var raw = DxfRawDocument.ReadSavedOutput(path); var file = new DxfFileResult(); raw.Extract(file);
            var layers = new HashSet<string>((ignoredLayers ?? "").Split(';').Select(s => s.Trim()).Where(s => s.Length > 0), StringComparer.OrdinalIgnoreCase);
            // Model space only: sheet annotations are not cut geometry. Coordinates stay in
            // exported millimeters; the existing parser rejects non-mm unit declarations.
            ignored = file.Edges.Count(e => layers.Contains(e.Layer));
            var edges = file.Edges.Where(e => !layers.Contains(e.Layer) && (string.Equals(e.Space, "Model", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(e.Space))).ToList();
            if (edges.Count == 0) throw new InvalidDataException("DXF không có biên dạng trong Model space.");
            return edges;
        }
    }
}
