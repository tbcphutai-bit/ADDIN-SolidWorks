using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    // Suppress document painting in the supplied session. Never create, hide or
    // exit an application. Visible document windows still evaluate linked text.
    internal sealed class DrawingCommandDisplayScope : IDisposable
    {
        const uint WmSetRedraw = 0x000B;
        readonly ISldWorks app;
        readonly ModelDoc2 original;
        readonly bool previousCommand, drawingVisible;
        readonly string originalSheet;
        readonly List<ViewDisplayState> viewStates = new List<ViewDisplayState>();
        readonly List<DocumentDisplayState> documentStates = new List<DocumentDisplayState>();
        IntPtr documentArea;
        bool commandChanged, redrawSuppressed, visibilityRead, disposed;

        [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int count);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr window, IntPtr rectangle, IntPtr region, uint flags);

        internal DrawingCommandDisplayScope(ISldWorks application)
        {
            app = application;
            if (app == null) return;
            try
            {
                original = app.ActiveDoc as ModelDoc2;
                if (original is DrawingDoc drawing) originalSheet = (drawing.GetCurrentSheet() as Sheet)?.GetName();
                previousCommand = app.CommandInProgress;
                drawingVisible = app.GetDocumentVisible((int)swDocumentTypes_e.swDocDRAWING);
                visibilityRead = true;
                CaptureExistingDisplays();
                app.CommandInProgress = true;
                commandChanged = true;
                // Freeze only the MDI document area. The task pane's progress and
                // Cancel control remain paintable and the main frame stays visible.
                var frame = app.Frame() as IFrame;
                var windows = frame?.ModelWindows as object[];
                if (windows != null)
                    foreach (object value in windows)
                    {
                        var window = value as IModelWindow;
                        if (window == null) continue;
                        IntPtr parent = GetParent(new IntPtr(window.HWndx64));
                        var className = new StringBuilder(256);
                        uint processId;
                        GetWindowThreadProcessId(parent, out processId);
                        GetClassName(parent, className, className.Capacity);
                        string name = className.ToString();
                        if (processId != app.GetProcessID() ||
                            (!name.Equals("MDIClient", StringComparison.OrdinalIgnoreCase) && !name.Equals("swMdiClient", StringComparison.OrdinalIgnoreCase))) continue;
                        IntPtr result;
                        if (SendMessageTimeout(parent, WmSetRedraw, IntPtr.Zero, IntPtr.Zero, 2, 2000, out result) != IntPtr.Zero)
                        { documentArea = parent; redrawSuppressed = true; }
                        break;
                    }
            }
            catch { Dispose(); throw; }
        }

        void CaptureExistingDisplays()
        {
            var documents = app.GetDocuments() as object[];
            if (documents == null) return;
            foreach (object value in documents)
            {
                var drawing = value as DrawingDoc;
                if (drawing == null) continue;
                var model = value as ModelDoc2;
                documentStates.Add(new DocumentDisplayState { Model = model, Visible = model.Visible,
                    Sheet = (drawing.GetCurrentSheet() as Sheet)?.GetName() });
                var sheets = drawing.GetViews() as object[];
                if (sheets == null) continue;
                foreach (object sheet in sheets)
                {
                    var views = sheet as object[];
                    if (views == null) continue;
                    foreach (object entry in views)
                    {
                        var view = entry as IView;
                        if (view == null || view.Type == (int)swDrawingViewTypes_e.swDrawingSheet) continue;
                        viewStates.Add(new ViewDisplayState { View = view, Mode = view.GetDisplayMode2(),
                            Parent = view.GetUseParentDisplayMode(), Faceted = view.GetFacettedHlrDisplay(),
                            Edges = view.GetDisplayEdgesInShadedMode(), ThreadQuality = view.GetCThreadQuality() });
                    }
                }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                foreach (var state in viewStates)
                    Restore(() => {
                        if (state.View.GetDisplayMode2() != state.Mode || state.View.GetDisplayEdgesInShadedMode() != state.Edges ||
                            state.View.GetUseParentDisplayMode() != state.Parent || state.View.GetFacettedHlrDisplay() != state.Faceted)
                        {
                            int mode = state.Mode == (int)swDisplayMode_e.swSHADED_EDGES ? (int)swDisplayMode_e.swSHADED : state.Mode;
                            state.View.SetDisplayMode4(state.Parent, mode, state.Faceted, state.Edges, state.ThreadQuality);
                            state.View.UpdateViewDisplayGeometry();
                        }
                    });
                foreach (var state in documentStates)
                    Restore(() => {
                        var drawing = state.Model as DrawingDoc;
                        if (drawing != null && !string.IsNullOrEmpty(state.Sheet) && (drawing.GetCurrentSheet() as Sheet)?.GetName() != state.Sheet)
                            drawing.ActivateSheet(state.Sheet);
                        if (state.Model.Visible != state.Visible) state.Model.Visible = state.Visible;
                    });
                if (original != null)
                    Restore(() => {
                        if (original is DrawingDoc drawing && !string.IsNullOrEmpty(originalSheet)) drawing.ActivateSheet(originalSheet);
                        if (app.IsSame(app.ActiveDoc, original) != (int)swObjectEquality.swObjectSame)
                        { int errors = 0; app.ActivateDoc3(original.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref errors); }
                    });
                if (visibilityRead) Restore(() => app.DocumentVisible(drawingVisible, (int)swDocumentTypes_e.swDocDRAWING));
                if (commandChanged) Restore(() => app.CommandInProgress = previousCommand);
            }
            finally
            {
                if (redrawSuppressed && IsWindow(documentArea))
                {
                    IntPtr result;
                    SendMessageTimeout(documentArea, WmSetRedraw, new IntPtr(1), IntPtr.Zero, 2, 2000, out result);
                    RedrawWindow(documentArea, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0400);
                }
            }
        }
        static void Restore(Action action) { try { action(); } catch (Exception error) { Trace.WriteLine("[DRAWING SILENT RESTORE] " + error); } }
        sealed class ViewDisplayState
        {
            internal IView View;
            internal int Mode;
            internal bool Parent, Faceted, Edges, ThreadQuality;
        }
        sealed class DocumentDisplayState
        {
            internal ModelDoc2 Model;
            internal string Sheet;
            internal bool Visible;
        }
    }
}
