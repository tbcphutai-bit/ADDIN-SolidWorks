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
        private readonly NumericUpDown quantity = Number(40, 2, 199, 0);
        private readonly NumericUpDown margin = Number(5, 0, 1000, 2);
        private readonly CheckBox addOne = new CheckBox { Text = "Add +1", Checked = true, AutoSize = true };
        private readonly CheckBox flip = new CheckBox { Text = "Flip Side", AutoSize = true };
        private readonly Label total = new Label { AutoSize = true };
        private readonly Label report = new Label { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
        private readonly Button create = new Button { Text = "Create 3D Sketch", AutoSize = true, Enabled = false };
        private readonly Timer refresh = new Timer { Interval = 350 };
        private readonly Func<int, double, double, bool, TubeLayout> analyze;
        private readonly BoundaryRails rails;
        private bool previewed;
        public TubeLayout Accepted { get; private set; }
        private int Count => (int)quantity.Value + (addOne.Checked ? 1 : 0);

        public CircleFaceTubeLayoutForm(string logPath, BoundaryRails rails, Func<int, double, double, bool, TubeLayout> analyze)
        {
            this.rails = rails;
            this.analyze = analyze;
            Text = "Circle Along Face";
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(430, 308);
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            MaximizeBox = MinimizeBox = false;

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 8 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            for (int i = 0; i < 6; i++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(grid);

            Row(grid, 0, "Tube outside diameter (mm)", diameter);
            Row(grid, 1, "Base count", quantity);
            Row(grid, 2, "Large-end edge margin (mm)", margin);
            grid.Controls.Add(addOne, 0, 3);
            grid.Controls.Add(flip, 1, 3);
            grid.Controls.Add(total, 0, 4);

            var legend = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            legend.Controls.Add(new Label { Text = "● Small end", ForeColor = Color.DeepSkyBlue, AutoSize = true });
            legend.Controls.Add(new Label { Text = "● Large end", ForeColor = Color.DarkGoldenrod, AutoSize = true });
            legend.Controls.Add(new Label { Text = "● Verified axes", ForeColor = Color.ForestGreen, AutoSize = true });
            grid.Controls.Add(legend, 0, 5);
            grid.SetColumnSpan(legend, 2);
            grid.Controls.Add(report, 0, 6);
            grid.SetColumnSpan(report, 2);

            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            var preview = new Button { Text = "Preview", AutoSize = true };
            var debug = new Button { Text = "Open Debug Log", AutoSize = true };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            buttons.Controls.AddRange(new Control[] { preview, create, debug, cancel });
            grid.Controls.Add(buttons, 0, 7);
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
            foreach (NumericUpDown control in new[] { diameter, quantity, margin })
                control.ValueChanged += (sender, args) => InvalidatePreview();
            foreach (CheckBox control in new[] { addOne, flip })
                control.CheckedChanged += (sender, args) => InvalidatePreview();
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

        private void InvalidatePreview()
        {
            Accepted = null;
            create.Enabled = false;
            total.Text = "Total axes: " + Count;
            report.Text = RailsText() + (previewed ? "\nUpdating preview…" : "\nChoose Preview to verify the layout.");
            if (!previewed) return;
            refresh.Stop();
            refresh.Start();
        }

        private string RailsText() => F("Small {0:F3} mm    Large {1:F3} mm", rails.Small.Length / Mm, rails.Large.Length / Mm);

        private void Preview()
        {
            refresh.Stop();
            previewed = true;
            Accepted = null;
            create.Enabled = false;
            UseWaitCursor = true;
            report.Text = RailsText() + "\nChecking 3D contact and sheet clearance…";
            report.Refresh();
            try
            {
                var result = analyze(Count, (double)diameter.Value * Mm, (double)margin.Value * Mm, flip.Checked);
                report.Text = RailsText() + F("\nLarge-end pitch {0:F3} mm  ·  lift {1:F3} mm\nSmall-end 3D gap {2:F4}–{3:F4} mm\nLarge cap margins {4:F3} / {5:F3} mm\n",
                    result.LargePitch / Mm, result.Lift / Mm, result.MinimumContact / Mm, result.MaximumContact / Mm, result.LargeLeftClearance / Mm, result.LargeRightClearance / Mm) +
                    (result.Issue == null ? "Verified separation for every pair and the sheet." : "INFEASIBLE: " + result.Issue);
                Accepted = result;
                create.Enabled = result.Issue == null;
            }
            catch (Exception ex)
            {
                report.Text = RailsText() + "\n" + ex.Message;
            }
            finally { UseWaitCursor = false; }
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
