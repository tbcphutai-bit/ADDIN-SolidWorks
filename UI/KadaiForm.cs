// Kadai layout and measured temporary-body preview. Production Apply stays gated.
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
        private readonly ComboBox heightAxis = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly NumericUpDown originU = Number(-99999, 99999, 0, 2);
        private readonly NumericUpDown originV = Number(-99999, 99999, 0, 2);
        private readonly NumericUpDown bottom = Number(-99999, 99999, 0, 2);
        private readonly NumericUpDown seam = Number(.1m, 99999, 100, 1);
        private readonly NumericUpDown depthClearance = Number(.01m, 20, .1m, 2);
        private readonly CheckBox autoMargins = new CheckBox { Text = "Tự tìm lề khi cần", AutoSize = true, Checked = true };
        private readonly Label panelName = FaceLabel();
        private readonly Label baseName = FaceLabel();
        private readonly Label pickHint = new Label { AutoSize = true, Text = "Chọn mặt panel trước, sau đó chọn mặt phẳng đế." };
        private readonly KadaiPreviewBackend backend;
        private readonly Timer lifecycle = new Timer { Interval = 300 };
        private KadaiPanelSource source;
        private KadaiBaseDatum baseDatum;
        private enum PickTarget { None, Panel, Base }
        private PickTarget picking;
        private ModelDoc2 pickDocument;
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
            StartPosition = FormStartPosition.Manual;
            var workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(workArea.Right - Width, workArea.Top + 48);
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
            Add(top, "Dày thanh dọc (mm)", thickL);
            Add(top, "Dày thanh ngang (mm)", thickC);
            var advanced = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true,
                ColumnCount = 2, Padding = new Padding(12), Visible = false };
            advanced.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            advanced.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            Add(advanced, "Span bố trí U (mm)", extentX);
            Add(advanced, "Span bố trí V (mm)", extentZ);
            Add(advanced, "Lề dọc (mm)", marginL);
            Add(advanced, "Lề ngang (mm)", marginC);
            Add(advanced, "Độ hở tổng rãnh thanh dọc (mm)", clearanceL);
            Add(advanced, "Độ hở tổng rãnh thanh ngang (mm)", clearanceC);
            Add(advanced, "Khe hở tới panel (mm)", gap);
            heightAxis.Items.AddRange(new object[] { "X (U=Y, V=Z)", "Y (U=X, V=Z)", "Z (U=X, V=Y)" });
            heightAxis.SelectedIndex = 1;
            Add(advanced, "Trục cao trong Part panel", heightAxis);
            Add(advanced, "Gốc bố trí U (mm)", originU);
            Add(advanced, "Gốc bố trí V (mm)", originV);
            Add(advanced, "Cao độ đáy (mm)", bottom);
            Add(advanced, "Mặt ghép tính từ đáy (mm)", seam);
            Add(advanced, "Độ hở theo chiều cao (mm)", depthClearance);
            Add(advanced, "Bố trí lề", autoMargins);

            var geometry = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true,
                ColumnCount = 2, Padding = new Padding(12, 8, 12, 4) };
            geometry.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            geometry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var capture = new Button { Text = "1. Chọn mặt panel", AutoSize = true };
            var captureBase = new Button { Text = "2. Chọn mặt đế", AutoSize = true };
            geometry.Controls.Add(capture, 0, 0); geometry.Controls.Add(panelName, 1, 0);
            geometry.Controls.Add(captureBase, 0, 1); geometry.Controls.Add(baseName, 1, 1);
            geometry.Controls.Add(pickHint, 0, 2); geometry.SetColumnSpan(pickHint, 2);

            map.Dock = DockStyle.Top; map.Height = 110; map.BackColor = Color.White;
            map.Paint += DrawLayout;
            report.Multiline = true; report.ReadOnly = true; report.ScrollBars = ScrollBars.Vertical;
            report.Dock = DockStyle.Fill; report.Font = new Font("Consolas", 9f);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 75,
                FlowDirection = FlowDirection.RightToLeft };
            var apply = new Button { Text = "Apply (chưa xác minh)", Width = 175, Enabled = false };
            var preview = new Button { Text = "Preview 3D", Width = 120 };
            var clear = new Button { Text = "Clear / Cancel 3D", Width = 140 };
            var calculate = new Button { Text = "Tính layout 2D", Width = 125 };
            calculate.Click += (s, e) => RefreshPlan();
            capture.Click += (s, e) => BeginPick(PickTarget.Panel);
            captureBase.Click += (s, e) => BeginPick(PickTarget.Base);
            preview.Click += (s, e) => Preview3D();
            clear.Click += (s, e) => { picking = PickTarget.None; pickDocument = null; backend.ClearTemporaryPreview(null); UpdateGeometryStatus(); };
            buttons.Controls.Add(apply); buttons.Controls.Add(preview);
            buttons.Controls.Add(clear); buttons.Controls.Add(calculate);

            var container = new Panel { Dock = DockStyle.Fill };
            var inputScroll = new Panel { Dock = DockStyle.Top, Height = 215, AutoScroll = true };
            var showAdvanced = new CheckBox { Text = "Thông số nâng cao", Dock = DockStyle.Top,
                Height = 28, Padding = new Padding(12, 0, 0, 0) };
            showAdvanced.CheckedChanged += (s, e) =>
            {
                advanced.Visible = showAdvanced.Checked;
                inputScroll.Height = showAdvanced.Checked ? 300 : 215;
            };
            inputScroll.Controls.Add(advanced); inputScroll.Controls.Add(showAdvanced); inputScroll.Controls.Add(top);
            container.Controls.Add(report); container.Controls.Add(map); container.Controls.Add(inputScroll); container.Controls.Add(geometry);
            Controls.Add(container); Controls.Add(buttons);
            preset.SelectedIndexChanged += (s, e) => { if (!loading) LoadPreset(preset.Text); };
            preset.SelectedIndex = 0;
            LoadPreset("KDA1");
            foreach (var input in new[] { countL, countC, extentX, extentZ, marginL, marginC,
                thickL, thickC, clearanceL, clearanceC, gap, originU, originV, bottom, seam, depthClearance })
                input.ValueChanged += (s, e) => { if (!loading) { backend.ClearTemporaryPreview(null); RefreshPlan(); } };
            heightAxis.SelectedIndexChanged += (s, e) =>
            {
                if (loading) return;
                backend.ClearTemporaryPreview(null);
                if (source != null) SetFootprint(backend.MeasureBounds((Face2)source.SelectedFaceTransient), heightAxis.SelectedIndex);
                RefreshPlan();
            };
            autoMargins.CheckedChanged += (s, e) => { backend.ClearTemporaryPreview(null); RefreshPlan(); };
            lifecycle.Tick += (s, e) => CheckSourceLifecycle();
            lifecycle.Start();
        }

        private void BeginPick(PickTarget target)
        {
            try {
                var active = app.ActiveDoc as ModelDoc2;
                if (active == null || (active.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY
                    && active.GetType() != (int)swDocumentTypes_e.swDocPART))
                    throw new InvalidOperationException("Mở Assembly hoặc Part để chọn mặt.");
                if (target == PickTarget.Base && !backend.IsSourceCurrent(source))
                    throw new InvalidOperationException("Chọn mặt panel trước.");
                picking = target; pickDocument = active;
                UpdateGeometryStatus();
                CapturePendingPick();
            } catch (Exception ex) { pickHint.Text = ex.Message; }
        }

        private void CapturePendingPick()
        {
            var active = app.ActiveDoc as ModelDoc2;
            if (picking == PickTarget.None || active == null || app.IsSame(pickDocument, active) != (int)swObjectEquality.swObjectSame)
                return;
            var selection = (SelectionMgr)active.SelectionManager;
            if (selection.GetSelectedObjectCount2(-1) != 1 || !(selection.GetSelectedObject6(1, -1) is Face2))
                return;
            var target = picking;
            picking = PickTarget.None; pickDocument = null;
            try {
                if (target == PickTarget.Panel)
                {
                    var captured = backend.CaptureSource(app, active);
                    if (captured.PanelComponent == null)
                        SetFootprint(backend.MeasureBounds((Face2)captured.SelectedFaceTransient), heightAxis.SelectedIndex);
                    source = captured;
                    baseDatum = null;
                }
                else
                {
                    var datum = backend.CaptureBase(source);
                    SetFootprint(backend.MeasureBounds((Face2)source.SelectedFaceTransient), datum.HeightAxis);
                    baseDatum = datum;
                }
                backend.ClearTemporaryPreview(null);
                lastPreviewPlan = null;
                active.ClearSelection2(true);
                UpdateGeometryStatus();
                RefreshPlan();
            } catch (Exception ex) { UpdateGeometryStatus(); pickHint.Text = ex.Message; }
        }

        private void SetFootprint(double[] box, int h)
        {
            int u = KadaiPreviewBackend.UAxis(h);
            int v = KadaiPreviewBackend.VAxis(h);
            loading = true;
            try {
                heightAxis.SelectedIndex = h;
                originU.Value = (decimal)Math.Floor(box[u] * 100000) / 100;
                originV.Value = (decimal)Math.Floor(box[v] * 100000) / 100;
                extentX.Value = (decimal)Math.Ceiling((box[u + 3] * 1000 - (double)originU.Value) * 10) / 10;
                extentZ.Value = (decimal)Math.Ceiling((box[v + 3] * 1000 - (double)originV.Value) * 10) / 10;
                decimal minimumHeight = (decimal)(box[h] * 1000) - gap.Value;
                if (minimumHeight <= bottom.Value + seam.Value)
                    bottom.Value = Math.Floor((minimumHeight - seam.Value - 20) * 100) / 100;
            } finally { loading = false; }
        }

        private void UpdateGeometryStatus()
        {
            panelName.Text = SourceName(source);
            baseName.Text = SourceName(baseDatum?.Source);
            heightAxis.Enabled = baseDatum == null;
            bottom.Enabled = baseDatum == null;
            if (picking != PickTarget.None)
                pickHint.Text = "Chọn đúng một mặt " + (picking == PickTarget.Panel ? "panel" : "đế") + " trong vùng đồ họa.";
            else if (baseDatum != null)
                pickHint.Text = "Hướng cao " + (baseDatum.HeightSign > 0 ? "+" : "−") + "XYZ"[baseDatum.HeightAxis]
                    + "  |  Mặt đế " + baseDatum.PlaneCoordinateMm.ToString("0.###") + " mm";
            else
                pickHint.Text = source == null ? "Chọn mặt panel trước." : "Chọn mặt phẳng đế nơi các thanh tỳ lên.";
        }

        private static Label FaceLabel()
        {
            return new Label { Text = "Chưa chọn", Dock = DockStyle.Fill, AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft, MinimumSize = new Size(0, 25) };
        }

        private static string SourceName(KadaiPanelSource face)
        {
            return face == null ? "Chưa chọn" : face.PanelComponent?.Name2 ?? face.PanelPart.GetTitle();
        }
        private void Preview3D()
        {
            try {
                backend.ClearTemporaryPreview(null);
                if (source == null) throw new InvalidOperationException("Chọn mặt panel trước.");
                if (source.PanelComponent != null && baseDatum == null)
                    throw new InvalidOperationException("Chọn mặt phẳng đế trước khi Preview trong Assembly.");
                if (baseDatum != null && !backend.IsSourceCurrent(baseDatum.Source))
                    throw new InvalidOperationException("Mặt đế đã thay đổi. Chọn lại mặt đế.");
                var previewResult = backend.BuildLayout(ReadSettings(), source, (double)seam.Value,
                    (double)depthClearance.Value, baseDatum != null && autoMargins.Checked);
                plan = previewResult.Plan;
                backend.ShowTemporaryPreview(source, previewResult.Profiles);
                map.Invalidate();
                report.Text = "Preview 3D: " + backend.VisibleBodyCount + " thanh đã xẻ rãnh, "
                    + plan.CrossingCount + " điểm giao, " + plan.CandidateSlots.Count + " rãnh."
                    + System.Environment.NewLine + "Lề Preview " + plan.Settings.LongEndMarginMm.ToString("0.0")
                    + " / " + plan.Settings.CrossEndMarginMm.ToString("0.0") + " mm."
                    + System.Environment.NewLine + "Dọc màu xanh lá: rãnh mở từ đáy. Ngang màu cam: rãnh mở từ phía panel."
                    + System.Environment.NewLine + "Apply đang khóa.";
                if (lastPreviewPlan != null)
                    report.AppendText(System.Environment.NewLine + KadaiPlanner.DescribeStationChanges(lastPreviewPlan, plan));
                lastPreviewPlan = plan;
            } catch (Exception ex) { backend.ClearTemporaryPreview(null); report.Text = "3D PREVIEW STOP: " + ex.Message; }
        }
        private void CheckSourceLifecycle()
        {
            try {
                var active = app.ActiveDoc as ModelDoc2;
                if (picking != PickTarget.None && (active == null || app.IsSame(pickDocument, active) != (int)swObjectEquality.swObjectSame))
                { picking = PickTarget.None; pickDocument = null; UpdateGeometryStatus(); }
                if (source != null && !backend.IsSourceCurrent(source))
                {
                    backend.ClearTemporaryPreview(null); source = null; baseDatum = null; lastPreviewPlan = null;
                    if (picking == PickTarget.Base) { picking = PickTarget.None; pickDocument = null; }
                    UpdateGeometryStatus();
                    pickHint.Text = "Panel đã thay đổi. Chọn lại hai mặt.";
                }
                else if (baseDatum != null && !backend.IsSourceCurrent(baseDatum.Source))
                {
                    backend.ClearTemporaryPreview(null); baseDatum = null; lastPreviewPlan = null;
                    UpdateGeometryStatus();
                    pickHint.Text = "Mặt đế đã thay đổi. Chọn lại mặt đế.";
                }
                CapturePendingPick();
            } catch (System.Runtime.InteropServices.COMException) { }
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
                if (source == null) { extentX.Value = (decimal)p.LengthXmm; extentZ.Value = (decimal)p.LengthZmm; }
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
                HeightAxis = heightAxis.SelectedIndex, HeightSign = baseDatum?.HeightSign ?? 1, OriginUmm = (double)originU.Value,
                OriginVmm = (double)originV.Value,
                BottomMm = baseDatum == null ? (double)bottom.Value : baseDatum.PlaneCoordinateMm * baseDatum.HeightSign
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
                    + "Preview 3D trong Assembly dùng mặt panel và mặt phẳng đế đã chọn.";
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
