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
        bool keepRedrawDisabled;
        SubclassCallback redrawCallback;
        static readonly UIntPtr SubclassId = new UIntPtr(0x54414944);
        delegate IntPtr SubclassCallback(IntPtr window, uint message, UIntPtr wParam,
            IntPtr lParam, UIntPtr subclassId, UIntPtr referenceData);
        delegate bool WindowCallback(IntPtr window, IntPtr parameter);
        [StructLayout(LayoutKind.Sequential)]
        struct WindowPosition { public IntPtr Window, InsertAfter; public int X, Y, Width, Height; public uint Flags; }
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr window, SubclassCallback callback, UIntPtr id, UIntPtr referenceData);
        [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(IntPtr window, SubclassCallback callback, UIntPtr id);
        [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, WindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetProp(IntPtr window, string name);

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
                if (frame == null) throw new InvalidOperationException("Không lấy được khung SOLIDWORKS hiện tại.");
                IntPtr frameHandle = new IntPtr(frame.GetHWndx64());
                uint processId;
                uint threadId = GetWindowThreadProcessId(frameHandle, out processId);
                if (processId != (uint)app.GetProcessID() || processId != (uint)Process.GetCurrentProcess().Id || threadId != GetCurrentThreadId())
                    throw new InvalidOperationException("Lệnh silent phải chạy trong luồng của add-in SOLIDWORKS hiện tại.");
                EnumChildWindows(frameHandle, (window, parameter) => {
                    var name = new StringBuilder(128);
                    GetClassName(window, name, name.Capacity);
                    if (!name.ToString().Equals("swMdiClient", StringComparison.OrdinalIgnoreCase) &&
                        !name.ToString().Equals("MDIClient", StringComparison.OrdinalIgnoreCase)) return true;
                    documentArea = window;
                    return false;
                }, IntPtr.Zero);
                if (documentArea == IntPtr.Zero)
                    throw new InvalidOperationException("Không tìm được vùng Drawing của phiên hiện tại.");
                // A nested scope does not own the caller's redraw state.
                if (GetProp(documentArea, "SysSetRedraw") != IntPtr.Zero ||
                    (GetWindowLongPtr(documentArea, -16).ToInt64() & 0x10000000) == 0) return;
                redrawCallback = GuardRedraw;
                keepRedrawDisabled = true;
                if (!SetWindowSubclass(documentArea, redrawCallback, SubclassId, UIntPtr.Zero))
                    throw new InvalidOperationException("Không giữ được vùng Drawing silent trong luồng hiện tại.");
                IntPtr result;
                if (SendMessageTimeout(documentArea, WmSetRedraw, IntPtr.Zero, IntPtr.Zero, 2, 2000, out result) == IntPtr.Zero)
                {
                    keepRedrawDisabled = false;
                    RemoveWindowSubclass(documentArea, redrawCallback, SubclassId);
                    throw new InvalidOperationException("Không tạm dừng được hiển thị vùng Drawing.");
                }
                redrawSuppressed = true;
                Trace.WriteLine("[DRAWING SILENT IN PROCESS] pid=" + processId + " mdi=" + documentArea.ToInt64());
            }
            catch { Dispose(); throw; }
        }

        IntPtr GuardRedraw(IntPtr window, uint message, UIntPtr wParam,
            IntPtr lParam, UIntPtr subclassId, UIntPtr referenceData)
        {
            // SOLIDWORKS sends WM_SETREDRAW(TRUE) while opening/activating a
            // document. Keep that same area guarded until all restoration ends.
            if (keepRedrawDisabled && message == WmSetRedraw && wParam != UIntPtr.Zero) return IntPtr.Zero;
            // ApplyDisplayState can show the MDI client directly, bypassing
            // WM_SETREDRAW. Suppress that visibility change on this area only.
            if (keepRedrawDisabled && message == 0x0046 && lParam != IntPtr.Zero)
            {
                var position = (WindowPosition)Marshal.PtrToStructure(lParam, typeof(WindowPosition));
                if ((position.Flags & 0x0040) != 0)
                {
                    position.Flags = (position.Flags & ~0x0040u) | 0x0080u;
                    Marshal.StructureToPtr(position, lParam, false);
                }
            }
            return DefSubclassProc(window, message, wParam, lParam);
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
                    keepRedrawDisabled = false;
                    RemoveWindowSubclass(documentArea, redrawCallback, SubclassId);
                    IntPtr result;
                    SendMessageTimeout(documentArea, WmSetRedraw, new IntPtr(1), IntPtr.Zero, 2, 2000, out result);
                    RedrawWindow(documentArea, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0400);
                    GC.KeepAlive(redrawCallback);
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
