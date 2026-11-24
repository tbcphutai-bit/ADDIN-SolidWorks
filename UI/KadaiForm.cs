using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using ADDIN.Commands;
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
        private readonly NumericUpDown jointHeight = Number(.1m, 99999, 100, 1);
        private readonly Button orientationHoleButton = new Button { AutoSize = true, Anchor = AnchorStyles.Left };
        private readonly Button tubeButton = new Button { Text = "Tạo ống và cắt gân…", AutoSize = true, Anchor = AnchorStyles.Left };
        private KadaiTubeSettings tubeSettings;
        private KadaiOrientationHoleSettings orientationHole = new KadaiOrientationHoleSettings(6, 15, 15, false, false);
        private bool orientationHoleEnabled = true;
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
        private PartNamesDialog partOptions;
        private bool CanApply => layout != null && source?.DocumentTransient is AssemblyDoc && picking == PickTarget.None;

        internal KadaiForm(ISldWorks app)
        {
            this.app = app;
            backend = new KadaiPreviewBackend(app);
            Text = "Kadai";
            ClientSize = new Size(505, 416);
            MinimumSize = new Size(465, 455);
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
            AddRow(form, Caption("Chiều cao rãnh từ mặt đế (mm)"), jointHeight);
            ConfigureOrientationHoleButton();
            var optionsRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
            optionsRow.Controls.AddRange(new Control[] { orientationHoleButton, tubeButton });
            AddFullRow(form, optionsRow, new RowStyle(SizeType.AutoSize));
            tubeButton.Click += (s, e) =>
            {
                using (var dialog = new KadaiTubeOptionsDialog(tubeSettings))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    tubeSettings = dialog.Settings;
                    tubeButton.Text = tubeSettings == null ? "Tạo ống và cắt gân…" : "Cài đặt ống · Đang bật";
                    ResetPreview();
                }
            };
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
            foreach (var input in new[] { countL, countC, startClearance, endClearance, fixedHeight, jointHeight })
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
            apply.Enabled = CanApply;
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
                    JointHeightMm = (double)jointHeight.Value,
                    OrientationHole = orientationHoleEnabled ? orientationHole : null,
                    Tubes = tubeSettings,
                    PanelRibDirection = panelDirection.SelectedIndex == 0 ? KadaiRibDirection.Longitudinal : KadaiRibDirection.Transverse,
                    LongThicknessMm = ribThicknessMm, CrossThicknessMm = ribThicknessMm,
                    LongSlotExtraMm = slotClearanceMm, CrossSlotExtraMm = slotClearanceMm
                };
                var result = backend.BuildBaseLayout(request, source, baseDatum);
                backend.ShowTemporaryPreview(source, result);
                layout = result;
                apply.Enabled = CanApply;
                map.Invalidate();
                int outside = layout.Plan.CrossingCount - layout.JoinedCrossings;
                status.Text = outside == 0 ? "Đã tạo Preview." : "Đã tạo Preview. " + outside + " giao điểm nằm ngoài biên thanh.";
                if (layout.Tubes != null) status.Text += " " + layout.Tubes.Axes.Length + " ống.";
                if (!CanApply) status.Text += " Apply cần Assembly.";
            }
            catch (Exception ex) { ClearPreview(); status.Text = ex.Message; }
        }

        private async void RunApply()
        {
            if (applying || !CanApply) return;
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
                string tubeMaterial = null, tubeDatabase = null;
                if (ready.Tubes != null)
                {
                    step = "Chọn vật liệu ống";
                    tubeMaterial = SelectMaterialFor(partTemplate, out tubeDatabase, "2. Chọn vật liệu ống, bấm Apply rồi đóng hộp thoại Material.");
                    if (tubeMaterial == null) return;
                }
                int activateError = 0;
                app.ActivateDoc3(sourceDocument.DocumentTransient.GetTitle(), false,
                    (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref activateError);

                step = "Đặt tên Part và chọn mặt ウラ";
                // Switching to the material Part may discard the displayed bodies.
                backend.ShowTemporaryPreview(sourceDocument, ready);
                var choices = await ChoosePartNamesAsync(ready.Blanks, material, sourceDocument);
                if (choices.Names == null || IsDisposed) return;
                if (!backend.IsSourceCurrent(sourceDocument) || baseDatum == null || !backend.IsSourceCurrent(baseDatum.Source))
                    throw new InvalidOperationException("Assembly đã thay đổi. Hãy tạo lại Preview trước khi tiếp tục.");
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
                    var options = new KadaiApplyOptions(partTemplate, drawingTemplate, material, materialDatabase,
                        choices.Names, choices.UraSides, tubeMaterial, tubeDatabase);
                    step = "Tạo gói Kadai";
                    ApplyToFolder(folder.SelectedPath, options);
                }
            }
            catch (Exception ex)
            {
                previousStatus = step + " · " + ex.Message;
                Trace.WriteLine("[KADAI] " + step + System.Environment.NewLine + ex);
                if (!IsDisposed) MessageBox.Show(this, previousStatus, "Kadai", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                applying = false;
                if (!IsDisposed)
                {
                    status.Text = previousStatus;
                    Enabled = true;
                    apply.Enabled = CanApply;
                }
            }
        }

        private async Task<(Dictionary<string, string> Names, Dictionary<string, KadaiUraSide> UraSides)>
            ChoosePartNamesAsync(IReadOnlyList<KadaiProfile3D> ribs, string material, KadaiPanelSource assembly)
        {
            var workArea = Screen.FromControl(this).WorkingArea;
            bool wasVisible = Visible;
            using (var inspection = backend.BeginInspection(assembly))
            using (var dialog = new PartNamesDialog(ribs, material, backend.SetHighlightedRib,
                inspection.LookAt, inspection.VisibleSide, backend.SetInspectionIsolation,
                () => backend.IsSourceCurrent(assembly) && baseDatum != null && backend.IsSourceCurrent(baseDatum.Source)))
            {
                var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                dialog.FormClosed += (s, e) => done.TrySetResult(dialog.Names != null);
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(Math.Max(workArea.Left, workArea.Right - dialog.Width - 16),
                    Math.Max(workArea.Top, workArea.Top + (workArea.Height - dialog.Height) / 2));
                Hide();
                partOptions = dialog;
                try
                {
                    var frame = app.Frame() as Frame;
                    if (frame == null || frame.GetHWndx64() == 0) dialog.Show();
                    else dialog.Show(new SolidWorksWindow(new IntPtr(frame.GetHWndx64())));
                    return await done.Task ? (dialog.Names, dialog.UraSides) : (null, null);
                }
                finally
                {
                    partOptions = null;
                    try
                    {
                        if (!IsDisposed && backend.IsSourceCurrent(assembly))
                        {
                            backend.SetInspectionIsolation(null);
                            backend.SetHighlightedRib(null, KadaiUraSide.Unset);
                        }
                    }
                    catch (COMException ex) { Trace.WriteLine("[KADAI] Reset naming highlight: " + ex); }
                    catch (InvalidOperationException ex) { Trace.WriteLine("[KADAI] Reset naming highlight: " + ex); }
                    if (wasVisible && !IsDisposed) Show();
                }
            }
        }

        private sealed class SolidWorksWindow : IWin32Window
        {
            public IntPtr Handle { get; }
            internal SolidWorksWindow(IntPtr handle) { Handle = handle; }
        }

        private void ConfigureOrientationHoleButton()
        {
            UpdateOrientationHoleButton();
            orientationHoleButton.Click += (s, e) => EditOrientationHole();
        }

        private void UpdateOrientationHoleButton()
        {
            orientationHoleButton.Text = orientationHoleEnabled
                ? "Lỗ định hướng · Ø" + orientationHole.DiameterMm.ToString("0.##") + " mm…"
                : "Lỗ định hướng · Tắt…";
        }

        private void EditOrientationHole()
        {
            using (var dialog = new Form
            {
                Text = "Kadai · Lỗ định hướng", StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog, ClientSize = new Size(410, 245),
                AutoScaleMode = AutoScaleMode.Dpi,
                MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, Font = Font
            })
            {
                var settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
                settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 202));
                settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                var enabled = new CheckBox { Text = "Tạo lỗ định hướng", Checked = orientationHoleEnabled, AutoSize = true };
                var diameter = Number(.1m, 1000, (decimal)orientationHole.DiameterMm, 2);
                var end = Number(.1m, 99999, (decimal)orientationHole.FromEndMm, 2);
                var bottom = Number(.1m, 99999, (decimal)orientationHole.FromBaseMm, 2);
                var longitudinal = HoleEndChoice(orientationHole.LongitudinalAtFinish);
                var transverse = HoleEndChoice(orientationHole.TransverseAtFinish);
                settings.Controls.Add(enabled, 0, 0);
                settings.SetColumnSpan(enabled, 2);
                settings.Controls.Add(new Label { Text = "Đường kính Ø (mm)", AutoSize = true }, 0, 1);
                settings.Controls.Add(diameter, 1, 1);
                settings.Controls.Add(new Label { Text = "Từ đầu thanh đến tâm (mm)", AutoSize = true }, 0, 2);
                settings.Controls.Add(end, 1, 2);
                settings.Controls.Add(new Label { Text = "Từ đáy đến tâm (mm)", AutoSize = true }, 0, 3);
                settings.Controls.Add(bottom, 1, 3);
                settings.Controls.Add(new Label { Text = "Góc dưới · Nhóm Dọc", AutoSize = true }, 0, 4);
                settings.Controls.Add(longitudinal, 1, 4);
                settings.Controls.Add(new Label { Text = "Góc dưới · Nhóm Ngang", AutoSize = true }, 0, 5);
                settings.Controls.Add(transverse, 1, 5);

                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel };
                var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
                actions.Controls.AddRange(new Control[] { cancel, ok });
                settings.Controls.Add(actions, 0, 6);
                settings.SetColumnSpan(actions, 2);
                enabled.CheckedChanged += (s, e) =>
                {
                    foreach (var input in new Control[] { diameter, end, bottom, longitudinal, transverse })
                        input.Enabled = enabled.Checked;
                };
                diameter.Enabled = end.Enabled = bottom.Enabled = longitudinal.Enabled = transverse.Enabled = enabled.Checked;
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;
                dialog.Controls.Add(settings);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                orientationHoleEnabled = enabled.Checked;
                orientationHole = new KadaiOrientationHoleSettings((double)diameter.Value, (double)end.Value,
                    (double)bottom.Value, longitudinal.SelectedIndex == 1, transverse.SelectedIndex == 1);
                UpdateOrientationHoleButton();
                ResetPreview();
            }
        }

        private static ComboBox HoleEndChoice(bool finish)
        {
            var input = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            input.Items.AddRange(new[] { "Đầu thanh", "Cuối thanh" });
            input.SelectedIndex = finish ? 1 : 0;
            return input;
        }

        private string ApplyToFolder(string parentDirectory, KadaiApplyOptions options)
        {
            string directory;
            try
            {
                directory = backend.ApplyBaseLayout(layout, source, baseDatum, parentDirectory, options, SetApplyStatus, DimensionFlatView);
                source = null;
                baseDatum = null;
                UpdateSelectionStatus();
            }
            finally { ClearPreview(); }
            Close();
            return directory;
        }

        private void DimensionFlatView(ModelDoc2 drawing, SolidWorks.Interop.sldworks.View view)
        {
            if (app.IsSame(app.ActiveDoc, drawing) != (int)swObjectEquality.swObjectSame)
                throw new InvalidOperationException("Hãy kích hoạt Drawing vừa tạo để chạy DIM Kegaki.");
            drawing.ClearSelection2(true);
            if (!drawing.Extension.SelectByID2(view.GetName2(), "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0))
                throw new InvalidOperationException("Không chọn được hình chiếu 展開 để chạy DIM Kegaki.");
            new TaoDimKegaki(app).RunNative(10, false);
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

        private string SelectMaterial(string partTemplate, out string databasePath) =>
            SelectMaterialFor(partTemplate, out databasePath, "2. Chọn vật liệu, bấm Apply rồi đóng hộp thoại Material.");

        private string SelectMaterialFor(string partTemplate, out string databasePath, string instruction)
        {
            databasePath = null;
            SetApplyStatus(instruction);
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
            private sealed class UraChoice
            {
                public string Text { get; }
                public KadaiUraSide Side { get; }

                internal UraChoice(string text, KadaiUraSide side)
                {
                    Text = text;
                    Side = side;
                }
            }

            private sealed class UraSelectionGrid : DataGridView
            {
                private const int WmMouseMove = 0x0200;
                private const int WmLeftButtonDown = 0x0201;
                private const int WmLeftButtonUp = 0x0202;
                private const int WmMouseLeave = 0x02A3;
                private const int MkLeftButton = 0x0001;
                private readonly Timer scrollDuringDrag = new Timer { Interval = 70 };
                private int anchorRow = -1;
                private int endRow = -1;
                private bool selecting;
                private Rectangle dragStartArea;
                private Point pointer;
                private int selectionColumnX;
                private Message pendingMouseDown;

                internal UraSelectionGrid()
                {
                    scrollDuringDrag.Tick += (s, e) => ExtendSelection(true);
                }

                private bool BeginDrag(Message message)
                {
                    StopDrag();
                    if (ModifierKeys != Keys.None) return false;
                    var point = MousePoint(message);
                    var hit = HitTest(point.X, point.Y);
                    if (hit.Type != DataGridViewHitTestType.Cell) return false;
                    if (!ContainsFocus) Focus();
                    anchorRow = hit.RowIndex;
                    selectionColumnX = point.X;
                    pendingMouseDown = message;
                    Size threshold = SystemInformation.DragSize;
                    dragStartArea = new Rectangle(point.X - threshold.Width / 2, point.Y - threshold.Height / 2,
                        threshold.Width, threshold.Height);
                    Capture = true;
                    return true;
                }

                protected override void WndProc(ref Message message)
                {
                    if (message.Msg == WmLeftButtonDown && BeginDrag(message)) return;
                    if (anchorRow < 0)
                    {
                        base.WndProc(ref message);
                        return;
                    }
                    if (message.Msg == WmMouseLeave) return;
                    if (message.Msg == WmLeftButtonUp)
                    {
                        bool wasDrag = endRow >= 0;
                        Message click = pendingMouseDown;
                        StopDrag();
                        Capture = false;
                        // A normal click still reaches the native Part name or face editor.
                        if (!wasDrag)
                        {
                            base.WndProc(ref click);
                            base.WndProc(ref message);
                        }
                        return;
                    }
                    if (message.Msg != WmMouseMove)
                    {
                        base.WndProc(ref message);
                        return;
                    }
                    if ((message.WParam.ToInt64() & MkLeftButton) == 0)
                    {
                        StopDrag();
                        Capture = false;
                        base.WndProc(ref message);
                        return;
                    }
                    pointer = MousePoint(message);
                    if (endRow < 0)
                    {
                        if (dragStartArea.Contains(pointer) || !EndEdit() || anchorRow < 0) return;
                        CurrentCell = Rows[anchorRow].Cells[0];
                    }
                    ExtendSelection(false);
                    scrollDuringDrag.Start();
                }

                private static Point MousePoint(Message message)
                {
                    long position = message.LParam.ToInt64();
                    return new Point(unchecked((short)position), unchecked((short)(position >> 16)));
                }

                private void ExtendSelection(bool scroll)
                {
                    int first = FirstDisplayedScrollingRowIndex;
                    if (anchorRow < 0 || first < 0) return;
                    const int edge = 8;
                    int top = GetRowDisplayRectangle(first, true).Top;
                    if (scroll)
                    {
                        int step = pointer.Y < top + edge ? -1 : pointer.Y >= ClientSize.Height - edge ? 1 : 0;
                        if (step == 0 || (step < 0 && first == 0) ||
                            (step > 0 && Rows[Rows.Count - 1].Displayed &&
                             GetRowDisplayRectangle(Rows.Count - 1, false).Bottom <= ClientSize.Height))
                            return;
                        int increment = pointer.Y < top || pointer.Y >= ClientSize.Height ? 3 : 1;
                        FirstDisplayedScrollingRowIndex = Math.Max(0, Math.Min(Rows.Count - 1, first + step * increment));
                        first = FirstDisplayedScrollingRowIndex;
                    }

                    int last = Math.Min(Rows.Count - 1, first + DisplayedRowCount(true) - 1);
                    int row = HitTest(selectionColumnX, pointer.Y).RowIndex;
                    if (row < 0) row = pointer.Y < top ? first : last;
                    if (row < 0 || endRow == row) return;

                    endRow = row;
                    int start = Math.Min(anchorRow, row);
                    int end = Math.Max(anchorRow, row);
                    selecting = true;
                    try
                    {
                        foreach (DataGridViewRow item in Rows)
                        {
                            bool selected = item.Index >= start && item.Index <= end;
                            if (item.Selected != selected) item.Selected = selected;
                        }
                    }
                    finally { selecting = false; }
                    OnSelectionChanged(EventArgs.Empty);
                }

                protected override void OnSelectionChanged(EventArgs e)
                {
                    if (!selecting) base.OnSelectionChanged(e);
                }

                protected override void OnMouseCaptureChanged(EventArgs e)
                {
                    if (!Capture && anchorRow >= 0) StopDrag();
                    base.OnMouseCaptureChanged(e);
                }

                private void StopDrag()
                {
                    anchorRow = endRow = -1;
                    scrollDuringDrag.Stop();
                }

                protected override void Dispose(bool disposing)
                {
                    if (disposing)
                    {
                        StopDrag();
                        scrollDuringDrag.Dispose();
                    }
                    base.Dispose(disposing);
                }
            }

            private readonly DataGridView grid = new UraSelectionGrid
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true
            };
            private readonly Label validation = new Label { AutoSize = true, ForeColor = Color.Firebrick };
            private readonly Action refreshInspection;
            internal Dictionary<string, string> Names { get; private set; }
            internal Dictionary<string, KadaiUraSide> UraSides { get; private set; }

            internal PartNamesDialog(IReadOnlyList<KadaiProfile3D> ribs, string material,
                Action<string, KadaiUraSide> highlight, Action<KadaiProfile3D, KadaiUraSide> lookAt,
                Func<KadaiProfile3D, KadaiUraSide> visibleSide, Action<string> isolate, Func<bool> sourceIsCurrent)
            {
                Text = "Kadai · 3/5 · Tên Part và mặt ウラ";
                ClientSize = new Size(535, 490);
                MinimumSize = new Size(500, 395);
                ShowInTaskbar = false;

                var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
                content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
                content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                AddFullRow(content, Caption("Vật liệu: " + material), new RowStyle(SizeType.AutoSize));
                var longSeed = new TextBox { Text = "L-001", Dock = DockStyle.Fill };
                var crossSeed = new TextBox { Text = "C-001", Dock = DockStyle.Fill };
                AddRow(content, Caption("Tên bắt đầu · Dọc"), longSeed);
                AddRow(content, Caption("Tên bắt đầu · Ngang"), crossSeed);

                var hint = Caption("Chọn thanh trong bảng. Xoay và zoom Assembly để xem hai mặt.");
                AddFullRow(content, hint, new RowStyle(SizeType.AutoSize));
                AddFullRow(content, Caption("Kéo qua các dòng ở bất kỳ cột nào. Ctrl+A chọn tất cả."),
                    new RowStyle(SizeType.AutoSize));
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Thanh", ReadOnly = true,
                    SortMode = DataGridViewColumnSortMode.NotSortable, FillWeight = 38 });
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tên Part", SortMode = DataGridViewColumnSortMode.NotSortable });
                var sides = new DataGridViewComboBoxColumn
                {
                    HeaderText = "Mặt ウラ",
                    SortMode = DataGridViewColumnSortMode.NotSortable,
                    DataSource = new[]
                    {
                        new UraChoice("Chưa chọn", KadaiUraSide.Unset),
                        new UraChoice("Mặt 1", KadaiUraSide.First),
                        new UraChoice("Mặt 2", KadaiUraSide.Second)
                    },
                    DisplayMember = "Text",
                    ValueMember = "Side",
                    FillWeight = 54
                };
                grid.Columns.Add(sides);
                foreach (var rib in ribs)
                    grid.Rows[grid.Rows.Add(rib.RibId, rib.RibId, KadaiUraSide.Unset)].Tag = rib;
                AddFullRow(content, grid, new RowStyle(SizeType.Percent, 100));
                var look = new Button { Text = "Xem rõ thanh", AutoSize = true };
                var opposite = new Button { Text = "Xem mặt đối diện", AutoSize = true };
                var isolation = new CheckBox { Text = "Ẩn thanh Preview khác", AutoSize = true, Anchor = AnchorStyles.Left };
                var views = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
                views.Controls.AddRange(new Control[] { look, opposite, isolation });
                AddFullRow(content, views, new RowStyle(SizeType.AutoSize));
                var choose = new Button { Text = "Chọn mặt đang xem làm ウラ", AutoSize = true };
                var reverse = new Button { Text = "Đảo mặt ウラ", AutoSize = true, Enabled = false };
                var group = new Button { Text = "Áp dụng cùng phương", AutoSize = true, Enabled = false };
                var sideActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
                sideActions.Controls.AddRange(new Control[] { choose, reverse, group });
                AddFullRow(content, sideActions, new RowStyle(SizeType.AutoSize));
                var progress = Caption("");
                AddFullRow(content, progress, new RowStyle(SizeType.AutoSize));
                AddFullRow(content, validation, new RowStyle(SizeType.AutoSize));
                var next = new Button { Text = "Tiếp tục", AutoSize = true };
                var cancel = new Button { Text = "Hủy", AutoSize = true };
                var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
                actions.Controls.AddRange(new Control[] { next, cancel });
                AddFullRow(content, actions, new RowStyle(SizeType.AutoSize));
                Controls.Add(content);
                AcceptButton = next;
                CancelButton = cancel;

                var viewing = KadaiUraSide.Unset;
                var visibleSides = new Dictionary<KadaiRibDirection, KadaiUraSide>();
                bool settingSides = false;
                DataGridViewRow[] SelectedRibs() => grid.SelectedRows.Cast<DataGridViewRow>().ToArray();

                void RefreshInspection(bool draw)
                {
                    if (!(grid.CurrentRow?.Tag is KadaiProfile3D rib)) return;
                    var side = (KadaiUraSide)grid.CurrentRow.Cells[2].Value;
                    var selection = SelectedRibs();
                    try
                    {
                        bool available = sourceIsCurrent();
                        if (available && draw)
                        {
                            isolate(isolation.Checked ? rib.RibId : null);
                            highlight(rib.RibId, side);
                        }
                        viewing = available ? visibleSide(rib) : KadaiUraSide.Unset;
                        visibleSides.Clear();
                        if (available)
                        {
                            visibleSides[rib.Direction] = viewing;
                            foreach (var row in selection)
                            {
                                var target = (KadaiProfile3D)row.Tag;
                                if (!visibleSides.ContainsKey(target.Direction))
                                    visibleSides[target.Direction] = visibleSide(target);
                            }
                        }
                        look.Enabled = isolation.Enabled = next.Enabled = available;
                        opposite.Enabled = available && viewing != KadaiUraSide.Unset;
                        choose.Enabled = available && selection.Length != 0 &&
                            selection.All(row => visibleSides[((KadaiProfile3D)row.Tag).Direction] != KadaiUraSide.Unset);
                        reverse.Enabled = available && selection.Length != 0 &&
                            selection.All(row => (KadaiUraSide)row.Cells[2].Value != KadaiUraSide.Unset);
                        group.Enabled = available && selection.Length == 1 && grid.CurrentRow.Selected && side != KadaiUraSide.Unset;
                        if (!available) hint.Text = "Quay lại Assembly đã Preview. Nếu nó đã thay đổi, tạo lại Preview.";
                        else if (viewing == KadaiUraSide.Unset) hint.Text = rib.RibId + " · Hãy xoay để thấy mặt rộng, hoặc bấm Xem rõ thanh.";
                        else if (selection.Length > 1 && !choose.Enabled) hint.Text = "Xoay Assembly để thấy mặt rộng của mọi thanh đã chọn.";
                        else
                        {
                            string face = viewing == KadaiUraSide.First ? "Mặt 1" : "Mặt 2";
                            string role = side == KadaiUraSide.Unset ? "chưa chọn ウラ" : side == viewing ? "ウラ · màu hồng" : "表 · mặt lên bản vẽ";
                            hint.Text = rib.RibId + " · Đang xem " + face + " · " + role;
                        }
                        progress.Text = (selection.Length > 1 ? "Đang chọn " + selection.Length + " thanh · " : "") +
                            grid.Rows.Cast<DataGridViewRow>().Count(row => (KadaiUraSide)row.Cells[2].Value != KadaiUraSide.Unset)
                            + "/" + ribs.Count + " thanh đã chọn mặt ウラ";
                    }
                    catch (Exception ex) when (ex is COMException || ex is InvalidOperationException)
                    {
                        choose.Enabled = opposite.Enabled = reverse.Enabled = group.Enabled = next.Enabled = false;
                        hint.Text = "Không thể xem thanh " + rib.RibId + " trong Assembly.";
                        Trace.WriteLine("[KADAI] Naming highlight: " + ex);
                    }
                }

                void SetSides(IEnumerable<DataGridViewRow> rows, Func<DataGridViewRow, KadaiUraSide> getSide)
                {
                    settingSides = true;
                    try
                    {
                        foreach (var row in rows) row.Cells[2].Value = getSide(row);
                    }
                    finally { settingSides = false; }
                    RefreshInspection(true);
                }

                refreshInspection = () => RefreshInspection(false);
                void FocusFace(bool flip)
                {
                    if (!(grid.CurrentRow?.Tag is KadaiProfile3D rib)) return;
                    try
                    {
                        var side = viewing == KadaiUraSide.Unset ? KadaiUraSide.First : viewing;
                        lookAt(rib, flip ? Opposite(side) : side);
                        RefreshInspection(false);
                    }
                    catch (Exception ex) when (ex is COMException || ex is InvalidOperationException) { validation.Text = ex.Message; }
                }
                look.Click += (s, e) => FocusFace(false);
                opposite.Click += (s, e) => FocusFace(true);
                isolation.CheckedChanged += (s, e) => RefreshInspection(true);
                choose.Click += (s, e) =>
                {
                    RefreshInspection(false);
                    if (choose.Enabled)
                        SetSides(SelectedRibs(), row => visibleSides[((KadaiProfile3D)row.Tag).Direction]);
                };
                grid.CurrentCellChanged += (s, e) => RefreshInspection(true);
                grid.SelectionChanged += (s, e) => RefreshInspection(false);
                grid.CurrentCellDirtyStateChanged += (s, e) =>
                {
                    if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewComboBoxCell)
                        grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                };
                grid.CellValueChanged += (s, e) =>
                {
                    if (!settingSides && e.ColumnIndex == sides.Index) RefreshInspection(e.RowIndex == grid.CurrentCell?.RowIndex);
                };
                reverse.Click += (s, e) =>
                {
                    RefreshInspection(false);
                    if (reverse.Enabled)
                        SetSides(SelectedRibs(), row => Opposite((KadaiUraSide)row.Cells[2].Value));
                };
                group.Click += (s, e) =>
                {
                    RefreshInspection(false);
                    if (!group.Enabled) return;
                    var selected = (KadaiProfile3D)grid.CurrentRow.Tag;
                    var side = (KadaiUraSide)grid.CurrentRow.Cells[2].Value;
                    SetSides(grid.Rows.Cast<DataGridViewRow>().Where(row => ((KadaiProfile3D)row.Tag).Direction == selected.Direction), row => side);
                };
                Shown += (s, e) => RefreshInspection(true);
                cancel.Click += (s, e) => Close();
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
                        var uraSides = grid.Rows.Cast<DataGridViewRow>().ToDictionary(row => ((KadaiProfile3D)row.Tag).RibId,
                            row => (KadaiUraSide)row.Cells[2].Value, StringComparer.Ordinal);
                        var missing = uraSides.FirstOrDefault(entry => entry.Value == KadaiUraSide.Unset).Key;
                        if (missing != null)
                            throw new InvalidOperationException("Chọn mặt ウラ cho thanh " + missing + ".");
                        Names = names;
                        UraSides = uraSides;
                        Close();
                    }
                    catch (InvalidOperationException ex) { validation.Text = ex.Message; }
                };
            }

            private static KadaiUraSide Opposite(KadaiUraSide side)
            { return side == KadaiUraSide.First ? KadaiUraSide.Second : KadaiUraSide.First; }

            internal void RefreshInspectionState() { if (Visible) refreshInspection(); }

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
            partOptions?.RefreshInspectionState();
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
            if (disposing)
            {
                lifecycle.Stop();
                lifecycle.Dispose();
                partOptions?.Close();
                backend.Dispose();
            }
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
