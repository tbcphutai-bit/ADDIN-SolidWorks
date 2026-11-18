// Kadai layout and measured temporary-body preview. Production Apply stays gated.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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
        private readonly ComboBox heightAxis = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly NumericUpDown originU = Number(-99999, 99999, 0, 2);
        private readonly NumericUpDown originV = Number(-99999, 99999, 0, 2);
        private readonly NumericUpDown bottom = Number(-99999, 99999, 0, 2);
        private readonly NumericUpDown seam = Number(.1m, 99999, 100, 1);
        private readonly NumericUpDown depthClearance = Number(.01m, 20, .1m, 2);
        private readonly KadaiPreviewBackend backend;
        private readonly Timer lifecycle = new Timer { Interval = 300 };
        private KadaiPanelSource source;
        private readonly TextBox report = new TextBox();
        private readonly Panel map = new Panel();
        private KadaiPlan plan;
        private KadaiPlan lastPreviewPlan;
        private bool loading;

        internal KadaiForm(ISldWorks app)
        {
            this.app = app;
            backend = new KadaiPreviewBackend(app);
            Text = "Kadai | Panel Rib Generator — 3D preview";
            Width = 760; Height = 700; MinimumSize = new Size(650, 610);
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
            Add(top, "Span bố trí U (mm)", extentX);
            Add(top, "Span bố trí V (mm)", extentZ);
            Add(top, "Lề dọc (mm)", marginL);
            Add(top, "Lề ngang (mm)", marginC);
            Add(top, "Dày thanh dọc (mm)", thickL);
            Add(top, "Dày thanh ngang (mm)", thickC);
            Add(top, "Độ hở tổng rãnh thanh dọc (mm)", clearanceL);
            Add(top, "Độ hở tổng rãnh thanh ngang (mm)", clearanceC);
            Add(top, "Gap theo trục cao (mm)", gap);
            heightAxis.Items.AddRange(new object[] { "X (U=Y, V=Z)", "Y (U=X, V=Z)", "Z (U=X, V=Y)" });
            heightAxis.SelectedIndex = 1;
            Add(top, "Trục cao trong Part panel", heightAxis);
            Add(top, "Gốc footprint U (mm)", originU);
            Add(top, "Gốc footprint V (mm)", originV);
            Add(top, "Cao độ đáy (mm)", bottom);
            Add(top, "Mặt ghép tính từ đáy (mm)", seam);
            Add(top, "Độ hở theo chiều cao (mm)", depthClearance);

            map.Dock = DockStyle.Top; map.Height = 110; map.BackColor = Color.White;
            map.Paint += DrawLayout;
            report.Multiline = true; report.ReadOnly = true; report.ScrollBars = ScrollBars.Vertical;
            report.Dock = DockStyle.Fill; report.Font = new Font("Consolas", 9f);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 75,
                FlowDirection = FlowDirection.RightToLeft };
            var apply = new Button { Text = "Apply (chưa xác minh)", Width = 175, Enabled = false };
            var capture = new Button { Text = "Capture face", Width = 120 };
            var preview = new Button { Text = "Preview 3D", Width = 120 };
            var clear = new Button { Text = "Clear / Cancel 3D", Width = 140 };
            var calculate = new Button { Text = "Tính layout 2D", Width = 125 };
            calculate.Click += (s, e) => RefreshPlan();
            capture.Click += (s, e) => CaptureFace();
            preview.Click += (s, e) => Preview3D();
            clear.Click += (s, e) => backend.ClearTemporaryPreview(null);
            buttons.Controls.Add(apply); buttons.Controls.Add(preview); buttons.Controls.Add(capture);
            buttons.Controls.Add(clear); buttons.Controls.Add(calculate);

            var container = new Panel { Dock = DockStyle.Fill };
            var inputScroll = new Panel { Dock = DockStyle.Top, Height = 310, AutoScroll = true };
            inputScroll.Controls.Add(top);
            container.Controls.Add(report); container.Controls.Add(map); container.Controls.Add(inputScroll);
            Controls.Add(container); Controls.Add(buttons);
            preset.SelectedIndexChanged += (s, e) => { if (!loading) LoadPreset(preset.Text); };
            preset.SelectedIndex = 0;
            LoadPreset("KDA1");
            foreach (var input in new[] { countL, countC, extentX, extentZ, marginL, marginC,
                thickL, thickC, clearanceL, clearanceC, gap, originU, originV, bottom, seam, depthClearance })
                input.ValueChanged += (s, e) => { if (!loading) { backend.ClearTemporaryPreview(null); RefreshPlan(); } };
            heightAxis.SelectedIndexChanged += (s, e) => { if (!loading) { backend.ClearTemporaryPreview(null); source = null; lastPreviewPlan = null; RefreshPlan(); } };
            lifecycle.Tick += (s, e) => CheckSourceLifecycle();
            lifecycle.Start();
        }

        private void CaptureFace()
        {
            try {
                backend.ClearTemporaryPreview(null);
                source = null;
                lastPreviewPlan = null;
                var captured = backend.CaptureSource(app, app.ActiveDoc as ModelDoc2);
                // Face.GetBox is deliberately approximate for spline surfaces.
                // Use true geometric extrema for the initial footprint instead.
                var box = backend.MeasureBounds((Face2)captured.SelectedFaceTransient);
                int h = heightAxis.SelectedIndex;
                int u = KadaiPreviewBackend.UAxis(h);
                int v = KadaiPreviewBackend.VAxis(h);
                loading = true;
                try {
                    originU.Value = (decimal)Math.Floor(box[u] * 100000) / 100;
                    originV.Value = (decimal)Math.Floor(box[v] * 100000) / 100;
                    extentX.Value = (decimal)Math.Ceiling((box[u + 3] * 1000 - (double)originU.Value) * 10) / 10;
                    extentZ.Value = (decimal)Math.Ceiling((box[v + 3] * 1000 - (double)originV.Value) * 10) / 10;
                    decimal minimumHeight = (decimal)(box[h] * 1000) - gap.Value;
                    if (minimumHeight <= bottom.Value + seam.Value)
                        bottom.Value = Math.Floor((minimumHeight - seam.Value - 20) * 100) / 100;
                } finally { loading = false; }
                source = captured;
                RefreshPlan();
                report.AppendText(System.Environment.NewLine + "Đã chọn panel "
                    + (source.PanelComponent?.Name2 ?? source.PanelPart.GetTitle())
                    + " / " + source.PanelConfiguration
                    + ". Kiểm tra lề và đặt cao độ đáy theo mặt đế trong hệ tọa độ Part panel.");
            } catch (Exception ex) { source = null; report.Text = "CAPTURE: " + ex.Message; }
        }
        private void Preview3D()
        {
            try {
                backend.ClearTemporaryPreview(null);
                if (source == null) throw new InvalidOperationException("Chọn mặt panel và bấm Capture face trước.");
                plan = KadaiPlanner.Build(ReadSettings());
                var profiles = backend.CalculateProfiles(plan, source);
                Dictionary<string, List<KadaiJointCut>> joints;
                try {
                    joints = KadaiJointGeometry.Build(plan, profiles, (double)seam.Value, (double)depthClearance.Value);
                } catch (InvalidOperationException ex) {
                    report.Text = "Kiểm tra rãnh: " + ex.Message;
                    string suggestion = KadaiJointGeometry.SuggestMargins(plan, profiles);
                    if (suggestion.Length > 0) report.AppendText(System.Environment.NewLine + suggestion);
                    return;
                }
                backend.ShowTemporaryPreview(source, KadaiJointGeometry.NotchedProfiles(plan, profiles, joints));
                report.Text = "Preview 3D: " + backend.VisibleBodyCount + " thanh đã xẻ rãnh, "
                    + plan.CrossingCount + " điểm giao, " + joints.Values.Sum(cuts => cuts.Count) + " rãnh."
                    + System.Environment.NewLine + "Dọc màu xanh lá: rãnh mở từ đáy. Ngang màu cam: rãnh mở từ phía panel."
                    + System.Environment.NewLine + "Apply đang khóa.";
                if (lastPreviewPlan != null)
                    report.AppendText(System.Environment.NewLine + KadaiPlanner.DescribeStationChanges(lastPreviewPlan, plan));
                lastPreviewPlan = plan;
            } catch (Exception ex) { backend.ClearTemporaryPreview(null); report.Text = "3D PREVIEW STOP: " + ex.Message; }
        }
        private void CheckSourceLifecycle()
        {
            if (source == null) return;
            try {
                if (backend.IsSourceCurrent(source)) return;
            } catch (System.Runtime.InteropServices.COMException) { }
            backend.ClearTemporaryPreview(null); source = null; lastPreviewPlan = null;
            report.Text = "Panel document/configuration/rebuild đã đổi. Preview đã xóa; Capture lại mặt.";
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { lifecycle.Stop(); lifecycle.Dispose(); backend.Dispose(); source = null; }
            base.Dispose(disposing);
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
            backend.ClearTemporaryPreview(null);
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
                seam.Value = p.Preset == "KF3" ? 50 : 100;
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
                GapToPanelMm = (double)gap.Value,
                HeightAxis = heightAxis.SelectedIndex, OriginUmm = (double)originU.Value,
                OriginVmm = (double)originV.Value, BottomMm = (double)bottom.Value
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
                report.Text = "Document: " + doc.GetTitle() + System.Environment.NewLine
                    + plan.ToReport() + System.Environment.NewLine
                    + "3D: Chọn mặt của Part panel trong Assembly rồi bấm Capture face. Datum theo trục của Part panel.";
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
