using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using static ADDIN.Commands.CircleFaceTubeLayout.TubeLayoutSolver;

namespace ADDIN.Commands.CircleFaceTubeLayout
{
    internal sealed class CircleFaceTubeLayoutForm : Form
    {
        private readonly NumericUpDown diameter = Number(20, 1, 500, 2);
        private readonly NumericUpDown quantity = Number(40, 2, MaximumCount - 1, 0);
        private readonly NumericUpDown margin = Number(5, 0, 1000, 2);
        private readonly CheckBox autoCount = new CheckBox { Text = "Auto count", Checked = true, AutoSize = true };
        private readonly CheckBox addOne = new CheckBox { Text = "Add +1", Checked = true, AutoSize = true };
        private readonly CheckBox flip = new CheckBox { Text = "Flip Side", AutoSize = true };
        private readonly CheckBox allowOverhang = new CheckBox
        { Text = "Allow symmetric overhang (V3)", AutoSize = true, Checked = false };
        private readonly NumericUpDown maxOverhang = Number(12, 0, 40, 2);
        private readonly ComboBox smallMode = new ComboBox { Width = 195, DropDownStyle = ComboBoxStyle.DropDownList };
        private SmallEndSpacingMode SmallMode => smallMode.SelectedIndex == 1
            ? SmallEndSpacingMode.EqualPitch : SmallEndSpacingMode.TangentContact;
        private readonly Label total = new Label { AutoSize = true };
        private readonly TextBox report = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            TabStop = false,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            BackColor = SystemColors.Control,
            Margin = new Padding(0, 8, 0, 4)
        };
        private readonly Button create = new Button { Text = "Create 3D Sketch", AutoSize = true, Enabled = false };
        private readonly Timer refresh = new Timer { Interval = 350 };
        private readonly Func<int?, double, double, bool, SmallEndSpacingMode, bool, double, TubeLayout> analyze;
        private bool OverhangActive => !autoCount.Checked &&
            SmallMode == SmallEndSpacingMode.EqualPitch && allowOverhang.Checked;
        private readonly Action clearPreview;
        private readonly Action<string> logLine;
        private readonly BoundaryRails rails;
        private bool previewed;
        private bool previewInProgress;
        private bool updatingControls;
        private int previewVersion;
        public TubeLayout Accepted { get; private set; }
        private int? Count => autoCount.Checked ? (int?)null : (int)quantity.Value + (addOne.Checked ? 1 : 0);

        public CircleFaceTubeLayoutForm(string logPath, BoundaryRails rails,
            Func<int?, double, double, bool, SmallEndSpacingMode, bool, double, TubeLayout> analyze,
            Action clearPreview = null, Action<string> logLine = null)
        {
            this.rails = rails;
            this.analyze = analyze;
            this.clearPreview = clearPreview;
            this.logLine = logLine;
            Text = "Circle Along Face";
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(560, 480);
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MaximizeBox = MinimizeBox = false;

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 11 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
            for (int i = 0; i < 9; i++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(grid);

            Row(grid, 0, "Tube outside diameter (mm)", diameter);
            Row(grid, 1, "Base count", quantity);
            Row(grid, 2, "Both-end edge margin (mm)", margin);
            smallMode.Items.AddRange(new object[] { "Touch at small end (existing)", "Equal pitch at both ends" });
            smallMode.SelectedIndex = 0;
            grid.Controls.Add(addOne, 0, 3);
            grid.Controls.Add(flip, 1, 3);
            grid.Controls.Add(autoCount, 0, 4);
            grid.Controls.Add(total, 1, 4);

            Row(grid, 5, "Small-end mode (Custom)", smallMode);
            grid.Controls.Add(allowOverhang, 0, 6);
            Row(grid, 7, "Max cap overhang (mm)", maxOverhang);
            var legend = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            legend.Controls.Add(new Label { Text = "● Small end", ForeColor = Color.DeepSkyBlue, AutoSize = true });
            legend.Controls.Add(new Label { Text = "● Large end", ForeColor = Color.DarkGoldenrod, AutoSize = true });
            legend.Controls.Add(new Label { Text = "● Verified axes", ForeColor = Color.ForestGreen, AutoSize = true });
            grid.Controls.Add(legend, 0, 8);
            grid.SetColumnSpan(legend, 2);
            grid.Controls.Add(report, 0, 9);
            grid.SetColumnSpan(report, 2);

            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            var preview = new Button { Text = "Preview", AutoSize = true };
            var debug = new Button { Text = "Open Debug Log", AutoSize = true };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            buttons.Controls.AddRange(new Control[] { preview, create, debug, cancel });
            grid.Controls.Add(buttons, 0, 10);
            grid.SetColumnSpan(buttons, 2);
            CancelButton = cancel;
            AcceptButton = preview;

            preview.Click += (sender, args) => Preview();
            create.Click += (sender, args) =>
            {
                if (Accepted != null && Accepted.Issue == null) DialogResult = DialogResult.OK;
            };
            debug.Click += (sender, args) =>
            {
                try { Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
            };
            refresh.Tick += (sender, args) => Preview();
            foreach (NumericUpDown control in new[] { diameter, quantity, margin, maxOverhang })
            {
                control.ValueChanged += (sender, args) => InvalidatePreview();
                control.TextChanged += (sender, args) => InvalidatePendingText();
            }
            foreach (CheckBox control in new[] { autoCount, addOne, flip, allowOverhang })
                control.CheckedChanged += (sender, args) => InvalidatePreview();
            smallMode.SelectedIndexChanged += (sender, args) => InvalidatePreview();
            FormClosed += (sender, args) => refresh.Stop();
            InvalidatePreview();
        }

        public static bool ShowCreated(IWin32Window owner, string name, int count, string logStatus)
        {
            using (var done = new Form
            {
                Text = "Circle Along Face",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                Font = SystemFonts.MessageBoxFont
            })
            {
                var content = new FlowLayoutPanel
                {
                    AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12)
                };
                content.Controls.Add(new Label { Text = name + "\n" + count + " construction axes created." + logStatus, AutoSize = true });
                var buttons = new FlowLayoutPanel { AutoSize = true };
                var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
                buttons.Controls.Add(close);
                buttons.Controls.Add(new Button { Text = "Undo Sketch", AutoSize = true, DialogResult = DialogResult.Retry });
                content.Controls.Add(buttons);
                done.Controls.Add(content);
                done.AcceptButton = close;
                return done.ShowDialog(owner) == DialogResult.Retry;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Rectangle screen = Screen.FromHandle(Owner?.Handle ?? Handle).WorkingArea;
            Location = new Point(Math.Max(screen.Left, screen.Right - Width - 24), screen.Top + 80);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) refresh.Dispose();
            base.Dispose(disposing);
        }

        private void InvalidatePendingText()
        {
            if (IsDisposed || Disposing) return;
            previewVersion++;
            Accepted = null;
            create.Enabled = false;
            if (previewed) clearPreview?.Invoke();
        }

        private void InvalidatePreview()
        {
            if (IsDisposed || Disposing || updatingControls) return;
            updatingControls = true;
            try
            {
                previewVersion++;
                Accepted = null;
                create.Enabled = false;
                if (previewed) clearPreview?.Invoke();
                quantity.Enabled = addOne.Enabled = smallMode.Enabled = !autoCount.Checked;
                allowOverhang.Enabled = !autoCount.Checked && SmallMode == SmallEndSpacingMode.EqualPitch;
                maxOverhang.Enabled = OverhangActive;
                if (maxOverhang.Maximum != diameter.Value)
                    maxOverhang.Maximum = diameter.Value; // Center extrapolation stays bounded.

                total.Text = Count.HasValue
                    ? F("Total axes: {0} ({1}{2}) · {3}", Count.Value, (int)quantity.Value,
                        addOne.Checked ? "+1" : "", SmallMode == SmallEndSpacingMode.EqualPitch ? "EqualPitch" : "Touch")
                    : "Total axes: Auto (small end touching)";
                report.Text = RailsText() + Environment.NewLine +
                    (previewed ? "Updating preview…" : "Choose Preview to verify the layout.");
                if (!previewed) return;
                refresh.Stop();
                refresh.Start();
            }
            finally { updatingControls = false; }
        }

        private string RailsText() => F("Small {0:F3} mm    Large {1:F3} mm", rails.Small.Length / Mm, rails.Large.Length / Mm);

        private void Preview()
        {
            if (IsDisposed || Disposing || previewInProgress) return;
            refresh.Stop();
            int version = ++previewVersion;
            previewInProgress = true;
            previewed = true;
            Accepted = null;
            create.Enabled = false;
            UseWaitCursor = true;
            report.Text = RailsText() + Environment.NewLine + "Checking 3D contact, full-length clearance, and sheet BREP…";
            report.Refresh();
            try
            {
                var mode = autoCount.Checked ? SmallEndSpacingMode.TangentContact : SmallMode;
                bool activeOverhang = OverhangActive;
                logLine?.Invoke(F("UI_INPUT auto={0} base_count={1} add_one={2} effective_count={3} selected_mode={4} effective_mode={5} diameter_mm={6:F3} margin_mm={7:F3} flip={8} allow_overhang={9} max_cap_overhang_mm={10:F3}",
                    autoCount.Checked, (int)quantity.Value, addOne.Checked, Count?.ToString() ?? "auto",
                    SmallMode, mode, (double)diameter.Value, (double)margin.Value, flip.Checked,
                    activeOverhang, (double)maxOverhang.Value));
                var result = analyze(Count, (double)diameter.Value * Mm, (double)margin.Value * Mm,
                    flip.Checked, mode, activeOverhang, (double)maxOverhang.Value * Mm);
                if (IsDisposed || Disposing || version != previewVersion) return;
                string smallLine = result.SmallMode == SmallEndSpacingMode.EqualPitch
                    ? F("Small end: EqualPitch · pitch {0:F3} mm · cap margins L={1:F3} / R={2:F3} mm",
                        result.SmallPitch / Mm, result.SmallLeftClearance / Mm, result.SmallRightClearance / Mm)
                    : "Small end: Touching mode";
                string largeLine = F("Large end: pitch {0:F3} mm · cap margins L={1:F3} / R={2:F3} mm",
                    result.LargePitch / Mm, result.LargeLeftClearance / Mm, result.LargeRightClearance / Mm);
                string pairTag = result.MinimumGapPairA > 0
                    ? F(" (pair {0}-{1})", result.MinimumGapPairA, result.MinimumGapPairB)
                    : "";
                string interferenceTag = result.InterferingPairCount > 0
                    ? F(" · interfering={0} (first {1}-{2})", result.InterferingPairCount, result.FirstInterferingPairA, result.FirstInterferingPairB)
                    : "";
                string gapsLine = F("Adj small 3D gaps: {0:F4}–{1:F4} mm · Min full 3D gap: {2:F4} mm{3}{4}",
                    result.MinimumContact / Mm, result.MaximumContact / Mm, result.MinimumGap / Mm, pairTag, interferenceTag);
                bool sheetChecked = !double.IsNaN(result.MinimumSheetEstimate);
                string brepStatus = !sheetChecked
                    ? "Skipped (geometry invalid)"
                    : result.SheetClashCount == 0 && result.SheetInconclusiveCount == 0
                        ? "Clear"
                        : F("clashes={0}, inconclusive={1}", result.SheetClashCount, result.SheetInconclusiveCount);
                string sheetLine = sheetChecked
                    ? F("Sheet clearance: min {0:F3} mm · BREP: {1} · lift {2:F3} mm",
                        result.MinimumSheetEstimate / Mm, brepStatus, result.Lift / Mm)
                    : F("Sheet clearance: n/a (geometry invalid) · BREP: {0} · lift {1:F3} mm",
                        brepStatus, result.Lift / Mm);
                string overhangLine = activeOverhang
                    ? (result.OverhangSearchAttempts == 0 && result.Issue == null
                        ? F("Smart Overhang: baseline margin {0:F3} mm preserved (cap protrusion 0.000 mm; centers on selected rails)",
                            result.EffectiveCapMargin / Mm)
                        : F("Smart Overhang: cap protrusion {0:F3} mm each side; center extension max {1:F3} mm; attempts {2}; {3}",
                            result.AppliedOverhang / Mm, result.MaximumCenterExtension / Mm,
                            result.OverhangSearchAttempts,
                            result.CenterExtrapolated ? "EXTRAPOLATED center outside selected face" : "centers on selected rails"))
                    : "Smart Overhang: OFF";
                string statusLine = result.Issue == null
                    ? "OK: Verified 3D separation for every pair and the sheet."
                    : "INFEASIBLE: " + result.Issue;

                report.Text = string.Join(Environment.NewLine,
                    RailsText(), smallLine, largeLine, overhangLine, gapsLine, sheetLine, statusLine);
                total.Text = autoCount.Checked
                    ? F("Total axes: {0} (Auto{1})", result.Axes.Length, result.Axes.Length == MaximumCount ? ", limit" : "")
                    : F("Total axes: {0} ({1}{2}) · {3}", result.Axes.Length, (int)quantity.Value,
                        addOne.Checked ? "+1" : "", result.SmallMode == SmallEndSpacingMode.EqualPitch ? "EqualPitch" : "Touch");
                Accepted = result;
                create.Enabled = result.Issue == null;
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing)
                    report.Text = RailsText() + Environment.NewLine + ex.Message;
            }
            finally
            {
                previewInProgress = false;
                if (!IsDisposed && !Disposing) UseWaitCursor = false;
            }
        }

        private static NumericUpDown Number(decimal value, decimal min, decimal max, int places) =>
            new NumericUpDown { Value = value, Minimum = min, Maximum = max, DecimalPlaces = places, Width = 110 };

        private static void Row(TableLayoutPanel grid, int row, string name, Control input)
        {
            grid.Controls.Add(new Label { Text = name, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            grid.Controls.Add(input, 1, row);
        }
    }
}
