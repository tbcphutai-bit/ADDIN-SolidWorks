using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using ADDIN.Commands;
using ADDIN.UI;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.sldworks;

namespace ADDIN
{
    public partial class BomTaskPaneControl
    {
        private MakeHolePropertyManagerPage nativeMakeHolePage;
        private bool makeHoleActionsOnly;
        private readonly Dictionary<Control, bool> makeHoleTaskPaneLocks = new Dictionary<Control, bool>();
        private bool makeHolePatternQueued, makeHolePatternPosted, makeHoleWaitingForWizardClose;
        private ModelDoc2 pendingMakeHoleModel, pendingMakeHolePart;
        private Component2 pendingMakeHoleComponent;
        private string pendingMakeHoleConfiguration;
        private bool HasTaskPaneMakeHolePattern { get { return makeHoleCommand != null && makeHoleCommand.HasPendingHybridPattern &&
            (nativeMakeHolePage == null || !nativeMakeHolePage.IsPatternPhase); } }

        private void ShowNativeMakeHolePage()
        {
            try
            {
                if (swApp == null) throw new InvalidOperationException("Chưa kết nối SOLIDWORKS.");
                if (nativeMakeHolePage != null && nativeMakeHolePage.IsOpen) return;
                if (nativeModelCommandPage != null && nativeModelCommandPage.IsOpen)
                    throw new InvalidOperationException("Hãy kết thúc lệnh native đang mở trước khi dùng MAKE HOLE.");
                if (autoSplinePage != null && autoSplinePage.IsOpen)
                    throw new InvalidOperationException("Hãy kết thúc AUTO SPLINE trước khi mở MAKE HOLE.");
                if (makeHoleCommand == null)
                {
                    makeHoleCommand = new LenhMakeHole(swApp);
                    BindRepairHoleViewModelToMakeHoleCommand();
                }
                if (makeHoleCommand.HasPendingHybridPattern)
                {
                    ShowNativeMakeHolePatternPage();
                    return;
                }
                var initial = new MakeHolePageValues
                {
                    SizeText = cboRepairHoleDiameter.Text,
                    Direction = NormalizeMakeHoleDirection(cboMakeHoleDirection.Text),
                    HoleLabel = cboHoleType.Text,
                    EdgeOffsetMm = NativeHoleInitialLength(txtMakeHoleEdgeOffset.Text, 20),
                    LeftOffsetMm = NativeHoleInitialLength(txtMakeHoleLeftOffset.Text, 50),
                    RightOffsetMm = NativeHoleInitialLength(txtMakeHoleRightOffset.Text, 50),
                    PitchMm = NativeHoleInitialLength(txtMakeHolePitch.Text, 300),
                    ThicknessMm = NativeHoleInitialLength(txtModelThickness.Text, 0)
                };
                nativeMakeHolePage = new MakeHolePropertyManagerPage(swApp, initial,
                    cboRepairHoleDiameter.Items.Cast<object>().Select(Convert.ToString).ToArray(),
                    cboHoleType.Items.Cast<object>().Select(Convert.ToString).ToArray(),
                    AcceptNativeMakeHole,
                    closedPage =>
                    {
                        if (ReferenceEquals(nativeMakeHolePage, closedPage)) nativeMakeHolePage = null;
                        RestoreMakeHoleTaskPaneCommands();
                        if (!IsDisposed && !Disposing && !solidWorksClosing)
                        {
                            SetMakeHoleUpdateButtonState(false);
                            ShowMakeHoleActions();
                        }
                    },
                    work => BeginInvoke((Action)(() => { if (!IsDisposed && !Disposing && !solidWorksClosing) work(); })),
                    UpdateNativeMakeHole, ResetNativeMakeHole, SetMakeHoleUpdateButtonState);
                nativeMakeHolePage.Show();
                if (nativeMakeHolePage != null && nativeMakeHolePage.IsOpen)
                {
                    ShowMakeHoleActions();
                    // Keep Update and Reset clickable outside; pause unrelated commands.
                    LockOtherMakeHoleTaskPaneCommands(this);
                }
            }
            catch (Exception ex)
            {
                DismissNativeMakeHolePage();
                MessageBox.Show(this, "Không mở được MAKE HOLE:\n" + ex.Message,
                    "MAKE HOLE", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private double NativeHoleInitialLength(string text, double fallback)
        {
            double value;
            if ((TryParsePositiveMillimeter(text, out value) || TryParseFirstPositiveMillimeter(text, out value)) && MakeHolePageValues.Positive(value))
                return value;
            return fallback;
        }

        private void AcceptNativeMakeHole(MakeHolePageValues values, MakeHoleOptions options)
        {
            if (solidWorksClosing || swApp == null || makeHoleCommand == null) return;
            // Executed only after the native page closes and restores its picks.
            // Preserve the existing engine and its Hole Wizard / Pattern continuation.
            repairHolePanelMode = false;
            EnsureComboItem(cboRepairHoleDiameter, options.HoleSizeText);
            cboRepairHoleDiameter.Text = options.HoleSizeText;
            SelectComboItem(cboMakeHoleDirection, options.Direction);
            txtMakeHoleEdgeOffset.Text = values.EdgeOffsetMm.ToString("0.###############", CultureInfo.InvariantCulture);
            txtMakeHoleLeftOffset.Text = values.LeftOffsetMm.ToString("0.###############", CultureInfo.InvariantCulture);
            txtMakeHoleRightOffset.Text = values.RightOffsetMm.ToString("0.###############", CultureInfo.InvariantCulture);
            txtMakeHolePitch.Text = values.PitchMm.ToString("0.###############", CultureInfo.InvariantCulture);
            cboHoleType.Text = options.HoleLabel ?? "";
            SaveMakeHoleSizeHistory(options.HoleSizeText);
            CapturePendingMakeHoleContext();
            makeHoleCommand.Run(options);
            if (!makeHoleCommand.HasPendingHybridPattern) ClearPendingMakeHoleContext();
            ShowMakeHoleActions();
            UpdateMakeHolePatternButton();
            LayoutModelCommandButtons();
        }

        private void UpdateNativeMakeHole(double pitchMm)
        {
            if (solidWorksClosing || swApp == null || IsDisposed || Disposing) return;
            // Preserve UPDATE HOLE's existing equation/path update engine.
            txtMakeHolePitch.Text = pitchMm.ToString("0.###############", CultureInfo.InvariantCulture);
            btnMakeHoleUpdate_Click(this, EventArgs.Empty);
            ShowMakeHoleActions();
        }

        private void ResetNativeMakeHole()
        {
            if (solidWorksClosing || swApp == null || IsDisposed || Disposing) return;
            ResetMakeHolePendingWork();
            ShowMakeHoleActions();
        }

        private void ResetMakeHolePendingWork()
        {
            if (solidWorksClosing || swApp == null || IsDisposed || Disposing) return;
            bool cancelHoleWizard = false;
            try
            {
                // Cancel only a Hole Wizard launched by this captured Make Hole
                // continuation, in the same document/part/configuration.
                if (nativeMakeHolePage == null && makeHoleWaitingForWizardClose &&
                    makeHoleCommand != null && makeHoleCommand.HasPendingHybridPattern && PendingMakeHoleContextMatches())
                {
                    int command; string title; bool uiActive;
                    swApp.GetRunningCommandInfo(out command, out title, out uiActive);
                    cancelHoleWizard = uiActive && command == 39;
                }
            }
            catch (System.Runtime.InteropServices.COMException ex) { System.Diagnostics.Debug.WriteLine("[MAKE HOLE RESET] " + ex.Message); }
            catch (System.Runtime.InteropServices.InvalidComObjectException ex) { System.Diagnostics.Debug.WriteLine("[MAKE HOLE RESET] " + ex.Message); }
            // Clear first: the native Cancel event must not queue the Pattern step.
            makeHoleCommand?.ResetPendingCommand();
            ClearPendingMakeHoleContext();
            if (cancelHoleWizard)
            {
                // swCommands_PmCancel = -1 (standard native PropertyManager Cancel).
                try
                {
                    if (!swApp.RunCommand(-1, ""))
                        System.Diagnostics.Debug.WriteLine("[MAKE HOLE RESET] Native Hole Wizard Cancel was unavailable.");
                }
                catch (System.Runtime.InteropServices.COMException ex) { System.Diagnostics.Debug.WriteLine("[MAKE HOLE RESET] " + ex.Message); }
                catch (System.Runtime.InteropServices.InvalidComObjectException ex) { System.Diagnostics.Debug.WriteLine("[MAKE HOLE RESET] " + ex.Message); }
            }
            UpdateMakeHolePatternButton();
            ShowMakeHoleActions();
        }

        private void ShowMakeHoleActions()
        {
            if (IsDisposed || Disposing || grpMakeHoleOptions == null) return;
            repairHolePanelMode = false;
            makeHoleActionsOnly = true;
            grpMakeHoleOptions.Text = "Make hole";
            foreach (Control control in grpMakeHoleOptions.Controls)
                control.Visible = ReferenceEquals(control, btnMakeHoleUpdate) ||
                    ReferenceEquals(control, btnMakeHoleReset) || ReferenceEquals(control, btnMakeHolePattern);
            bool nativeOpen = nativeMakeHolePage != null;
            lblMakeHolePitch.Text = "Bước P (mm)";
            lblMakeHolePitch.Visible = txtMakeHolePitch.Visible = !nativeOpen;
            btnMakeHoleUpdate.Visible = btnMakeHoleReset.Visible = true;
            btnMakeHoleReset.Enabled = nativeOpen || CanUseMakeHoleActions();
            if (!nativeOpen) txtMakeHolePitch.Enabled = btnMakeHoleReset.Enabled && (makeHoleCommand == null || !makeHoleCommand.HasPendingHybridPattern);
            btnMakeHolePattern.Visible = HasTaskPaneMakeHolePattern;
            grpMakeHoleOptions.Visible = true;
            LayoutMakeHoleActions();
            LayoutModelCommandButtons();
        }

        private bool CanUseMakeHoleActions()
        {
            if (solidWorksClosing || swApp == null) return false;
            try
            {
                var model = swApp.ActiveDoc as ModelDoc2;
                if (model == null) return false;
                if (model.GetType() == (int)swDocumentTypes_e.swDocPART) return true;
                var assembly = model as AssemblyDoc;
                var part = assembly == null ? null : assembly.IGetEditTarget2() as ModelDoc2;
                return assembly != null && assembly.GetEditTargetComponent() != null && part != null &&
                    part.GetType() == (int)swDocumentTypes_e.swDocPART;
            }
            catch (System.Runtime.InteropServices.COMException) { return false; }
            catch (System.Runtime.InteropServices.InvalidComObjectException) { return false; }
        }

        private void LayoutMakeHoleActions()
        {
            if (!makeHoleActionsOnly || grpMakeHoleOptions == null || panelModelCommands == null) return;
            int width = Math.Max(240, panelModelCommands.ClientSize.Width - 12);
            int left = 10, gap = 8, innerWidth = width - left * 2;
            int buttonWidth = (innerWidth - gap) / 2;
            grpMakeHoleOptions.Width = width;
            // The row starts below the card title drawn by the theme.
            const int top = 36;
            btnMakeHoleUpdate.SetBounds(left, top, buttonWidth, 36);
            btnMakeHoleReset.SetBounds(left + buttonWidth + gap, top, buttonWidth, 36);
            int bottom = top + 36;
            if (nativeMakeHolePage == null)
            {
                lblMakeHolePitch.AutoSize = false;
                lblMakeHolePitch.SetBounds(left, bottom + 9, 96, 30);
                txtMakeHolePitch.SetBounds(left + 100, bottom + 9, innerWidth - 100, 30);
                bottom += 39;
            }
            if (HasTaskPaneMakeHolePattern)
            {
                btnMakeHolePattern.SetBounds(left, bottom + gap, buttonWidth, 36);
                bottom += gap + 36;
            }
            grpMakeHoleOptions.Height = bottom + 10;
        }

        private void LockOtherMakeHoleTaskPaneCommands(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                if (ReferenceEquals(control, btnMakeHoleUpdate) || ReferenceEquals(control, btnMakeHoleReset)) continue;
                if (control.Contains(btnMakeHoleUpdate) || control.Contains(btnMakeHoleReset))
                    LockOtherMakeHoleTaskPaneCommands(control);
                else
                {
                    makeHoleTaskPaneLocks[control] = control.Enabled;
                    control.Enabled = false;
                }
            }
        }

        private void RestoreMakeHoleTaskPaneCommands()
        {
            foreach (var entry in makeHoleTaskPaneLocks)
                if (!entry.Key.IsDisposed && !entry.Key.Disposing) entry.Key.Enabled = entry.Value;
            makeHoleTaskPaneLocks.Clear();
        }

        private void CapturePendingMakeHoleContext()
        {
            ClearPendingMakeHoleContext();
            makeHoleWaitingForWizardClose = true;
            pendingMakeHoleModel = swApp.ActiveDoc as ModelDoc2;
            var assembly = pendingMakeHoleModel as AssemblyDoc;
            pendingMakeHoleComponent = assembly == null ? null : assembly.GetEditTargetComponent();
            pendingMakeHolePart = assembly == null ? pendingMakeHoleModel : assembly.IGetEditTarget2() as ModelDoc2;
            pendingMakeHoleConfiguration = pendingMakeHolePart == null ? null : pendingMakeHolePart.ConfigurationManager.ActiveConfiguration.Name;
        }
        private void ClearPendingMakeHoleContext()
        {
            makeHolePatternQueued = false;
            makeHoleWaitingForWizardClose = false;
            pendingMakeHoleModel = null; pendingMakeHolePart = null; pendingMakeHoleComponent = null; pendingMakeHoleConfiguration = null;
        }
        private bool PendingMakeHoleContextMatches()
        {
            if (solidWorksClosing || swApp == null || pendingMakeHoleModel == null) return false;
            try
            {
                var current = swApp.ActiveDoc as ModelDoc2;
                if (current == null || swApp.IsSame(current, pendingMakeHoleModel) != (int)swObjectEquality.swObjectSame) return false;
                var assembly = current as AssemblyDoc;
                var part = assembly == null ? current : assembly.IGetEditTarget2() as ModelDoc2;
                var component = assembly == null ? null : assembly.GetEditTargetComponent();
                return part != null && swApp.IsSame(part, pendingMakeHolePart) == (int)swObjectEquality.swObjectSame &&
                    (pendingMakeHoleComponent == null ? component == null : swApp.IsSame(component, pendingMakeHoleComponent) == (int)swObjectEquality.swObjectSame) &&
                    pendingMakeHoleConfiguration == part.ConfigurationManager.ActiveConfiguration.Name;
            }
            catch (System.Runtime.InteropServices.COMException) { return false; }
            catch (System.Runtime.InteropServices.InvalidComObjectException) { return false; }
        }
        private bool MakeHolePatternUiIsFree()
        {
            int command; string title; bool uiActive;
            swApp.GetRunningCommandInfo(out command, out title, out uiActive);
            return !uiActive;
        }
        private int OnMakeHoleCommandCloseNotify(int command, int reason)
        {
            // 39 is the Hole Wizard command used by LenhMakeHole.RunCommand.
            // Do not read model geometry or open a page from a native command callback.
            if (!solidWorksClosing && command == 39 && makeHoleWaitingForWizardClose)
            {
                makeHoleWaitingForWizardClose = false;
                if (pendingMakeHoleModel != null && makeHoleCommand != null && makeHoleCommand.HasPendingHybridPattern)
                    makeHolePatternQueued = true;
            }
            return 0;
        }
        private int OnMakeHoleIdleNotify()
        {
            if (!makeHolePatternQueued || makeHolePatternPosted || solidWorksClosing || IsDisposed || Disposing || !IsHandleCreated || swApp == null) return 0;
            if (nativeMakeHolePage != null || nativeModelCommandPage != null || (autoSplinePage != null && autoSplinePage.IsOpen)) return 0;
            try
            {
                if (!MakeHolePatternUiIsFree()) return 0;
                makeHolePatternQueued = false;
                if (!PendingMakeHoleContextMatches()) return 0;
                makeHolePatternPosted = true;
                BeginInvoke((Action)(() =>
                {
                    makeHolePatternPosted = false;
                    if (!solidWorksClosing && !IsDisposed && !Disposing && PendingMakeHoleContextMatches()) RunPendingMakeHolePattern();
                }));
            }
            catch (Exception ex) { makeHolePatternQueued = false; System.Diagnostics.Debug.WriteLine("[MAKE HOLE PATTERN] " + ex.Message); }
            return 0;
        }
        // Hole Wizard has just closed. The seed hole is all the Pattern step needs, so it
        // runs here without a second confirmation. When it cannot start (Hole Wizard
        // cancelled, another command active) the step stays on the task pane's Pattern button.
        private void RunPendingMakeHolePattern()
        {
            if (solidWorksClosing || swApp == null || nativeMakeHolePage != null || nativeModelCommandPage != null ||
                (autoSplinePage != null && autoSplinePage.IsOpen) || makeHoleCommand == null || !makeHoleCommand.HasPendingHybridPattern) return;
            try
            {
                if (PendingMakeHoleContextMatches() && MakeHolePatternUiIsFree() && makeHoleCommand.CanContinuePendingHoleWizard())
                    ContinueNativeMakeHolePattern();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[MAKE HOLE PATTERN] " + ex.Message);
                MessageBox.Show(this, "Bước rải lỗ (Pattern) gặp lỗi:\n" + ex.Message +
                    "\n\nNếu lỗ chưa được rải, bấm Pattern bên Task Pane để thử lại hoặc Reset để hủy.",
                    "MAKE HOLE", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            ShowMakeHoleActions();
        }
        // The way back to a Pattern step that is still waiting: the user clicks Make hole again.
        private void ShowNativeMakeHolePatternPage()
        {
            if (solidWorksClosing || swApp == null || nativeMakeHolePage != null || nativeModelCommandPage != null ||
                (autoSplinePage != null && autoSplinePage.IsOpen) || makeHoleCommand == null || !makeHoleCommand.HasPendingHybridPattern) return;
            ShowMakeHoleActions();
            try
            {
                if (!PendingMakeHoleContextMatches() || !MakeHolePatternUiIsFree() || !makeHoleCommand.CanContinuePendingHoleWizard())
                {
                    swApp.SendMsgToUser2("MAKE HOLE đang chờ lỗ mẫu: xác nhận Hole Wizard (✓) là lỗ được rải ngay.\nĐã hủy Hole Wizard thì bấm Reset bên Task Pane rồi chạy lại.",
                        (int)swMessageBoxIcon_e.swMbInformation, (int)swMessageBoxBtn_e.swMbOk);
                    return;
                }
                nativeMakeHolePage = new MakeHolePropertyManagerPage(swApp, null, null, null, null,
                    closedPage =>
                    {
                        if (ReferenceEquals(nativeMakeHolePage, closedPage)) nativeMakeHolePage = null;
                        RestoreMakeHoleTaskPaneCommands();
                        if (!IsDisposed && !Disposing && !solidWorksClosing) { SetMakeHoleUpdateButtonState(false); ShowMakeHoleActions(); }
                    }, null, null, ResetNativeMakeHole, SetMakeHoleUpdateButtonState,
                    ContinueNativeMakeHolePattern,
                    () => PendingMakeHoleContextMatches() && makeHoleCommand != null && makeHoleCommand.HasPendingHybridPattern,
                    makeHoleCommand.PendingPatternSummary);
                nativeMakeHolePage.Show();
                if (nativeMakeHolePage != null && nativeMakeHolePage.IsOpen) { ShowMakeHoleActions(); LockOtherMakeHoleTaskPaneCommands(this); }
            }
            catch (Exception ex) { DismissNativeMakeHolePage(); System.Diagnostics.Debug.WriteLine("[MAKE HOLE PATTERN] " + ex.Message); }
        }
        private void ContinueNativeMakeHolePattern()
        {
            if (!PendingMakeHoleContextMatches()) return;
            btnMakeHolePattern_Click(this, EventArgs.Empty);
        }

        private void DismissNativeMakeHolePage()
        {
            makeHolePatternQueued = false;
            if (solidWorksClosing) ClearPendingMakeHoleContext();
            var current = nativeMakeHolePage;
            nativeMakeHolePage = null;
            if (current != null) current.Dismiss();
        }
    }
}
