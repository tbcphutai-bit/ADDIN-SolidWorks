using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands.DxfChecking
{
    internal sealed class DrawingMismatchMarker
    {
        private readonly List<Note> notes = new List<Note>();
        public int Mark(CheckDxfDrawingViewCommand command, DxfCheckResult result)
        {
            command.VerifyComparedGeometry();
            if (((Sheet)((DrawingDoc)command.Drawing).GetCurrentSheet()).GetName() != command.SheetName || Math.Abs(command.View.ScaleDecimal - command.DisplayScale) > 1e-12)
                throw new InvalidOperationException("Sheet hoặc tỉ lệ View đã đổi. Hãy chạy kiểm tra lại trước khi đánh dấu.");
            Clear(command);
            var drawing = command.Drawing; var doc = (DrawingDoc)drawing;
            var layers = drawing.GetLayerManager() as LayerMgr;
            if (layers == null) throw new InvalidOperationException("Không lấy được Layer Manager của Drawing.");
            const string layerName = "DXF_CHECK_ERROR";
            if (layers.GetLayer(layerName) == null) layers.AddLayer(layerName, "DXF vs Drawing View", 255, 0, 0);
            // The Drawing API buffer uses view-local projected coordinates. Add the current
            // View position when rendering on the sheet; this never moves the View itself.
            var viewPosition = (double[])command.View.Position;
            drawing.ClearSelection2(true); doc.ActivateView("");
            int count = 0;
            try
            {
                foreach (var mismatch in result.Mismatches.Where(m => m.HasDrawingPosition || (m.HasDxfPosition && result.AlignmentFound)).Take(200))
                {
                    var p = mismatch.HasDrawingPosition ? mismatch.DrawingPosition : mismatch.DxfPosition;
                    var note = drawing.InsertNote("[DXF " + command.ViewName + "] ERR-" + mismatch.Id.ToString("00")) as Note;
                    if (note == null) throw new InvalidOperationException("Không tạo được note đánh dấu lỗi.");
                    notes.Add(note); var annotation = note.GetAnnotation() as Annotation;
                    if (annotation == null) throw new InvalidOperationException("Không lấy được annotation đánh dấu.");
                    annotation.Layer = layerName;
                    if (!annotation.SetPosition2(viewPosition[0] + p.X * command.DisplayScale / 1000, viewPosition[1] + p.Y * command.DisplayScale / 1000, 0))
                        throw new InvalidOperationException("Không đặt được vị trí note đánh dấu.");
                    count++;
                }
            }
            finally { drawing.ClearSelection2(true); drawing.GraphicsRedraw2(); }
            return count;
        }

        public void Clear(CheckDxfDrawingViewCommand command)
        {
            command.VerifyContext(); var drawing = command.Drawing;
            foreach (var note in notes.ToList())
            {
                try
                {
                    var annotation = note.GetAnnotation() as Annotation;
                    if (annotation != null)
                    {
                        drawing.ClearSelection2(true);
                        if (!annotation.Select3(false, null)) throw new InvalidOperationException("Không chọn được note để xóa. Có thể xóa thủ công trên layer DXF_CHECK_ERROR.");
                        drawing.EditDelete();
                    }
                    notes.Remove(note);
                }
                catch (System.Runtime.InteropServices.COMException) { notes.Remove(note); }
            }
            drawing.ClearSelection2(true); drawing.GraphicsRedraw2();
        }
    }
}
