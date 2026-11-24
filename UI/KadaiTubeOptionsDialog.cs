using System;
using System.Drawing;
using System.Windows.Forms;
using ADDIN.Commands.CircleFaceTubeLayout;
using ADDIN.Commands.Kadai;

namespace ADDIN.UI
{
    internal sealed class KadaiTubeOptionsDialog : Form
    {
        private readonly CheckBox useTubes = new CheckBox { Text = "Tạo ống và cắt gân", AutoSize = true };
        private readonly CheckBox autoCount = new CheckBox { Text = "Tự tính số ống", AutoSize = true };
        private readonly ComboBox spacing = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly NumericUpDown count = Number(2, 150, 24, 0);
        private readonly NumericUpDown diameter = Number(2.1m, 500, 20, 2);
        private readonly NumericUpDown wall = Number(.1m, 100, 2.6m, 2);
        private readonly NumericUpDown margin = Number(0, 500, 4, 2);
        private readonly NumericUpDown clearance = Number(0, 25, .1m, 2);

        internal KadaiTubeSettings Settings { get; private set; }

        internal KadaiTubeOptionsDialog(KadaiTubeSettings current)
        {
            Text = "Kadai · Tạo ống và cắt gân";
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(435, 345);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ShowInTaskbar = false;
            MaximizeBox = MinimizeBox = false;
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 49));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 51));
            Controls.Add(grid);
            useTubes.Checked = current != null;
            autoCount.Checked = current?.Count == null;
            spacing.Items.AddRange(new object[] { "Tiếp xúc ở đầu nhỏ", "Chia đều ở hai đầu" });
            spacing.SelectedIndex = current?.Spacing == SmallEndSpacingMode.EqualPitch ? 1 : 0;
            if (current != null)
            {
                count.Value = current.Count ?? 24;
                diameter.Value = (decimal)(current.Diameter * 1000);
                wall.Value = (decimal)(current.Wall * 1000);
                margin.Value = (decimal)(current.Margin * 1000);
                clearance.Value = (decimal)(current.SeatClearance * 1000);
            }
            AddWide(grid, useTubes);
            AddWide(grid, new Label { Text = "Ống nằm giữa panel và các gân theo phương bám panel.", AutoSize = true });
            AddWide(grid, autoCount);
            AddRow(grid, "Cách bố trí", spacing);
            AddRow(grid, "Số ống", count);
            AddRow(grid, "Đường kính ngoài (mm)", diameter);
            AddRow(grid, "Chiều dày thành ống (mm)", wall);
            AddRow(grid, "Chừa hai đầu (mm)", margin);
            AddRow(grid, "Chừa thêm bán kính cắt (mm)", clearance);
            var actions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            var accept = new Button { Text = "OK", AutoSize = true };
            var cancel = new Button { Text = "Hủy", AutoSize = true, DialogResult = DialogResult.Cancel };
            actions.Controls.AddRange(new Control[] { accept, cancel });
            AddWide(grid, actions);
            AcceptButton = accept;
            CancelButton = cancel;

            void RefreshInputs()
            {
                autoCount.Enabled = diameter.Enabled = wall.Enabled = margin.Enabled = clearance.Enabled = useTubes.Checked;
                count.Enabled = spacing.Enabled = useTubes.Checked && !autoCount.Checked;
                if (autoCount.Checked) spacing.SelectedIndex = 0;
            }
            useTubes.CheckedChanged += (sender, args) => RefreshInputs();
            autoCount.CheckedChanged += (sender, args) => RefreshInputs();
            accept.Click += (sender, args) =>
            {
                try
                {
                    Settings = useTubes.Checked
                        ? new KadaiTubeSettings(autoCount.Checked ? null : (int?)count.Value, spacing.SelectedIndex == 1
                            ? SmallEndSpacingMode.EqualPitch : SmallEndSpacingMode.TangentContact,
                            (double)diameter.Value, (double)wall.Value, (double)margin.Value, (double)clearance.Value)
                        : null;
                    DialogResult = DialogResult.OK;
                }
                catch (ArgumentException ex)
                {
                    MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            RefreshInputs();
        }

        private static NumericUpDown Number(decimal min, decimal max, decimal value, int decimals) =>
            new NumericUpDown { Minimum = min, Maximum = max, Value = value, DecimalPlaces = decimals, Dock = DockStyle.Fill };

        private static void AddWide(TableLayoutPanel grid, Control control)
        {
            int row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(control, 0, row);
            grid.SetColumnSpan(control, 2);
        }

        private static void AddRow(TableLayoutPanel grid, string label, Control control)
        {
            int row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(new Label { Text = label, Anchor = AnchorStyles.Left, AutoSize = true }, 0, row);
            grid.Controls.Add(control, 1, row);
        }
    }
}
