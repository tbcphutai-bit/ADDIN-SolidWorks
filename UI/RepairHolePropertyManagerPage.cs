using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Forms.Integration;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using ADDIN.Commands;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.UI
{
    internal sealed class NativeRepairScan
    {
        internal Face2 Face;
        internal List<LenhMakeHole.RepairHoleGroup> Groups;
    }

    internal sealed class NativeRepairDraft
    {
        internal readonly List<LenhMakeHole.RepairHoleGroup> Groups;
        internal readonly string[] Targets;
        internal NativeRepairDraft(List<LenhMakeHole.RepairHoleGroup> groups)
        { Groups = groups ?? new List<LenhMakeHole.RepairHoleGroup>(); Targets = new string[Groups.Count]; }

        internal bool TryBatch(out List<LenhMakeHole.RepairHoleBatchItem> items, out string message)
        {
            items = new List<LenhMakeHole.RepairHoleBatchItem>();
            for (int i = 0; i < Groups.Count; i++)
            {
                string text = (Targets[i] ?? "").Trim();
                if (text.Length == 0) continue;
                bool slot; double diameter, width, length; string normalized;
                MakeHoleOptions parsed; string parseMessage;
                if (!new MakeHolePageValues { SizeText = text.Replace("×", "x") }.TryGetOptions(out parsed, out parseMessage) ||
                    !LenhMakeHole.TryParseRepairTargetText(parsed.HoleSizeText, out slot, out diameter, out width, out length, out normalized) ||
                    !MakeHolePageValues.Positive(diameter) || (slot && (!MakeHolePageValues.Positive(width) || !MakeHolePageValues.Positive(length))))
                { message = "Nhóm " + (i + 1) + ": nhập 4.2 (tròn) hoặc 5x25 (slot); để trống để bỏ qua."; return false; }
                var group = Groups[i];
                // Preserve entered precision when a confirmation reopens the draft.
                normalized = slot ? parsed.LooseType : diameter.ToString("0.###############", CultureInfo.InvariantCulture);
                items.Add(new LenhMakeHole.RepairHoleBatchItem {
                    SourceEquivalentDiameterMm = group.EquivalentDiameterMm, SourceWidthMm = group.WidthMm,
                    SourceHeightMm = group.HeightMm, SourceCount = group.Count, SourceDisplayText = group.DisplayText,
                    RepairAsLoose = slot, RepairDiameterMm = diameter, RepairSlotWidthMm = width,
                    RepairSlotLengthMm = length, RepairInputText = normalized });
            }
            message = items.Count == 0 ? "Nhập kích thước mới vào cột Repair (ít nhất một dòng)." :
                "Sẵn sàng: Repair " + items.Count + " nhóm" +
                (Groups.Count > items.Count ? ", bỏ qua " + (Groups.Count - items.Count) + " nhóm để trống." : ".") + " Bấm ✓.";
            return items.Count > 0;
        }
    }


    internal sealed class NativeRepairTableModel
    {
        public ObservableCollection<RepairHoleRowViewModel> Rows { get; } = new ObservableCollection<RepairHoleRowViewModel>();
        public System.Windows.Visibility TableVisibility { get { return System.Windows.Visibility.Visible; } }
    }

    // Keep the embedded table inside its native container. SW2024 sometimes
    // starts WindowFromHandle at 100 dialog units, regardless of page width.
    // Correct only this control's bounds; no COM calls from window messages.
    internal sealed class NativeRepairElementHost : ElementHost
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct WindowPosition
        {
            internal IntPtr Window, InsertAfter;
            internal int X, Y, Width, Height;
            internal uint Flags;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct ClientRect { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr window, out ClientRect rect);

        protected override void WndProc(ref System.Windows.Forms.Message message)
        {
            const int WindowPositionChanging = 0x0046;
            const uint NoSize = 0x0001;
            const uint NoMove = 0x0002;
            if (message.Msg == WindowPositionChanging && message.LParam != IntPtr.Zero)
            {
                var position = Marshal.PtrToStructure<WindowPosition>(message.LParam);
                var parent = GetParent(message.HWnd);
                int left = (position.Flags & NoMove) == 0 ? position.X : Left;
                ClientRect rect;
                if ((position.Flags & NoSize) == 0 && parent != IntPtr.Zero &&
                    GetClientRect(parent, out rect) && rect.Right > rect.Left && left >= 0)
                {
                    position.Width = Math.Max(1, rect.Right - rect.Left - 2 * left);
                    Marshal.StructureToPtr(position, message.LParam, false);
                }
            }
            base.WndProc(ref message);
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class RepairHolePropertyManagerPage : NativeCommandPropertyManagerPage
    {
        private const int FaceId = 2, ScanId = 3, TableId = 11, ResetId = 14;
        private readonly Func<NativeRepairScan> scan;
        private readonly Action<Face2, List<LenhMakeHole.RepairHoleGroup>, List<LenhMakeHole.RepairHoleBatchItem>> repair;
        private readonly Action resetCache;
        private readonly NativeRepairTableModel tableModel = new NativeRepairTableModel();
        private PropertyManagerPageSelectionbox faceBox;
        private PropertyManagerPageWindowFromHandle tableWindow;
        private ElementHost tableHost;
        private RepairHoleTaskPaneView tableView;
        private NativeRepairDraft draft = new NativeRepairDraft(null);
        private Face2 scannedFace, selectedFace;
        private bool syncing, selectionPending;
        private Face2 attemptedFace;
        private string scanError;
        private int selectionRequest;
        private int faceSelectionCount = -1;
        private int previousPanelWidth, expandedPanelWidth;

        internal ObservableCollection<RepairHoleRowViewModel> Rows { get { return tableModel.Rows; } }

        internal RepairHolePropertyManagerPage(ISldWorks app, Func<NativeRepairScan> scan,
            Action<Face2, List<LenhMakeHole.RepairHoleGroup>, List<LenhMakeHole.RepairHoleBatchItem>> repair,
            Action resetCache, Action<NativeCommandPropertyManagerPage> closed)
            : base(app, "REPAIR HOLE", closed)
        { this.scan = scan; this.repair = repair; this.resetCache = resetCache; }
        protected override bool Supports(int type) { return type == (int)swDocumentTypes_e.swDocPART; }
        protected override bool UsesSelectionBoxes { get { return true; } }
        protected override int SelectionMark(object item, int type)
        { return type == (int)swSelectType_e.swSelFACES && item is Face2 ? 1 : 0; }
        protected override void OnPreparingPage()
        {
            tableHost = new NativeRepairElementHost { Width = 280, Height = 400 };
            previousPanelWidth = Model.GetFeatureManagerWidth();
            int minimumWidth = (int)Math.Ceiling(360.0 * tableHost.DeviceDpi / 96.0);
            if (previousPanelWidth > 0 && previousPanelWidth < minimumWidth && Model.SetFeatureManagerWidth(minimumWidth) == 0)
                expandedPanelWidth = Model.GetFeatureManagerWidth();
        }
        protected override void BuildControls()
        {
            // Picking the face scans it at once; the button only repeats the scan.
            var source = Group(1, "1. Mặt cần Repair");
            faceBox = Selection(source, FaceId, "Mặt phẳng", 1, swSelectType_e.swSelFACES);
            Button(source, ScanId, "Quét lại");

            var targets = Group(10, "2. Kích thước Repair (mm)");
            tableView = new RepairHoleTaskPaneView();
            tableView.ConfigureForNativeCommand();
            tableView.DataContext = tableModel;
            tableHost.Child = tableView;
            tableWindow = (PropertyManagerPageWindowFromHandle)Control(targets, TableId,
                swPropertyManagerPageControlType_e.swControlType_WindowFromHandle, "Bảng Repair",
                "Nhập kích thước ngay tại dòng lỗ tương ứng; để trống để bỏ qua.");
            tableWindow.Height = 250;
            // The supported native HWND host needs no additional COM registration.
            if (!tableWindow.SetWindowHandlex64(tableHost.Handle.ToInt64()))
                throw new InvalidOperationException("Không mở được bảng nhập Repair.");
            Button(targets, ResetId, "Xóa bảng");
        }
        protected override void OnShown()
        {
            faceSelectionCount = -1;
            faceBox.SetSelectionFocus();
            RequestFaceRefresh();
        }
        private Face2 ReadSelectedFace()
        {
            // In SW2024, querying ItemCount on the selection widget alongside
            // a WindowFromHandle host can terminate SOLIDWORKS. This command
            // accepts exactly one face, so read the sole model pick instead.
            if (faceSelectionCount == 0 || faceSelectionCount > 1) return null;
            var manager = Manager;
            if (manager == null || manager.GetSelectedObjectCount2(-1) != 1) return null;
            var face = manager.GetSelectedObject6(1, -1) as Face2;
            var surface = face == null ? null : face.GetSurface() as Surface;
            return surface != null && surface.IsPlane() ? face : null;
        }
        private void PopulateRows()
        {
            syncing = true;
            try
            {
                foreach (var row in Rows) row.PropertyChanged -= OnRowChanged;
                Rows.Clear();
                for (int i = 0; i < draft.Groups.Count; i++)
                {
                    var group = draft.Groups[i];
                    string recommendation = (group.RecommendedDisplayText ?? "").Replace("Ø", "").Trim();
                    var row = new RepairHoleRowViewModel {
                        Group = group, HoleTypeText = group.IsSlotFamily ? "Slot" : "Méo",
                        CurrentSizeText = group.IsSlotFamily
                            ? group.SeedSlotWidthMm.ToString("0.##", CultureInfo.InvariantCulture) + "x" + group.SeedSlotLengthMm.ToString("0.##", CultureInfo.InvariantCulture)
                            : "Ø" + group.EquivalentDiameterMm.ToString("0.00", CultureInfo.InvariantCulture),
                        Count = group.Count, RecommendationText = recommendation.Length == 0 ? "-" : recommendation,
                        RepairInput = draft.Targets[i] ?? "" };
                    row.PropertyChanged += OnRowChanged;
                    Rows.Add(row);
                }
            }
            finally { syncing = false; }
        }
        private void OnRowChanged(object sender, PropertyChangedEventArgs args)
        {
            if (!Interactive || syncing || args.PropertyName != nameof(RepairHoleRowViewModel.RepairInput)) return;
            var row = sender as RepairHoleRowViewModel;
            int index = row == null ? -1 : Rows.IndexOf(row);
            if (index < 0 || index >= draft.Targets.Length) return;
            // WPF's PropertyChanged binding stores every edit directly in its row.
            // Never read native controls or a destroyed HWND while closing.
            draft.Targets[index] = row.RepairInput;
            UpdateReady();
        }
        private void ClearScan()
        {
            scannedFace = null; scanError = null; draft = new NativeRepairDraft(null);
            PopulateRows(); resetCache?.Invoke();
        }
        // Reads the hole groups of the selected face into the table. An expected
        // failure goes to the page message, not to a dialog: the user may have
        // clicked a face without holes on the way to the right one.
        private void RunScan()
        {
            ClearScan();
            attemptedFace = selectedFace;
            try
            {
                var result = scan();
                if (result == null || result.Face == null || result.Groups == null || result.Groups.Count == 0)
                    throw new InvalidOperationException("Không tìm thấy nhóm lỗ cần Repair trên mặt này.");
                if (!Same(result.Face, selectedFace)) throw new InvalidOperationException("Mặt đã chọn thay đổi trong khi quét; hãy chọn lại.");
                scannedFace = result.Face; draft = new NativeRepairDraft(result.Groups);
                PopulateRows();
            }
            catch (InvalidOperationException ex) { scanError = ex.Message; }
        }
        internal void RestoreDraft(Face2 face, List<LenhMakeHole.RepairHoleGroup> groups,
            List<LenhMakeHole.RepairHoleBatchItem> items)
        {
            if (!Interactive) return;
            if (selectionPending)
            {
                tableHost.BeginInvoke((Action)(() => {
                    if (Interactive && !selectionPending) RestoreDraft(face, groups, items);
                }));
                return;
            }
            Handle(() => {
                if (selectionPending || !Same(face, selectedFace)) return;
                scannedFace = face; draft = new NativeRepairDraft(groups);
                for (int i = 0; i < draft.Groups.Count; i++)
                {
                    var group = draft.Groups[i];
                    var item = items.FirstOrDefault(x => x.SourceEquivalentDiameterMm == group.EquivalentDiameterMm &&
                        x.SourceWidthMm == group.WidthMm && x.SourceHeightMm == group.HeightMm &&
                        x.SourceCount == group.Count && x.SourceDisplayText == group.DisplayText);
                    draft.Targets[i] = item == null ? "" : item.RepairInputText;
                }
                PopulateRows();
            });
        }
        public override void OnButtonPress(int id)
        {
            Handle(() => {
                if (id == ResetId) { ClearScan(); faceBox.SetSelectionFocus(); return; }
                if (id != ScanId) return;
                if (selectionPending || selectedFace == null) throw new InvalidOperationException("Chọn một mặt phẳng trước khi quét.");
                RunScan();
            });
        }
        public override void OnSelectionboxListChanged(int id, int count)
        {
            if (!Interactive || id != FaceId) return;
            faceSelectionCount = count;
            RequestFaceRefresh();
        }
        private void RequestFaceRefresh()
        {
            if (!Interactive) return;
            int request = ++selectionRequest;
            selectionPending = true; selectedFace = null; UpdateReady();
            // Read model selection after the native selection callback has returned.
            tableHost.BeginInvoke((Action)(() => {
                if (!Interactive || request != selectionRequest) return;
                Handle(() => {
                    selectedFace = ReadSelectedFace(); selectionPending = false;
                    if (scannedFace != null && !Same(scannedFace, selectedFace)) ClearScan();
                    // No confirmation click: a newly picked face is scanned here, once.
                    if (selectedFace == null) attemptedFace = null;
                    else if (scannedFace == null && !Same(attemptedFace, selectedFace)) RunScan();
                });
            }));
        }
        public override bool OnSubmitSelection(int id, object item, int type, ref string text)
        {
            if (Closing || id != FaceId || SelectionMark(item, type) != 1) return false;
            try { var surface = ((Face2)item).GetSurface() as Surface; return surface != null && surface.IsPlane(); }
            catch { return false; }
        }
        protected override bool Validate(out string message)
        {
            if (selectionPending) { message = "Đang đọc mặt đã chọn…"; return false; }
            if (scannedFace == null || !Same(scannedFace, selectedFace))
            { message = selectedFace != null && scanError != null ? scanError : "Chọn 1 mặt phẳng có lỗ cần Repair."; return false; }
            List<LenhMakeHole.RepairHoleBatchItem> items;
            return draft.TryBatch(out items, out message);
        }
        protected override Action CaptureAccepted()
        {
            string message;
            if (!Validate(out message)) throw new InvalidOperationException(message);
            List<LenhMakeHole.RepairHoleBatchItem> items;
            if (!draft.TryBatch(out items, out message)) throw new InvalidOperationException(message);
            var face = scannedFace; var groups = draft.Groups;
            return () => repair(face, groups, items);
        }
        protected override void OnReleased(bool accepted)
        {
            ++selectionRequest; selectionPending = false;
            foreach (var row in Rows) row.PropertyChanged -= OnRowChanged;
            Rows.Clear();
            if (tableHost != null) { tableHost.Child = null; tableHost.Dispose(); }
            if (previousPanelWidth > 0 && expandedPanelWidth > 0 && Same(App.ActiveDoc, Model) && Model.GetFeatureManagerWidth() == expandedPanelWidth)
                Model.SetFeatureManagerWidth(previousPanelWidth);
            tableHost = null; tableView = null; tableWindow = null; faceBox = null;
            selectedFace = scannedFace = attemptedFace = null; scanError = null; draft = new NativeRepairDraft(null);
            if (!accepted) resetCache?.Invoke();
        }
    }
}
