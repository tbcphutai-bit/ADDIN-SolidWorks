using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
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
        // SOLIDWORKS swCommands_e values from swcommands, without a new interop dependency.
        private const int NewDocumentCommand = 1;
        private const int EditMaterialCommand = 501;

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
            apply.Click += (s, e) => RunApply();
            clear.Click += (s, e) =>
            {
                picking = PickTarget.None;
                pickDocument = null;
                ClearPreview();
                UpdateSelectionStatus();
            };
            map.Paint += DrawLayout;
            map.Resize += (s, e) => map.Invalidate();
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

        private void RunApply()
        {
            if (applying || layout == null) return;
            string previousStatus = status.Text;
            var ready = layout;
            var sourceDocument = source;
            string step = "Chọn Template Part";
            applying = true;
            apply.Enabled = false;
            Enabled = false;
            try
            {
                string partTemplate = SelectTemplate(".prtdot", "Part", "Kadai · 1/5 · Template Part");
                if (partTemplate == null) return;
                step = "Chọn vật liệu";
                string materialDatabase;
                string material = SelectMaterial(partTemplate, out materialDatabase);
                if (material == null) return;
                int activateError = 0;
                app.ActivateDoc3(sourceDocument.DocumentTransient.GetTitle(), false,
                    (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref activateError);

                step = "Đặt tên Part";
                Dictionary<string, string> names;
                using (var dialog = new PartNamesDialog(ready.Blanks, material))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    names = dialog.Names;
                }
                step = "Chọn Template Drawing";
                string drawingTemplate = SelectTemplate(".drwdot", "Draw", "Kadai · 4/5 · Template bản vẽ");
                if (drawingTemplate == null) return;

                step = "Chọn thư mục lưu";
                using (var folder = new FolderBrowserDialog
                {
                    Description = "5. Chọn nơi lưu bộ chi tiết Kadai",
                    SelectedPath = Path.GetDirectoryName(sourceDocument.DocumentPath) ?? string.Empty
                })
                {
                    if (folder.ShowDialog(this) != DialogResult.OK) return;
                    var options = new KadaiApplyOptions(partTemplate, drawingTemplate, material, materialDatabase, names);
                    step = "Tạo gói Kadai";
                    previousStatus = "Đã lưu bộ Kadai tại " + ApplyToFolder(folder.SelectedPath, options);
                }
            }
            catch (Exception ex)
            {
                previousStatus = step + " · " + ex.Message;
                Trace.WriteLine("[KADAI] " + step + System.Environment.NewLine + ex);
                MessageBox.Show(this, previousStatus, "Kadai", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                status.Text = previousStatus;
                applying = false;
                Enabled = true;
                apply.Enabled = layout != null;
            }
        }

        private string ApplyToFolder(string parentDirectory, KadaiApplyOptions options)
        {
            try { return backend.ApplyBaseLayout(layout, source, baseDatum, parentDirectory, options, SetApplyStatus); }
            finally { ClearPreview(); }
        }

        private void SetApplyStatus(string message)
        {
            status.Text = message;
            status.Refresh();
        }

        private string SelectTemplate(string extension, string tab, string caption)
        {
            string instruction = "Trong hộp thoại SolidWorks, mở tab " + tab + " và chọn mẫu " + extension + ".";
            SetApplyStatus(instruction);
            if (MessageBox.Show(this, instruction, caption, MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information) != DialogResult.OK)
                return null;

            while (true)
            {
                string selected = CaptureTemplateSelection(() => app.RunCommand(NewDocumentCommand, ""));
                if (selected == null) return null;
                if (selected.Length == 0)
                {
                    Trace.WriteLine("[KADAI] The new document command did not report the template path.");
                    return BrowseTemplateFile(extension);
                }
                if (!string.Equals(Path.GetExtension(selected), extension, StringComparison.OrdinalIgnoreCase))
                {
                    Trace.WriteLine("[KADAI] Expected " + extension + ", selected: " + selected);
                    if (MessageBox.Show(this, instruction + "\nMục đã chọn là " + Path.GetFileName(selected) + ".",
                        caption, MessageBoxButtons.RetryCancel, MessageBoxIcon.Information) != DialogResult.Retry)
                        return null;
                    continue;
                }
                KadaiApplyOptions.RequireTemplate(selected, extension);
                return selected;
            }
        }

        private string CaptureTemplateSelection(Action invoke)
        {
            var events = (DSldWorksEvents_Event)app;
            var selection = new TemplateSelection();
            ModelDoc2 scratch = null;
            DSldWorksEvents_FileNewPreNotifyEventHandler beforeCreate = (type, path) =>
            {
                Trace.WriteLine("[KADAI] FileNewPreNotify " + type + ": " + path);
                return selection.BeforeCreate(path);
            };
            DSldWorksEvents_FileNewNotify2EventHandler afterCreate = (document, type, path) =>
            {
                Trace.WriteLine("[KADAI] FileNewNotify2 " + type + ": " + path);
                if (scratch == null) scratch = document as ModelDoc2;
                selection.AfterCreate(path);
                return 0;
            };
            events.FileNewPreNotify += beforeCreate;
            try
            {
                events.FileNewNotify2 += afterCreate;
                try { invoke(); }
                finally { events.FileNewNotify2 -= afterCreate; }
            }
            finally
            {
                events.FileNewPreNotify -= beforeCreate;
                if (scratch != null && string.IsNullOrEmpty(scratch.GetPathName()))
                    app.CloseDoc(scratch.GetTitle());
            }
            return selection.Result;
        }

        private sealed class TemplateSelection
        {
            private string path;
            private bool created;

            // An extension-only pre-notification can be followed by cancellation.
            public string Result => path ?? (created ? string.Empty : null);

            public int BeforeCreate(string template)
            {
                if (!IsTemplatePath(template)) return 0;
                path = template;
                return 1; // A known template path can be used without creating a scratch document.
            }

            public void AfterCreate(string template)
            {
                created = true;
                if (IsTemplatePath(template)) path = template;
            }
        }

        private static bool IsTemplatePath(string path)
        {
            if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path)) return false;
            string extension = Path.GetExtension(path);
            return string.Equals(extension, ".prtdot", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".drwdot", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".asmdot", StringComparison.OrdinalIgnoreCase);
        }

        private string BrowseTemplateFile(string extension)
        {
            var setting = extension == ".prtdot" ? swUserPreferenceStringValue_e.swDefaultTemplatePart :
                swUserPreferenceStringValue_e.swDefaultTemplateDrawing;
            string defaultPath = app.GetUserPreferenceStringValue((int)setting);
            string templateFolders = app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swFileLocationsDocumentTemplates);
            string initialFolder = (templateFolders ?? "").Split(';').Select(path => path.Trim()).FirstOrDefault(Directory.Exists)
                ?? Path.GetDirectoryName(defaultPath);
            using (var dialog = new OpenFileDialog
            {
                Title = "Kadai · Chọn file template " + extension,
                InitialDirectory = initialFolder ?? string.Empty,
                Filter = "SolidWorks template (*" + extension + ")|*" + extension
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return null;
                KadaiApplyOptions.RequireTemplate(dialog.FileName, extension);
                return dialog.FileName;
            }
        }

        private string SelectMaterial(string partTemplate, out string databasePath)
        {
            databasePath = null;
            SetApplyStatus("2. Chọn vật liệu, bấm Apply rồi đóng hộp thoại Material.");
            var scratch = app.NewDocument(partTemplate, 0, 0, 0) as ModelDoc2;
            if (scratch == null)
                throw new InvalidOperationException("SolidWorks không mở được Part tạm từ template đã chọn.");
            try
            {
                var part = (PartDoc)scratch;
                string config = scratch.ConfigurationManager.ActiveConfiguration.Name;
                string baseDatabase = backend.ResolveMaterialDatabase(baseDatum.MaterialDatabase);
                if (!string.IsNullOrEmpty(baseDatabase) && !string.IsNullOrWhiteSpace(baseDatum.MaterialName))
                    part.SetMaterialPropertyName2(config, baseDatabase, baseDatum.MaterialName);
                if (!app.RunCommand(EditMaterialCommand, ""))
                    throw new InvalidOperationException("SolidWorks không mở được hộp thoại Material cho Part tạm.");
                string database;
                string material = part.GetMaterialPropertyName2(config, out database);
                if (string.IsNullOrWhiteSpace(material)) return null;
                databasePath = backend.ResolveMaterialDatabase(database);
                if (databasePath == null)
                    throw new InvalidOperationException("Không tìm thấy thư viện của vật liệu đã chọn: " + database);
                return material;
            }
            finally { app.CloseDoc(scratch.GetTitle()); }
        }

        private sealed class PartNamesDialog : Form
        {
            private readonly DataGridView grid = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect
            };
            private readonly Label validation = new Label { AutoSize = true, ForeColor = Color.Firebrick };
            internal Dictionary<string, string> Names { get; private set; }

            internal PartNamesDialog(IReadOnlyList<KadaiProfile3D> ribs, string material)
            {
                Text = "Kadai · Tên Part";
                StartPosition = FormStartPosition.CenterParent;
                ClientSize = new Size(475, 408);
                MinimumSize = new Size(430, 330);
                ShowInTaskbar = false;

                var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
                content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
                content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                AddFullRow(content, Caption("Vật liệu: " + material), new RowStyle(SizeType.AutoSize));
                var longSeed = new TextBox { Text = "L-001", Dock = DockStyle.Fill };
                var crossSeed = new TextBox { Text = "C-001", Dock = DockStyle.Fill };
                AddRow(content, Caption("Tên bắt đầu · Dọc"), longSeed);
                AddRow(content, Caption("Tên bắt đầu · Ngang"), crossSeed);

                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Thanh", ReadOnly = true,
                    SortMode = DataGridViewColumnSortMode.NotSortable, FillWeight = 38 });
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tên Part", SortMode = DataGridViewColumnSortMode.NotSortable });
                foreach (var rib in ribs)
                    grid.Rows[grid.Rows.Add(rib.RibId, rib.RibId)].Tag = rib;
                AddFullRow(content, grid, new RowStyle(SizeType.Percent, 100));
                AddFullRow(content, validation, new RowStyle(SizeType.AutoSize));
                var next = new Button { Text = "Tiếp tục", AutoSize = true };
                var cancel = new Button { Text = "Hủy", AutoSize = true, DialogResult = DialogResult.Cancel };
                var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
                actions.Controls.AddRange(new Control[] { next, cancel });
                AddFullRow(content, actions, new RowStyle(SizeType.AutoSize));
                Controls.Add(content);
                AcceptButton = next;
                CancelButton = cancel;

                longSeed.TextChanged += (s, e) => SetSeries(longSeed.Text, KadaiRibDirection.Longitudinal);
                crossSeed.TextChanged += (s, e) => SetSeries(crossSeed.Text, KadaiRibDirection.Transverse);
                next.Click += (s, e) =>
                {
                    try
                    {
                        KadaiApplyOptions.NumberedNames(longSeed.Text, ribs.Count(rib => rib.Direction == KadaiRibDirection.Longitudinal));
                        KadaiApplyOptions.NumberedNames(crossSeed.Text, ribs.Count(rib => rib.Direction == KadaiRibDirection.Transverse));
                        grid.EndEdit();
                        var names = grid.Rows.Cast<DataGridViewRow>().ToDictionary(row => ((KadaiProfile3D)row.Tag).RibId,
                            row => (Convert.ToString(row.Cells[1].Value) ?? "").Trim(), StringComparer.Ordinal);
                        KadaiApplyOptions.ValidateNames(names.Values);
                        Names = names;
                        DialogResult = DialogResult.OK;
                    }
                    catch (InvalidOperationException ex) { validation.Text = ex.Message; }
                };
            }

            private void SetSeries(string seed, KadaiRibDirection direction)
            {
                try
                {
                    var group = grid.Rows.Cast<DataGridViewRow>()
                        .Where(row => ((KadaiProfile3D)row.Tag).Direction == direction).ToArray();
                    var names = KadaiApplyOptions.NumberedNames(seed, group.Length);
                    for (int i = 0; i < group.Length; i++) group[i].Cells[1].Value = names[i];
                    validation.Text = "";
                }
                catch (InvalidOperationException ex) { validation.Text = ex.Message; }
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
            var box = FitPreviewBounds(map.ClientRectangle, settings.LengthXmm, settings.LengthZmm);
            if (box.IsEmpty) return;
            double pixelsPerMm = box.Width / settings.LengthXmm;
            PointF Project(double[] p) => new PointF(
                box.Left + (float)((p[u] * 1000 - settings.OriginUmm) * pixelsPerMm),
                box.Bottom - (float)((p[v] * 1000 - settings.OriginVmm) * pixelsPerMm));
            using (var border = new Pen(Color.LightSlateGray))
            using (var longitudinal = new Pen(Color.SeaGreen, 2f))
            using (var transverse = new Pen(Color.Chocolate, 2f))
            {
                e.Graphics.DrawRectangle(border, box.X, box.Y, box.Width, box.Height);
                foreach (var rib in layout.Blanks)
                    e.Graphics.DrawLine(rib.Direction == KadaiRibDirection.Longitudinal ? longitudinal : transverse,
                        Project(rib.PointsWorldMeters[0]), Project(rib.PointsWorldMeters[rib.PointsWorldMeters.Count - 2]));
            }
        }

        private static RectangleF FitPreviewBounds(Rectangle client, double widthMm, double heightMm)
        {
            const int horizontalPadding = 10, verticalPadding = 8;
            if (!(widthMm > 0) || !(heightMm > 0)) return RectangleF.Empty;
            double pixelsPerMm = Math.Min((client.Width - 2 * horizontalPadding) / widthMm,
                (client.Height - 2 * verticalPadding) / heightMm);
            if (pixelsPerMm <= 0) return RectangleF.Empty;

            float width = (float)(widthMm * pixelsPerMm);
            float height = (float)(heightMm * pixelsPerMm);
            return new RectangleF(client.Left + (client.Width - width) / 2,
                client.Top + (client.Height - height) / 2, width, height);
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
