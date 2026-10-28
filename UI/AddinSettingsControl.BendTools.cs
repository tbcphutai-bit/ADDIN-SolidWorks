using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ADDIN.Commands;

namespace ADDIN.UI
{
    internal sealed partial class AddinSettingsControl
    {
        private readonly BendToolStorageSettings bendToolSettings;
        private readonly TextBox bendToolPath = new TextBox();
        private readonly ModernButton bendToolChoose = new ModernButton();
        private readonly Label bendToolError = new Label();
        private readonly System.Windows.Forms.Timer bendToolTimer = new System.Windows.Forms.Timer();
        private readonly CancellationTokenSource bendToolLifetime = new CancellationTokenSource();
        private BendToolCatalogSnapshot bendToolLastValid;
        private string bendToolSavedPath;
        private bool bendToolBusy, bendToolDisposed, bendToolLifetimeDisposed;
        private bool settingsSaving, bendToolLoadingPath;
        private int bendToolGeneration;

        private Control CreateBendToolCard()
        {
            var card = new ModernCard
            {
                Text = "DAO CHẤN (BEND TOOLS)",
                HeaderStyle = ModernCardHeaderStyle.DarkNavy,
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12, 42, 12, 12),
                Margin = new Padding(0, 0, 0, 8)
            };

            var content = new TableLayoutPanel
            {
                Name = "bendToolSettingsCard",
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = TaiTheme.Surface,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var lblTitle = new Label
            {
                Text = "File Excel dao chấn (.xlsx)",
                Font = TaiTheme.SmallStrong,
                ForeColor = TaiTheme.Text,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };
            content.Controls.Add(lblTitle, 0, 0);

            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74f));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            bendToolPath.Name = "bendToolExcelPath";
            bendToolPath.Dock = DockStyle.Fill;
            bendToolPath.Font = TaiTheme.Body;
            bendToolPath.BorderStyle = BorderStyle.FixedSingle;
            bendToolPath.BackColor = TaiTheme.Surface;
            bendToolPath.Margin = new Padding(0, 2, 6, 0);

            StyleButton(bendToolChoose, "Browse…", ModernButtonKind.Standard, TaiIcon.None, 70);
            bendToolChoose.Name = "browseBendToolExcel";
            bendToolChoose.Dock = DockStyle.Fill;
            bendToolChoose.Margin = Padding.Empty;
            bendToolChoose.Click += (s, e) => ChooseBendToolExcel();

            row.Controls.Add(bendToolPath, 0, 0);
            row.Controls.Add(bendToolChoose, 1, 0);
            content.Controls.Add(row, 0, 1);

            bendToolError.Name = "bendToolPathError";
            bendToolError.AutoSize = true;
            bendToolError.Dock = DockStyle.Top;
            bendToolError.Font = TaiTheme.Small;
            bendToolError.ForeColor = TaiTheme.Danger;
            bendToolError.Margin = new Padding(0, 4, 0, 0);
            bendToolError.Visible = false;
            content.Controls.Add(bendToolError, 0, 2);
            content.SizeChanged += (s, e) => bendToolError.MaximumSize = new Size(Math.Max(100, content.Width - 12), 0);

            var sourceButton=new Button {Text="Nguồn DXF / Master / Excel Check Chấn…",AutoSize=true,Dock=DockStyle.Top,
                Margin=new Padding(0,8,0,0)};
            sourceButton.Click+=(sender,args)=>{
                if(bendToolDisposed)return;sourceButton.Enabled=false;
                BendAssemblyBatchForm.ConfigureToolSourcesAsync(this).ContinueWith(t=>{
                    var observed=t.Exception;
                    PublishBendToolCompletion(()=>{if(!sourceButton.IsDisposed)sourceButton.Enabled=true;},()=>{});
                },TaskScheduler.Default);
            };
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.Controls.Add(sourceButton,0,3);
            card.Controls.Add(content);

            bendToolPath.TextChanged += (s, e) => {
                if (bendToolLoadingPath) return;
                bendToolError.Visible = false;
                DraftChanged();
            };
            bendToolTimer.Interval = 5000;
            bendToolTimer.Tick += (s, e) => RefreshBendToolCatalog();
            HandleCreated += (s, e) => { if (Visible) { bendToolTimer.Start(); RefreshBendToolCatalog(); } };
            VisibleChanged += (s, e) => {
                if (bendToolDisposed) return;
                if (Visible && IsHandleCreated) { bendToolTimer.Start(); RefreshBendToolCatalog(); }
                else bendToolTimer.Stop();
            };
            Disposed += (s, e) => {
                if (bendToolDisposed) return;
                bendToolDisposed = true;
                bendToolTimer.Stop(); bendToolTimer.Dispose();
                bendToolLifetime.Cancel();
                DisposeBendToolLifetimeIfIdle();
            };
            return card;
        }

        private void DisposeBendToolLifetimeIfIdle()
        {
            if (!bendToolDisposed || bendToolBusy || settingsSaving || bendToolLifetimeDisposed) return;
            bendToolLifetimeDisposed = true;
            bendToolLifetime.Dispose();
        }

        private bool IsBendToolPathChanged()
        {
            return !string.Equals(bendToolPath.Text.Trim().Trim('"'), bendToolSavedPath ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private void ResetBendToolPathDraft()
        {
            bendToolLoadingPath = true;
            bendToolGeneration++;
            try {
                bendToolSavedPath = bendToolSettings.LoadPath();
                bendToolPath.Text = bendToolSavedPath ?? "";
                bendToolError.Visible = false;
            }
            catch (Exception ex) { SetBendToolError(ex.Message); }
            finally { bendToolLoadingPath = false; }
        }

        private void ChooseBendToolExcel()
        {
            using (var picker = new OpenFileDialog {
                Title = "Chọn file Excel dao chấn", Filter = "Excel (*.xlsx)|*.xlsx",
                CheckFileExists = true, Multiselect = false
            }) {
                try { if (File.Exists(bendToolPath.Text)) picker.FileName = bendToolPath.Text; } catch { }
                if (picker.ShowDialog(this) == DialogResult.OK) bendToolPath.Text = picker.FileName;
            }
        }

        // SolidWorks owns the native message loop. Marshal completions explicitly
        // instead of depending on an ambient WinForms synchronization context.
        private void PublishBendToolCompletion(Action completed, Action abandoned)
        {
            if (bendToolDisposed || IsDisposed) { abandoned(); return; }
            try {
                BeginInvoke(new Action(() => {
                    if (bendToolDisposed || IsDisposed) abandoned();
                    else completed();
                }));
            }
            catch (InvalidOperationException) { abandoned(); }
        }

        private void FinishSettingsSave()
        {
            settingsSaving = false;
            if (bendToolDisposed) DisposeBendToolLifetimeIfIdle();
            else UpdateDraftControls();
        }

        private void SaveChanges()
        {
            if (settingsSaving || bendToolDisposed || !IsDraftChanged()) return;
            bool saveTools = IsBendToolPathChanged();
            bool saveTables = IsBendTableDraftChanged();
            string path;
            try { path = saveTools ? BendToolStorageSettings.NormalizePath(bendToolPath.Text) : null; }
            catch (Exception ex) { SetBendToolError(ex.Message); ShowStatus("Chưa lưu: " + ex.Message, true); return; }
            CancellationToken cancellation = bendToolLifetime.Token;
            settingsSaving = true;
            bendToolGeneration++;
            UpdateDraftControls();
            ShowStatus("Đang lưu cài đặt…", false);
            // A bad source is rejected before writing any other drafts.
            Task.Run(() => saveTools ? BendToolExcelCatalog.Load(path, cancellation) : null, cancellation)
                .ContinueWith(validation => PublishBendToolCompletion(() => {
                    if (validation.IsCanceled) { FinishSettingsSave(); return; }
                    if (validation.IsFaulted) {
                        string error = validation.Exception.GetBaseException().Message;
                        SetBendToolError(error); ShowStatus("Chưa lưu: " + error, true);
                        FinishSettingsSave(); return;
                    }
                    if (saveTables && !SaveBendTableChanges()) { FinishSettingsSave(); return; }
                    if (!saveTools) { ShowStatus("✓ Đã lưu cài đặt.", false, true); FinishSettingsSave(); return; }
                    Task.Run(() => bendToolSettings.SavePath(path, cancellation), cancellation)
                        .ContinueWith(save => PublishBendToolCompletion(() => {
                            try {
                                if (save.IsCanceled) return;
                                if (save.IsFaulted) {
                                    string error = save.Exception.GetBaseException().Message;
                                    SetBendToolError(error);
                                    ShowStatus((saveTables ? "Đã lưu Bend Table; chưa lưu đường dẫn dao chấn: " : "Chưa lưu: ") + error, true);
                                    return;
                                }
                                bendToolSavedPath = path; bendToolLastValid = save.Result;
                                bendToolLoadingPath = true;
                                try { bendToolPath.Text = path; }
                                finally { bendToolLoadingPath = false; }
                                bendToolError.Visible = false;
                                ShowStatus("✓ Đã lưu cài đặt.", false, true);
                            }
                            finally { FinishSettingsSave(); }
                        }, FinishSettingsSave), TaskScheduler.Default);
                }, FinishSettingsSave), TaskScheduler.Default);
        }

        private void RefreshBendToolCatalog()
        {
            if (bendToolDisposed || IsDisposed || bendToolBusy || settingsSaving || !Visible || !IsHandleCreated) return;
            string path;
            try { path = bendToolSettings.LoadPath(); }
            catch (Exception ex) { if (!IsBendToolPathChanged()) SetBendToolError(ex.Message); return; }
            if (path == null) return;
            int generation = bendToolGeneration;
            CancellationToken cancellation = bendToolLifetime.Token;
            BendToolCatalogSnapshot previous = bendToolLastValid;
            bendToolBusy = true;
            Action finished = () => { bendToolBusy = false; DisposeBendToolLifetimeIfIdle(); };
            Task.Run(() => BendToolExcelCatalog.Load(path, cancellation, previous), cancellation)
                .ContinueWith(read => PublishBendToolCompletion(() => {
                    try {
                        if (read.IsCanceled || generation != bendToolGeneration || settingsSaving) return;
                        if (read.IsFaulted) {
                            if (!IsBendToolPathChanged()) SetBendToolError(read.Exception.GetBaseException().Message);
                            return;
                        }
                        bendToolLastValid = read.Result;
                        if (!IsBendToolPathChanged()) bendToolError.Visible = false;
                    }
                    finally { finished(); }
                }, finished), TaskScheduler.Default);
        }

        private void SetBendToolError(string message)
        {
            if (bendToolDisposed) return;
            bendToolError.Text = "Không đọc được file dao chấn: " + message;
            bendToolError.Visible = true;
        }
    }
}
