using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ADDIN.Commands.Kadai;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.UI
{
    internal sealed class KadaiForm : Form
    {
        private enum PickTarget { None, Panel, Base }

        private readonly ISldWorks app;
        private readonly KadaiPreviewBackend backend;
        private readonly Timer lifecycle = new Timer { Interval = 300 };
        private readonly NumericUpDown countL = Number(1, 100, 7, 0);
        private readonly NumericUpDown countC = Number(1, 100, 4, 0);
        private readonly NumericUpDown startClearance = Number(0, 99999, 50, 1);
        private readonly NumericUpDown endClearance = Number(0, 99999, 50, 1);
        private readonly NumericUpDown fixedHeight = Number(.1m, 99999, 170, 1);
        private readonly ComboBox panelDirection = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        private readonly Label panelName = FaceLabel();
        private readonly Label baseName = FaceLabel();
        private readonly Label status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Panel map = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        private readonly Button preview = new Button { Text = "Preview 3D", AutoSize = true, Enabled = false };
        private readonly Button apply = new Button { Text = "Apply", AutoSize = true, Enabled = false };
        private KadaiPanelSource source;
        private KadaiBaseDatum baseDatum;
        private KadaiPreviewBackend.SlottedLayout layout;
        private PickTarget picking;
        private ModelDoc2 pickDocument;
        private bool applying;

        internal KadaiForm(ISldWorks app)
        {
            this.app = app;
            backend = new KadaiPreviewBackend(app);
            Text = "Kadai";
            ClientSize = new Size(505, 416);
            MinimumSize = new Size(465, 422);
            StartPosition = FormStartPosition.Manual;
            var workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(Math.Max(workArea.Left, workArea.Right - Width), workArea.Top + 48);
            Font = new Font("Segoe UI", 9f);

            var form = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 193));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var capture = new Button { Text = "1. Chọn mặt panel", AutoSize = true };
            var captureBase = new Button { Text = "2. Chọn mặt đế", AutoSize = true };
            capture.Click += (s, e) => BeginPick(PickTarget.Panel);
            captureBase.Click += (s, e) => BeginPick(PickTarget.Base);
            AddRow(form, capture, panelName);
            AddRow(form, captureBase, baseName);
            AddRow(form, Caption("Số thanh dọc"), countL);
            AddRow(form, Caption("Số thanh ngang"), countC);
            panelDirection.Items.AddRange(new object[] { "Dọc (xanh)", "Ngang (cam)" });
            panelDirection.SelectedIndex = 0;
            AddRow(form, Caption("Phương bám panel"), panelDirection);
            AddRow(form, Caption("Khoảng chừa đầu base (mm)"), startClearance);
            AddRow(form, Caption("Khoảng chừa cuối base (mm)"), endClearance);
            AddRow(form, Caption("Chiều cao thanh cố định (mm)"), fixedHeight);
            AddFullRow(form, map, new RowStyle(SizeType.Percent, 100));
            AddFullRow(form, status, new RowStyle(SizeType.Absolute, 42));

            var clear = new Button { Text = "Xóa Preview", AutoSize = true };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            actions.Controls.AddRange(new Control[] { apply, preview, clear });
            AddFullRow(form, actions, new RowStyle(SizeType.AutoSize));
            Controls.Add(form);

            preview.Click += (s, e) => Preview3D();
            apply.Click += (s, e) =>
            {
                using (var folder = new FolderBrowserDialog { Description = "Chọn nơi lưu bộ chi tiết Kadai",
                    SelectedPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments) })
                    if (folder.ShowDialog(this) == DialogResult.OK) ApplyToFolder(folder.SelectedPath);
            };
            clear.Click += (s, e) =>
            {
                picking = PickTarget.None;
                pickDocument = null;
                ClearPreview();
                UpdateSelectionStatus();
            };
            map.Paint += DrawLayout;
            foreach (var input in new[] { countL, countC, startClearance, endClearance, fixedHeight })
                input.ValueChanged += (s, e) => ResetPreview();
            panelDirection.SelectedIndexChanged += (s, e) => ResetPreview();
            UpdateSelectionStatus();
            lifecycle.Tick += (s, e) => CheckSourceLifecycle();
            lifecycle.Start();
        }

        private void BeginPick(PickTarget target)
        {
            try
            {
                var active = app.ActiveDoc as ModelDoc2;
                if (active == null || (active.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY
                    && active.GetType() != (int)swDocumentTypes_e.swDocPART))
                    throw new InvalidOperationException("Mở Assembly hoặc Part để chọn mặt.");
                if (target == PickTarget.Base && !backend.IsSourceCurrent(source))
                    throw new InvalidOperationException("Chọn mặt panel trước.");
                picking = target;
                pickDocument = active;
                UpdateSelectionStatus();
                CapturePendingPick();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        }

        private void CapturePendingPick()
        {
            if (picking == PickTarget.None) return;
            var active = app.ActiveDoc as ModelDoc2;
            if (active == null || app.IsSame(pickDocument, active) != (int)swObjectEquality.swObjectSame) return;
            var selection = (SelectionMgr)active.SelectionManager;
            if (selection.GetSelectedObjectCount2(-1) != 1 || !(selection.GetSelectedObject6(1, -1) is Face2)) return;
            var target = picking;
            picking = PickTarget.None;
            pickDocument = null;
            try
            {
                if (target == PickTarget.Panel)
                {
                    source = backend.CaptureSource(app, active);
                    baseDatum = null;
                }
                else baseDatum = backend.CaptureBase(source);
                ClearPreview();
                active.ClearSelection2(true);
                UpdateSelectionStatus();
            }
            catch (Exception ex) { UpdateSelectionStatus(); status.Text = ex.Message; }
        }

        private void UpdateSelectionStatus()
        {
            panelName.Text = SourceName(source);
            baseName.Text = SourceName(baseDatum?.Source);
            preview.Enabled = baseDatum != null && picking == PickTarget.None;
            apply.Enabled = layout != null && picking == PickTarget.None;
            if (picking != PickTarget.None)
                status.Text = picking == PickTarget.Panel ? "Chọn một mặt panel trong vùng đồ họa." : "Chọn mặt phẳng tỳ của base.";
            else if (baseDatum != null)
                status.Text = "Đã chọn hai mặt. Bấm Preview 3D.";
            else
                status.Text = source == null ? "Chọn mặt panel trước." : "Chọn mặt phẳng tỳ của base.";
        }

        private void Preview3D()
        {
            try
            {
                ClearPreview();
                if (source == null) throw new InvalidOperationException("Chọn mặt panel trước.");
                const double ribThicknessMm = 1.6;
                const double slotClearanceMm = .2;
                var request = new KadaiRequest
                {
                    LongRibCount = (int)countL.Value, CrossRibCount = (int)countC.Value,
                    StartClearanceMm = (double)startClearance.Value, EndClearanceMm = (double)endClearance.Value,
                    FixedHeightMm = (double)fixedHeight.Value,
                    PanelRibDirection = panelDirection.SelectedIndex == 0 ? KadaiRibDirection.Longitudinal : KadaiRibDirection.Transverse,
                    LongThicknessMm = ribThicknessMm, CrossThicknessMm = ribThicknessMm,
                    LongSlotExtraMm = slotClearanceMm, CrossSlotExtraMm = slotClearanceMm
                };
                var result = backend.BuildBaseLayout(request, source, baseDatum);
                backend.ShowTemporaryPreview(source, result.Profiles);
                layout = result;
                apply.Enabled = true;
                map.Invalidate();
                int outside = layout.Plan.CrossingCount - layout.JoinedCrossings;
                status.Text = outside == 0 ? "Đã tạo Preview." : "Đã tạo Preview. " + outside + " giao điểm nằm ngoài biên thanh.";
            }
            catch (Exception ex) { ClearPreview(); status.Text = ex.Message; }
        }

        private string ApplyToFolder(string parentDirectory)
        {
            if (applying || layout == null) return null;
            applying = true;
            var ready = layout;
            apply.Enabled = false;
            Enabled = false;
            try
            {
                string target = backend.ApplyBaseLayout(ready, source, baseDatum, parentDirectory, message =>
                {
                    status.Text = message.StartsWith("START ", StringComparison.Ordinal) ? "Đang tạo " + message.Substring(6) : status.Text;
                    status.Refresh();
                });
                ClearPreview();
                status.Text = "Đã lưu bộ Kadai tại " + target;
                return target;
            }
            catch (Exception ex)
            {
                ClearPreview();
                status.Text = ex.Message;
                return null;
            }
            finally
            {
                applying = false;
                Enabled = true;
            }
        }

        private void ResetPreview()
        {
            ClearPreview();
            UpdateSelectionStatus();
        }

        private void ClearPreview()
        {
            backend.ClearTemporaryPreview(null);
            layout = null;
            apply.Enabled = false;
            map.Invalidate();
        }

        private void CheckSourceLifecycle()
        {
            if (applying) return;
            try
            {
                var active = app.ActiveDoc as ModelDoc2;
                if (picking != PickTarget.None && (active == null || app.IsSame(pickDocument, active) != (int)swObjectEquality.swObjectSame))
                {
                    picking = PickTarget.None;
                    pickDocument = null;
                    UpdateSelectionStatus();
                }
                if (source != null && !backend.IsSourceCurrent(source))
                {
                    source = null; baseDatum = null;
                    if (picking == PickTarget.Base) { picking = PickTarget.None; pickDocument = null; }
                    ResetPreview();
                }
                else if (baseDatum != null && !backend.IsSourceCurrent(baseDatum.Source))
                {
                    baseDatum = null;
                    ResetPreview();
                }
                CapturePendingPick();
            }
            catch (COMException) { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { lifecycle.Stop(); lifecycle.Dispose(); backend.Dispose(); }
            base.Dispose(disposing);
        }

        private void DrawLayout(object sender, PaintEventArgs e)
        {
            if (layout == null) return;
            var settings = layout.Plan.Settings;
            int u = KadaiPreviewBackend.UAxis(settings.HeightAxis), v = KadaiPreviewBackend.VAxis(settings.HeightAxis);
            var box = new Rectangle(10, 8, Math.Max(1, map.ClientSize.Width - 21), Math.Max(1, map.ClientSize.Height - 17));
            PointF Project(double[] p) => new PointF(
                box.Left + (float)((p[u] * 1000 - settings.OriginUmm) / settings.LengthXmm * box.Width),
                box.Bottom - (float)((p[v] * 1000 - settings.OriginVmm) / settings.LengthZmm * box.Height));
            using (var border = new Pen(Color.LightSlateGray))
            using (var longitudinal = new Pen(Color.SeaGreen, 2f))
            using (var transverse = new Pen(Color.Chocolate, 2f))
            {
                e.Graphics.DrawRectangle(border, box);
                foreach (var rib in layout.Blanks)
                    e.Graphics.DrawLine(rib.Direction == KadaiRibDirection.Longitudinal ? longitudinal : transverse,
                        Project(rib.PointsWorldMeters[0]), Project(rib.PointsWorldMeters[rib.PointsWorldMeters.Count - 2]));
            }
        }

        private static string SourceName(KadaiPanelSource face) =>
            face == null ? "Chưa chọn" : face.PanelComponent?.Name2 ?? face.PanelPart.GetTitle();

        private static Label FaceLabel() => new Label { Dock = DockStyle.Fill, AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft, MinimumSize = new Size(0, 25) };

        private static Label Caption(string text) => new Label { Text = text, AutoSize = true,
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

        private static NumericUpDown Number(decimal min, decimal max, decimal value, int digits) =>
            new NumericUpDown { Minimum = min, Maximum = max, Value = value, DecimalPlaces = digits,
                Increment = digits == 0 ? 1 : 1m, Dock = DockStyle.Fill, ThousandsSeparator = true };

        private static void AddRow(TableLayoutPanel host, Control label, Control input)
        {
            int row = host.RowCount++;
            host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            host.Controls.Add(label, 0, row);
            host.Controls.Add(input, 1, row);
        }

        private static void AddFullRow(TableLayoutPanel host, Control control, RowStyle style)
        {
            int row = host.RowCount++;
            host.RowStyles.Add(style);
            host.Controls.Add(control, 0, row);
            host.SetColumnSpan(control, 2);
        }
    }
}
