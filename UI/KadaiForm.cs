// KADAI P0 UI - parameter layout only. It does NOT generate or modify CAD.
using System;
using System.Drawing;
using System.Windows.Forms;
using ADDIN.Commands.Kadai;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.UI
{
    internal sealed class KadaiForm : Form
    {
        private readonly ISldWorks app;
        private readonly ComboBox preset = new ComboBox();
        private readonly NumericUpDown countL = Number(1, 100, 7, 0);
        private readonly NumericUpDown countC = Number(1, 100, 4, 0);
        private readonly NumericUpDown extentX = Number(1, 99999, 2250, 1);
        private readonly NumericUpDown extentZ = Number(1, 99999, 1450, 1);
        private readonly NumericUpDown marginL = Number(0, 99999, 50, 1);
        private readonly NumericUpDown marginC = Number(0, 99999, 150.9m, 1);
        private readonly NumericUpDown thickL = Number(.1m, 30, 1.6m, 2);
        private readonly NumericUpDown thickC = Number(.1m, 30, 1.6m, 2);
        private readonly NumericUpDown clearanceL = Number(0, 5, .2m, 2);
        private readonly NumericUpDown clearanceC = Number(0, 5, .2m, 2);
        private readonly NumericUpDown gap = Number(0, 100, 0, 2);
        private readonly TextBox report = new TextBox();
        private readonly Panel map = new Panel();
        private KadaiPlan plan;
        private bool loading;

        internal KadaiForm(ISldWorks app)
        {
            this.app = app;
            Text = "Kadai | Panel Rib Generator (P0 layout)";
            Width = 650; Height = 760; MinimumSize = new Size(540, 610);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true,
                ColumnCount = 2, Padding = new Padding(12) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            preset.DropDownStyle = ComboBoxStyle.DropDownList;
            preset.Items.AddRange(new object[] { "KDA1", "KF3" });
            Add(top, "Mẫu hình học tham khảo", preset);
            Add(top, "Số thanh dọc", countL);
            Add(top, "Số thanh ngang", countC);
            Add(top, "Chiều dài bố trí X (mm)", extentX);
            Add(top, "Chiều dài bố trí Z (mm)", extentZ);
            Add(top, "Lề dọc (mm)", marginL);
            Add(top, "Lề ngang (mm)", marginC);
            Add(top, "Dày thanh dọc (mm)", thickL);
            Add(top, "Dày thanh ngang (mm)", thickC);
            Add(top, "Độ hở tổng rãnh thanh dọc (mm)", clearanceL);
            Add(top, "Độ hở tổng rãnh thanh ngang (mm)", clearanceC);
            Add(top, "Gap đến panel (mm)", gap);

            map.Dock = DockStyle.Top; map.Height = 150; map.BackColor = Color.White;
            map.Paint += DrawLayout;
            report.Multiline = true; report.ReadOnly = true; report.ScrollBars = ScrollBars.Vertical;
            report.Dock = DockStyle.Fill; report.Font = new Font("Consolas", 9f);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 43,
                FlowDirection = FlowDirection.RightToLeft };
            var apply = new Button { Text = "Apply 3D (disabled)", Width = 170, Enabled = false };
            var calculate = new Button { Text = "Tính layout", Width = 125 };
            calculate.Click += (s, e) => RefreshPlan();
            buttons.Controls.Add(apply); buttons.Controls.Add(calculate);

            var container = new Panel { Dock = DockStyle.Fill };
            container.Controls.Add(report); container.Controls.Add(map); container.Controls.Add(top);
            Controls.Add(container); Controls.Add(buttons);
            preset.SelectedIndexChanged += (s, e) => { if (!loading) LoadPreset(preset.Text); };
            preset.SelectedIndex = 0;
            LoadPreset("KDA1");
        }

        private static NumericUpDown Number(decimal min, decimal max, decimal value, int digits)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Value = value,
                DecimalPlaces = digits, Increment = digits == 0 ? 1 : .1m,
                Dock = DockStyle.Fill, ThousandsSeparator = true };
        }
        private static void Add(TableLayoutPanel host, string title, Control input)
        {
            int row = host.RowCount++;
            host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            host.Controls.Add(new Label { Text = title, AutoSize = true,
                Dock = DockStyle.Fill, Margin = new Padding(0, 5, 2, 5) }, 0, row);
            host.Controls.Add(input, 1, row);
        }
        private void LoadPreset(string name)
        {
            loading = true;
            try
            {
                var p = KadaiRequest.ForPreset(name);
                countL.Value = p.LongRibCount; countC.Value = p.CrossRibCount;
                extentX.Value = (decimal)p.LengthXmm; extentZ.Value = (decimal)p.LengthZmm;
                marginL.Value = (decimal)p.LongEndMarginMm;
                marginC.Value = (decimal)p.CrossEndMarginMm;
                thickL.Value = (decimal)p.LongThicknessMm; thickC.Value = (decimal)p.CrossThicknessMm;
                clearanceL.Value = (decimal)p.LongSlotExtraMm;
                clearanceC.Value = (decimal)p.CrossSlotExtraMm;
                gap.Value = (decimal)p.GapToPanelMm;
            }
            finally { loading = false; }
            RefreshPlan();
        }
        private KadaiRequest ReadSettings()
        {
            return new KadaiRequest
            {
                Preset = preset.Text,
                LengthXmm = (double)extentX.Value, LengthZmm = (double)extentZ.Value,
                LongRibCount = (int)countL.Value, CrossRibCount = (int)countC.Value,
                LongEndMarginMm = (double)marginL.Value, CrossEndMarginMm = (double)marginC.Value,
                LongThicknessMm = (double)thickL.Value, CrossThicknessMm = (double)thickC.Value,
                LongSlotExtraMm = (double)clearanceL.Value, CrossSlotExtraMm = (double)clearanceC.Value,
                GapToPanelMm = (double)gap.Value
            };
        }
        private void RefreshPlan()
        {
            try
            {
                if (app == null) throw new InvalidOperationException("Chưa kết nối SOLIDWORKS.");
                var doc = app.ActiveDoc as ModelDoc2;
                if (doc == null || (doc.GetType() != (int)swDocumentTypes_e.swDocPART
                    && doc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY))
                    throw new InvalidOperationException("Mở một Part hoặc Assembly.");
                plan = KadaiPlanner.Build(ReadSettings());
                report.Text = "Document: " + doc.GetTitle() + Environment.NewLine
                    + plan.ToReport() + Environment.NewLine
                    + "P1+: choose panel faces, create real 3D preview, native Sheet Metal/Cut/FlatPattern/Drawing.";
                map.Invalidate();
            }
            catch (Exception ex)
            {
                plan = null; report.Text = "LAYOUT ERROR: " + ex.Message; map.Invalidate();
            }
        }
        private void DrawLayout(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(Color.White);
            if (plan == null) return;
            var rect = new Rectangle(25, 14, Math.Max(10, map.ClientSize.Width - 50),
                Math.Max(10, map.ClientSize.Height - 28));
            using (var outer = new Pen(Color.SteelBlue, 2f))
            using (var longitudinal = new Pen(Color.SeaGreen, 2f))
            using (var transverse = new Pen(Color.IndianRed, 2f))
            {
                e.Graphics.DrawRectangle(outer, rect);
                foreach (var rib in plan.LongRibs)
                {
                    int x = rect.Left + (int)Math.Round(rib.NormalizedPosition * rect.Width);
                    e.Graphics.DrawLine(longitudinal, x, rect.Top, x, rect.Bottom);
                }
                foreach (var rib in plan.CrossRibs)
                {
                    int y = rect.Top + (int)Math.Round(rib.NormalizedPosition * rect.Height);
                    e.Graphics.DrawLine(transverse, rect.Left, y, rect.Right, y);
                }
            }
        }
    }
}
