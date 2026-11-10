// Prototype UI; currently NOT wired to the existing TaskPane.
// SolidWorks 2024 / .NET Framework 4.8 / C# 7.3.
using System;
using System.Drawing;
using System.Windows.Forms;
namespace ADDIN.Commands
{
    public sealed class CircleFaceTubeLayoutForm : Form
    {
        private readonly NumericUpDown diameter = Num(20, 1, 500, 2);
        private readonly NumericUpDown baseCount = Num(40, 2, 500, 0);
        private readonly NumericUpDown margin = Num(5, 0, 1000, 2);
        private readonly CheckBox addOne = new CheckBox { Text = "Add +1 tube", Checked = true, AutoSize = true };
        private readonly CheckBox flip = new CheckBox { Text = "Flip face side", AutoSize = true };
        private readonly Label total = new Label { AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        public double DiameterMm { get { return (double)diameter.Value; } }
        public int TotalCount { get { return (int)baseCount.Value + (addOne.Checked ? 1 : 0); } }
        public double LargeEndMarginMm { get { return (double)margin.Value; } }
        public bool FlipSide { get { return flip.Checked; } }
        public CircleFaceTubeLayoutForm()
        {
            Text = "Circle Along Face - 3D Sketch PROTOTYPE";
            Font = new Font("Segoe UI", 9);
            Width = 480; Height = 350; MaximizeBox = false; MinimizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(15), ColumnCount = 2, RowCount = 8 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
            Controls.Add(grid);
            var title = new Label { Text = "Select 1 face -> Preview -> 3D Sketch axes", AutoSize = true,
                Font = new Font("Segoe UI", 10, FontStyle.Bold) };
            grid.Controls.Add(title, 0, 0); grid.SetColumnSpan(title, 2);
            Row(grid, 1, "Tube diameter (mm)", diameter);
            Row(grid, 2, "Base quantity", baseCount);
            grid.Controls.Add(addOne, 0, 3); grid.SetColumnSpan(addOne, 2);
            grid.Controls.Add(total, 0, 4); grid.SetColumnSpan(total, 2);
            Row(grid, 5, "Large end clearance (mm)", margin);
            grid.Controls.Add(flip, 0, 6); grid.SetColumnSpan(flip, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
            buttons.Controls.Add(new Button { Text = "Analyze / Preview", Width = 145, DialogResult = DialogResult.OK });
            buttons.Controls.Add(new Button { Text = "Cancel", Width = 85, DialogResult = DialogResult.Cancel });
            grid.Controls.Add(buttons, 0, 7); grid.SetColumnSpan(buttons, 2);
            AcceptButton = (Button)buttons.Controls[0];
            CancelButton = (Button)buttons.Controls[1];
            addOne.CheckedChanged += (sender, args) => UpdateCount();
            baseCount.ValueChanged += (sender, args) => UpdateCount();
            UpdateCount();
        }
        private void UpdateCount() { total.Text = "Total tube axes: " + TotalCount; }
        private static NumericUpDown Num(decimal val, decimal min, decimal max, int decimals)
        {
            return new NumericUpDown { Value = val, Minimum = min, Maximum = max,
                DecimalPlaces = decimals, Increment = decimals > 0 ? .5m : 1m, Width = 145 };
        }
        private static void Row(TableLayoutPanel panel, int index, string name, Control input)
        {
            panel.Controls.Add(new Label { Text = name, AutoSize = true, Anchor = AnchorStyles.Left }, 0, index);
            panel.Controls.Add(input, 1, index);
        }
    }
}
