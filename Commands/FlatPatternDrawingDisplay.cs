using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    internal static class FlatPatternDrawingDisplay
    {
        // Display settings belong to the Drawing view. Keep the Part's face colors
        // available for the existing Omote/Ura checks.
        internal static bool RemoveShading(IView view)
        {
            if (view == null || !view.IsFlatPatternView()) return false;
            int mode = view.GetDisplayMode2();
            if (mode != (int)swDisplayMode_e.swSHADED &&
                mode != (int)swDisplayMode_e.swSHADED_EDGES) return false;

            bool changed = view.SetDisplayMode4(false, (int)swDisplayMode_e.swHIDDEN,
                false, false, view.GetCThreadQuality());
            if (!changed || view.GetDisplayMode2() != (int)swDisplayMode_e.swHIDDEN)
                throw new InvalidOperationException("Không tắt được màu Flat Pattern View: " + view.GetName2());
            view.UpdateViewDisplayGeometry();
            return true;
        }

        internal static int RemoveShadingOnCurrentSheet(DrawingDoc drawing)
        {
            int changed = 0;
            for (IView view = drawing.GetFirstView() as IView;
                view != null; view = view.GetNextView() as IView)
                if (RemoveShading(view)) changed++;
            return changed;
        }
    }
}
