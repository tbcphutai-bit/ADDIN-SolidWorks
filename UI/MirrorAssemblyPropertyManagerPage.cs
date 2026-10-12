using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ADDIN.Commands.MirrorV7.MirrorInPlace;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace ADDIN.UI
{
    // Native SOLIDWORKS PropertyManager of MIRROR in an Assembly: mirror plane
    // and components, as in the built-in Mirror Components command. The result
    // is a NEW assembly; the open assembly is not changed.
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class MirrorAssemblyPropertyManagerPage : PropertyManagerPage2Handler9
    {
        private const int PlaneGroupId = 1;
        private const int PlaneBoxId = 2;
        private const int ComponentGroupId = 3;
        private const int ComponentBoxId = 4;
        private const int HintId = 5;
        private const int PlaneMark = 1;
        private const int ComponentMark = 2;
        private const string Title = "MIRROR ASSEMBLY";

        private readonly ISldWorks app;
        private readonly Action<MirrorAssemblyPropertyManagerPage> closed;
        private readonly ModelDoc2 model;
        private PropertyManagerPage2 page;
        private PropertyManagerPageSelectionbox planeBox;
        private PropertyManagerPageSelectionbox componentBox;
        private bool shown;
        private bool executeAfterClose;
        private double[,] mirror;
        private HashSet<string> components;
        private readonly List<object> initialPlanes = new List<object>();
        private readonly List<Component2> initialComponents = new List<Component2>();

        public MirrorAssemblyPropertyManagerPage(ISldWorks app, Action<MirrorAssemblyPropertyManagerPage> closed)
        {
            this.app = app;
            this.closed = closed;
            model = app == null ? null : app.ActiveDoc as ModelDoc2;
        }

        public bool IsOpen { get { return shown; } }

        public void Show()
        {
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                throw new InvalidOperationException("Hãy mở một Assembly trước khi dùng MIRROR ASSEMBLY.");
            CaptureInitialSelection();
            int error = 0;
            int options =
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton |
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton;
            page = app.CreatePropertyManagerPage(Title, options, this, ref error) as PropertyManagerPage2;
            if (page == null || error != (int)swPropertyManagerPageStatus_e.swPropertyManagerPage_Okay)
                throw new InvalidOperationException("Không tạo được PropertyManager MIRROR ASSEMBLY (" + error + ").");

            int groupOptions =
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible |
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded;
            int controlOptions =
                (int)swAddControlOptions_e.swControlOptions_Visible |
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            short align = (short)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;

            var planeGroup = page.AddGroupBox(PlaneGroupId, "Mặt phẳng mirror", groupOptions) as PropertyManagerPageGroup;
            planeBox = planeGroup.AddControl2(PlaneBoxId,
                (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "Mặt phẳng mirror", align, controlOptions,
                "Chọn một Reference Plane làm mặt phẳng mirror.") as PropertyManagerPageSelectionbox;
            var componentGroup = page.AddGroupBox(ComponentGroupId, "Component cần mirror", groupOptions) as PropertyManagerPageGroup;
            componentBox = componentGroup.AddControl2(ComponentBoxId,
                (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "Component cần mirror", align, controlOptions,
                "Chọn các component cần mirror. Để trống: mirror toàn bộ Assembly.") as PropertyManagerPageSelectionbox;
            var hint = componentGroup.AddControl2(HintId,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Để trống = toàn bộ Assembly. Kết quả là một Assembly mới; Assembly đang mở không thay đổi.",
                align, controlOptions, "") as PropertyManagerPageControl;
            if (planeBox == null || componentBox == null || hint == null)
                throw new InvalidOperationException("Không tạo đủ mục chọn MIRROR ASSEMBLY.");
            planeBox.Mark = PlaneMark;
            planeBox.SingleEntityOnly = true;
            planeBox.Height = 30;
            planeBox.SetSelectionFilters(new int[] { (int)swSelectType_e.swSelDATUMPLANES });
            componentBox.Mark = ComponentMark;
            componentBox.SingleEntityOnly = false;
            componentBox.Height = 140;
            componentBox.SetSelectionFilters(new int[] { (int)swSelectType_e.swSelCOMPONENTS });

            page.Show2(0);
            shown = true;
            RestoreInitialSelection();
            UpdateReadyState();
        }

        // Whatever was selected before the command (components in the tree, a
        // plane) goes into the matching box, as the built-in command does.
        private void CaptureInitialSelection()
        {
            SelectionMgr manager = model.SelectionManager as SelectionMgr;
            if (manager == null) return;
            int count = manager.GetSelectedObjectCount2(-1);
            for (int i = 1; i <= count; i++)
            {
                int type = manager.GetSelectedObjectType3(i, -1);
                object item = manager.GetSelectedObject6(i, -1);
                if (type == (int)swSelectType_e.swSelDATUMPLANES && item != null) initialPlanes.Add(item);
                else if (type == (int)swSelectType_e.swSelCOMPONENTS && item is Component2) initialComponents.Add((Component2)item);
            }
        }

        private void RestoreInitialSelection()
        {
            model.ClearSelection2(true);
            SelectionMgr manager = model.SelectionManager as SelectionMgr;
            if (manager == null) return;
            if (initialPlanes.Count == 1)
            {
                Feature plane = initialPlanes[0] as Feature;
                if (plane != null) plane.Select2(false, PlaneMark);
            }
            foreach (Component2 component in initialComponents)
            {
                SelectData data = manager.CreateSelectData();
                data.Mark = ComponentMark;
                component.Select4(true, data, false);
            }
            if (initialPlanes.Count != 1) planeBox.SetSelectionFocus(); else componentBox.SetSelectionFocus();
        }

        private void UpdateReadyState()
        {
            if (page == null || !shown) return;
            SelectionMgr manager = model.SelectionManager as SelectionMgr;
            page.EnableButton((int)swPropertyManagerPageButtons_e.swPropertyManagerPageButton_Ok,
                manager != null && manager.GetSelectedObjectCount2(PlaneMark) == 1);
        }

        private void Notify(string message, swMessageBoxIcon_e icon)
        {
            if (app != null) app.SendMsgToUser2(message, (int)icon, (int)swMessageBoxBtn_e.swMbOk);
        }

        public void AfterActivation() { }

        // Selections are read here: they are cleared once the page has closed.
        public void OnClose(int reason)
        {
            executeAfterClose = reason == (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay;
            if (!executeAfterClose) return;
            try
            {
                SelectionMgr manager = model.SelectionManager as SelectionMgr;
                Feature planeFeature = manager.GetSelectedObjectCount2(PlaneMark) == 1
                    ? manager.GetSelectedObject6(1, PlaneMark) as Feature : null;
                RefPlane plane = planeFeature == null ? null : planeFeature.GetSpecificFeature2() as RefPlane;
                if (plane == null) throw new InvalidOperationException("Chưa chọn mặt phẳng mirror.");
                mirror = AssemblyMirror71.PlaneReflection(plane, manager.GetSelectedObjectsComponent4(1, PlaneMark) as Component2);
                components = null;
                int count = manager.GetSelectedObjectCount2(ComponentMark);
                if (count > 0)
                {
                    components = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 1; i <= count; i++)
                    {
                        Component2 component = manager.GetSelectedObject6(i, ComponentMark) as Component2
                            ?? manager.GetSelectedObjectsComponent4(i, ComponentMark) as Component2;
                        // A pick inside a sub-assembly stands for its top-level component.
                        for (Component2 parent = component == null ? null : component.GetParent() as Component2; parent != null;
                             parent = component.GetParent() as Component2) component = parent;
                        if (component != null) components.Add(component.Name2);
                    }
                }
            }
            catch (Exception ex)
            {
                executeAfterClose = false;
                Notify("Không đọc được lựa chọn MIRROR ASSEMBLY: " + ex.Message, swMessageBoxIcon_e.swMbStop);
            }
        }

        public void AfterClose()
        {
            shown = false;
            page = null;
            closed?.Invoke(this);
            if (!executeAfterClose) return;
            try
            {
                string sourcePath = model.GetPathName();
                if (string.IsNullOrWhiteSpace(sourcePath)) throw new InvalidOperationException("Lưu Assembly gốc trước khi mirror.");
                string output;
                using (SaveFileDialog dialog = new SaveFileDialog())
                {
                    dialog.Filter = "SolidWorks Assembly (*.sldasm)|*.sldasm";
                    dialog.InitialDirectory = Path.GetDirectoryName(sourcePath);
                    dialog.FileName = Path.GetFileNameWithoutExtension(sourcePath) + "_MIRROR.SLDASM";
                    dialog.Title = "Lưu Assembly mirror (các Part mirror được lưu cùng thư mục)";
                    if (dialog.ShowDialog() != DialogResult.OK) return;
                    output = dialog.FileName;
                }
                AssemblyMirrorResult71 result = AssemblyMirror71.Execute(app, model, mirror, components, output);
                string message = "ĐÃ TẠO ASSEMBLY MIRROR MỚI.\n" + result.OutputPath +
                    "\n\nDùng lại Part gốc (Part tự đối xứng): " + result.Reused +
                    "\nPart mirror mới (độc lập, sửa được): " + result.Mirrored +
                    "\nVị trí và hình học từng component: PASS";
                if (result.LeftOut.Count > 0)
                    message += "\n\nCHƯA ĐƯA VÀO ASSEMBLY MỚI (" + result.LeftOut.Count + "):\n" + string.Join("\n", result.LeftOut);
                Notify(message, result.LeftOut.Count > 0 ? swMessageBoxIcon_e.swMbWarning : swMessageBoxIcon_e.swMbInformation);
            }
            catch (Exception ex)
            {
                Notify("Không tạo được Assembly mirror:\n" + ex.Message +
                    "\nKhông có Assembly kết quả sai nào được giữ lại.", swMessageBoxIcon_e.swMbStop);
            }
        }

        public bool OnHelp()
        {
            Notify("Chọn mặt phẳng mirror, chọn các component (để trống = toàn bộ), bấm dấu ✓.\n\n" +
                   "Part tự đối xứng được dùng lại ở vị trí mới. Part có chiều trái/phải được tạo " +
                   "thành Part mirror độc lập. Kết quả là một Assembly mới, các component được Fix tại chỗ.",
                   swMessageBoxIcon_e.swMbInformation);
            return true;
        }
        public bool OnPreviousPage() { return false; }
        public bool OnNextPage() { return false; }
        public bool OnPreview() { return false; }
        public void OnWhatsNew() { }
        public void OnUndo() { }
        public void OnRedo() { }
        public bool OnTabClicked(int id) { return true; }
        public void OnGroupExpand(int id, bool expanded) { }
        public void OnGroupCheck(int id, bool isChecked) { }
        public void OnCheckboxCheck(int id, bool isChecked) { }
        public void OnOptionCheck(int id) { }
        public void OnButtonPress(int id) { }
        public void OnTextboxChanged(int id, string value) { }
        public void OnNumberboxChanged(int id, double value) { }
        public void OnComboboxEditChanged(int id, string value) { }
        public void OnComboboxSelectionChanged(int id, int item) { }
        public void OnListboxSelectionChanged(int id, int item) { }
        public void OnSelectionboxFocusChanged(int id) { }
        public void OnSelectionboxListChanged(int id, int count) { UpdateReadyState(); }
        public void OnSelectionboxCalloutCreated(int id) { }
        public void OnSelectionboxCalloutDestroyed(int id) { }
        public bool OnSubmitSelection(int id, object selection, int type, ref string itemText)
        {
            if (id == PlaneBoxId) return type == (int)swSelectType_e.swSelDATUMPLANES;
            return id == ComponentBoxId && type == (int)swSelectType_e.swSelCOMPONENTS;
        }
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
