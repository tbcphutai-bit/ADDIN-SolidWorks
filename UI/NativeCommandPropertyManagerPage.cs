using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace ADDIN.UI
{
    // Shared native command lifetime. Modeling work is deferred to AfterClose.
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public abstract class NativeCommandPropertyManagerPage : PropertyManagerPage2Handler9
    {
        protected readonly ISldWorks App;
        protected readonly ModelDoc2 Model;
        protected PropertyManagerPage2 Page;
        protected bool Initializing, Closing;
        private readonly string title;
        private readonly Action<NativeCommandPropertyManagerPage> closed;
        private readonly List<Pick> initial = new List<Pick>();
        private readonly List<object> ownedControls = new List<object>();
        private List<Pick> acceptedPicks;
        private Action accepted, cancelled;
        private string configuration, sheet, error;
        private bool shown, released, dismissed;
        private System.Windows.Forms.Control lifetimeDispatcher;

        protected sealed class Pick
        {
            internal object Item;
            internal int Type, Mark;
            internal double[] Point;
            internal SolidWorks.Interop.sldworks.View View;
        }

        protected NativeCommandPropertyManagerPage(ISldWorks app, string title,
            Action<NativeCommandPropertyManagerPage> closed)
        { App = app; Model = app == null ? null : app.ActiveDoc as ModelDoc2; this.title = title; this.closed = closed; }

        public bool IsOpen { get { return shown && !Closing && !released; } }
        protected bool Interactive { get { return IsOpen && !Initializing && !dismissed; } }
        protected abstract bool Supports(int documentType);
        protected abstract void BuildControls();
        protected abstract bool Validate(out string message);
        protected abstract Action CaptureAccepted();
        // A command may preserve a cancelled draft after its hosted HWND closes.
        // This callback is separate from acceptance and must not run on Dismiss.
        protected virtual Action CaptureCancelled() { return null; }
        protected virtual int SelectionMark(object item, int type) { return 0; }
        protected virtual int SelectionLimit(int mark) { return 1; }
        protected virtual bool UsesSelectionBoxes { get { return false; } }
        protected virtual void OnReleased(bool accepted) { }
        protected virtual IEnumerable<Pick> PrepareInitialSelections(List<Pick> picks) { return picks; }
        protected virtual void OnShown() { }
        protected virtual void OnPreparingPage() { }
        protected SelectionMgr Manager { get { return Model.SelectionManager as SelectionMgr; } }
        protected virtual int Count(int mark) { return Manager == null ? 0 : Manager.GetSelectedObjectCount2(mark); }
        protected virtual object Selected(int mark) { return Count(mark) == 1 ? Manager.GetSelectedObject6(1, mark) : null; }
        protected bool Same(object a, object b)
        { return a != null && b != null && (ReferenceEquals(a, b) || App.IsSame(a, b) == (int)swObjectEquality.swObjectSame); }

        private string CurrentConfiguration()
        { return Model.GetType() == (int)swDocumentTypes_e.swDocDRAWING ? "" : Model.ConfigurationManager.ActiveConfiguration.Name; }
        private string CurrentSheet()
        {
            var drawing = Model as DrawingDoc;
            var current = drawing == null ? null : drawing.GetCurrentSheet() as Sheet;
            return current == null ? "" : current.GetName();
        }
        protected bool ContextMatches()
        { return Same(App.ActiveDoc, Model) && Supports(Model.GetType()) && configuration == CurrentConfiguration() && sheet == CurrentSheet(); }

        public void Show()
        {
            if (IsOpen) return;
            if (released || dismissed) throw new InvalidOperationException("Bảng đã đóng; hãy mở lại lệnh.");
            if (Model == null || !Supports(Model.GetType())) throw new InvalidOperationException("Tài liệu đang mở không phù hợp với " + title + ".");
            configuration = CurrentConfiguration(); sheet = CurrentSheet();
            for (int i = 1; i <= Count(-1); i++) initial.Add(ReadPick(i, -1));
            // This HWND survives the hosted control and native page. Cleanup
            // must run after Page.Close and its AfterClose callback return.
            lifetimeDispatcher = new System.Windows.Forms.Control();
            var dispatcherHandle = lifetimeDispatcher.Handle;
            try { OnPreparingPage(); }
            catch { Dismiss(); throw; }
            int status = 0;
            int flags = (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton |
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton |
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_LockedPage;
            Page = App.CreatePropertyManagerPage(title, flags, this, ref status) as PropertyManagerPage2;
            if (Page == null || status != 0) { AfterClose(); throw new InvalidOperationException("Không tạo được " + title + " (" + status + ")."); }
            Initializing = true;
            try
            {
                BuildControls();
                status = Page.Show2(0);
                if (status != 0) throw new InvalidOperationException("Hãy kết thúc lệnh SolidWorks đang mở (" + status + ").");
                if (released || dismissed) return;
                shown = true;
                if (UsesSelectionBoxes)
                {
                    var commandPicks = PrepareInitialSelections(initial);
                    Model.ClearSelection2(true);
                    foreach (var pick in commandPicks)
                    {
                        int mark = SelectionMark(pick.Item, pick.Type);
                        if (mark != 0 && Count(mark) < SelectionLimit(mark)) Select(pick, mark);
                    }
                }
            }
            catch { Dismiss(); throw; }
            finally { Initializing = false; }
            OnShown();
            UpdateReady();
        }

        private Pick ReadPick(int index, int mark)
        {
            var pick = new Pick { Item = Manager.GetSelectedObject6(index, mark),
                Type = Manager.GetSelectedObjectType3(index, mark), Mark = Manager.GetSelectedObjectMark(index) };
            try { pick.Point = Manager.GetSelectionPoint2(index, mark) as double[]; } catch (COMException) { }
            if (Model.GetType() == (int)swDocumentTypes_e.swDocDRAWING)
                pick.View = Manager.GetSelectedObjectsDrawingView2(index, mark);
            return pick;
        }
        private bool Select(Pick pick, int mark)
        {
            var data = Manager.CreateSelectData(); data.Mark = mark;
            if (pick.View != null) data.View = pick.View;
            if (pick.Point != null && pick.Point.Length >= 3)
            { data.X = pick.Point[0]; data.Y = pick.Point[1]; data.Z = pick.Point[2]; }
            if (Model.Extension.MultiSelect2(new[] { new DispatchWrapper(pick.Item) }, true, data) <= 0) return false;
            // MultiSelect2 returns the total selected count, including appended
            // objects. Verify this pick rather than expecting a count of one.
            for (int i = 1; i <= Count(mark); i++)
                if (Same(Manager.GetSelectedObject6(i, mark), pick.Item)) return true;
            return false;
        }

        protected void UpdateReady()
        {
            if (!Interactive) return;
            try
            {
                string message = "Tài liệu hoặc cấu hình đã thay đổi. Hãy mở lại lệnh.";
                bool ready = ContextMatches() && Validate(out message);
                Page.SetMessage2(message, (int)swPropertyManagerPageMessageVisibility.swMessageBoxVisible, title);
                Page.EnableButton((int)swPropertyManagerPageButtons_e.swPropertyManagerPageButton_Ok, ready);
            }
            catch (Exception ex)
            {
                if (IsOpen) Page.EnableButton((int)swPropertyManagerPageButtons_e.swPropertyManagerPageButton_Ok, false);
                System.Diagnostics.Debug.WriteLine("[" + title + "] " + ex.Message);
            }
        }
        protected void Handle(Action work)
        {
            if (!Interactive) return;
            try { if (!ContextMatches()) throw new InvalidOperationException("Tài liệu đã thay đổi; hãy mở lại lệnh."); work(); }
            catch (Exception ex) { Notify(ex.Message); }
            UpdateReady();
        }
        protected void CloseOkay() { if (Interactive) Page.Close(true); }
        protected void Notify(string text)
        {
            try { App.SendMsgToUser2(text, (int)swMessageBoxIcon_e.swMbInformation, (int)swMessageBoxBtn_e.swMbOk); }
            catch (COMException) { } catch (InvalidComObjectException) { }
        }
        public void OnClose(int reason)
        {
            if (Closing || released) return;
            Closing = true;
            if (dismissed) return;
            try
            {
                if (reason != (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay)
                {
                    cancelled = CaptureCancelled();
                    return;
                }
                // Snapshot native controls before SolidWorks tears them down.
                if (!ContextMatches()) throw new InvalidOperationException("Tài liệu hoặc cấu hình đã thay đổi; hãy mở lại lệnh.");
                accepted = CaptureAccepted();
                if (accepted == null) return;
                acceptedPicks = new List<Pick>();
                for (int i = 1; i <= Count(-1); i++) acceptedPicks.Add(ReadPick(i, -1));
            }
            catch (Exception ex) { accepted = null; cancelled = null; error = ex.Message; }
        }
        public void AfterClose()
        {
            if (released) return;
            var nativePage = Page;
            released = true; Closing = true; shown = false; Page = null;
            var dispatcher = lifetimeDispatcher;
            lifetimeDispatcher = null;
            Action finish = () => FinishAfterClose(nativePage, dispatcher);
            if (dispatcher != null && dispatcher.IsHandleCreated && !dispatcher.IsDisposed)
                dispatcher.BeginInvoke(finish);
            else finish();
        }
        private void FinishAfterClose(PropertyManagerPage2 nativePage, System.Windows.Forms.Control dispatcher)
        {
            try
            {
                try { OnReleased(!dismissed && accepted != null); }
                finally
                {
                    ReleaseNativeControls(nativePage);
                    closed?.Invoke(this);
                }
                if (dismissed) return;
                if (!ContextMatches())
                { if (accepted != null) Notify("Tài liệu hoặc cấu hình đã thay đổi. Chưa chạy lệnh; hãy mở lại."); return; }
                Model.ClearSelection2(true);
                foreach (var pick in accepted == null ? initial : acceptedPicks)
                    if (!Select(pick, accepted == null ? pick.Mark : 0))
                    { if (accepted != null) { Notify("Không khôi phục được đối tượng đã chọn. Hãy mở lại lệnh."); return; } }
                if (accepted != null) accepted();
                else
                {
                    if (!string.IsNullOrEmpty(error)) Notify(error);
                    if (cancelled != null) cancelled();
                }
            }
            catch (Exception ex) { Notify(ex.Message); }
            finally
            {
                initial.Clear(); acceptedPicks = null; accepted = null; cancelled = null;
                if (dispatcher != null) dispatcher.Dispose();
            }
        }
        public void Dismiss()
        {
            dismissed = true; accepted = null; cancelled = null;
            try { if (Page != null && shown && !released) Page.Close(false); }
            catch (COMException) { }
            catch (InvalidComObjectException) { }
            finally { if (!released) AfterClose(); }
        }

        private void ReleaseNativeControls(PropertyManagerPage2 nativePage)
        {
            // These RCWs belong only to this page. Release them on the UI thread
            // before another page is opened; a later finalizer can touch native
            // control pointers that SOLIDWORKS has already reused.
            for (int i = ownedControls.Count - 1; i >= 0; i--) ReleaseOwnedControl(ownedControls[i]);
            ownedControls.Clear();
            ReleaseOwnedControl(nativePage);
        }
        private static void ReleaseOwnedControl(object control)
        {
            if (control == null || !Marshal.IsComObject(control)) return;
            try { Marshal.FinalReleaseComObject(control); }
            catch (COMException) { } catch (InvalidComObjectException) { }
        }

        private const int ControlFlags = (int)swAddControlOptions_e.swControlOptions_Visible | (int)swAddControlOptions_e.swControlOptions_Enabled;
        protected PropertyManagerPageGroup Group(int id, string title)
        {
            var group = Page.AddGroupBox(id, title, (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible | (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded) as PropertyManagerPageGroup;
            if (group != null) ownedControls.Add(group);
            return group;
        }
        protected object Control(PropertyManagerPageGroup group, int id, swPropertyManagerPageControlType_e type, string title, string tip = "")
        {
            var control = group.AddControl2(id, (short)type, title, (short)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge, ControlFlags, tip);
            if (control == null) throw new InvalidOperationException("Không tạo được trường " + title + ".");
            ownedControls.Add(control);
            return control;
        }
        protected PropertyManagerPageLabel Label(PropertyManagerPageGroup group, int id, string text, short height = 48)
        { var box = (PropertyManagerPageLabel)Control(group, id, swPropertyManagerPageControlType_e.swControlType_Label, ""); box.Caption = text; box.Height = height; return box; }
        protected PropertyManagerPageListbox List(PropertyManagerPageGroup group, int id, string title, string[] items)
        { var box = (PropertyManagerPageListbox)Control(group, id, swPropertyManagerPageControlType_e.swControlType_Listbox, title); box.Height = 150; if (items.Length > 0) box.AddItems(items); return box; }
        protected PropertyManagerPageTextbox Text(PropertyManagerPageGroup group, int id, string title)
        { return (PropertyManagerPageTextbox)Control(group, id, swPropertyManagerPageControlType_e.swControlType_Textbox, title); }
        protected PropertyManagerPageCombobox Combo(PropertyManagerPageGroup group, int id, string title, string[] items)
        { var box = (PropertyManagerPageCombobox)Control(group, id, swPropertyManagerPageControlType_e.swControlType_Combobox, title); box.Style = (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly; box.AddItems(items); return box; }
        protected void Button(PropertyManagerPageGroup group, int id, string title)
        { var box = (PropertyManagerPageButton)Control(group, id, swPropertyManagerPageControlType_e.swControlType_Button, title); box.Caption = title; }
        protected PropertyManagerPageSelectionbox Selection(PropertyManagerPageGroup group, int id, string title, int mark, params swSelectType_e[] types)
        {
            var box = (PropertyManagerPageSelectionbox)Control(group, id, swPropertyManagerPageControlType_e.swControlType_Selectionbox, title);
            var filters = new int[types.Length]; for (int i = 0; i < types.Length; i++) filters[i] = (int)types[i];
            box.SetSelectionFilters(filters); box.Mark = mark; box.SingleEntityOnly = true;
            box.AllowMultipleSelectOfSameEntity = false; box.Height = 30; return box;
        }
        public virtual bool OnSubmitSelection(int id, object item, int type, ref string text) { return !Closing && SelectionMark(item, type) != 0; }
        public virtual void OnButtonPress(int id) { }
        public virtual void OnTextboxChanged(int id, string value) { }
        public virtual void OnListboxSelectionChanged(int id, int item) { }
        public virtual void OnComboboxSelectionChanged(int id, int item) { }
        public virtual void OnCheckboxCheck(int id, bool value) { }
        public virtual void OnSelectionboxListChanged(int id, int count) { UpdateReady(); }
        // Explanations that are not needed to run the command stay behind the page's ? button.
        protected virtual string HelpText { get { return null; } }
        public virtual bool OnHelp() { string text = HelpText; if (!string.IsNullOrEmpty(text)) Notify(text); return true; }
        public virtual void AfterActivation() { }
        public bool OnPreviousPage() { return false; }
        public bool OnNextPage() { return false; }
        public bool OnPreview() { return false; }
        public void OnWhatsNew() { }
        public void OnUndo() { }
        public void OnRedo() { }
        public bool OnTabClicked(int id) { return true; }
        public void OnGroupExpand(int id, bool expanded) { }
        public void OnGroupCheck(int id, bool value) { }
        public void OnOptionCheck(int id) { }
        public virtual void OnNumberboxChanged(int id, double value) { }
        public virtual void OnComboboxEditChanged(int id, string text) { }
        public void OnSelectionboxFocusChanged(int id) { }
        public void OnSelectionboxCalloutCreated(int id) { }
        public void OnSelectionboxCalloutDestroyed(int id) { }
        public int OnActiveXControlCreated(int id, bool status) { return 0; }
        public void OnSliderPositionChanged(int id, double value) { }
        public void OnSliderTrackingCompleted(int id, double value) { }
        public bool OnKeystroke(int wparam, int message, int lparam, int id) { return false; }
        public void OnPopupMenuItem(int id) { }
        public void OnPopupMenuItemUpdate(int id, ref int value) { }
        public void OnGainedFocus(int id) { }
        public void OnLostFocus(int id) { }
        public int OnWindowFromHandleControlCreated(int id, bool status) { return 0; }
        public void OnListboxRMBUp(int id, int x, int y) { }
        public void OnNumberBoxTrackingCompleted(int id, double value) { }
    }
}
