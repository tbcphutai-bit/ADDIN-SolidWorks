using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ADDIN.Commands;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.UI
{
    internal sealed partial class AddinSettingsControl : UserControl
    {
        private readonly BendTableStorageSettings settings;
        private ISldWorks swApp;

        // Bend Table Excel source controls
        private readonly CheckBox useCustomBendStandards = new CheckBox();
        private readonly TextBox excelFolderBox = new TextBox();
        private readonly ModernButton browseExcelBtn = new ModernButton();
        private readonly ModernButton btnScanExcel = new ModernButton();
        private readonly ModernButton btnOpenFolder = new ModernButton();
        private readonly ModernButton btnUseDefault = new ModernButton();
        private readonly ModernButton btnDetailFiles = new ModernButton();
        private readonly Label lblBendSummary = new Label();

        // Database Cache controls
        private readonly CheckBox chkCustomCache = new CheckBox();
        private readonly TextBox cacheFolderBox = new TextBox();
        private readonly ModernButton browseCacheBtn = new ModernButton();
        private Control cacheBox;

        // Sticky Bottom Bar controls
        private readonly Label statusLabel = new Label();
        private readonly ModernButton saveButton = new ModernButton();
        private readonly ModernButton cancelButton = new ModernButton();

        // State trackers
        private string savedExcelDirectory;
        private string savedCacheDirectory;
        private string customExcelDirectory;
        private bool savedUseCustomStandards;
        private BendTableStorageSelection savedSelection;
        private bool loading, loadFailed;

        internal AddinSettingsControl() : this(new BendTableStorageSettings()) { }

        internal AddinSettingsControl(BendTableStorageSettings store)
            : this(store, new BendToolStorageSettings()) { }

        internal AddinSettingsControl(BendTableStorageSettings store, BendToolStorageSettings bendTools)
        {
            settings = store;
            bendToolSettings = bendTools;
            Name = "addinSettingsControl";
            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = TaiTheme.Body;
            ForeColor = TaiTheme.Text;
            BackColor = TaiTheme.Page;
            Padding = Padding.Empty;

            BuildLayout();
            WireEvents();
            LoadSavedLocation();
        }

        internal void SetSldWorks(ISldWorks app)
        {
            swApp = app;
        }

        internal void ReloadIfUnchanged()
        {
            if (!IsDraftChanged()) LoadSavedLocation();
        }

        private void BuildLayout()
        {
            // 1. Sticky Bottom Action Bar
            var bottomBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                BackColor = TaiTheme.Surface,
                Padding = new Padding(10, 6, 10, 6)
            };
            bottomBar.Paint += (s, e) =>
            {
                using (var pen = new Pen(TaiTheme.Border, 1f))
                    e.Graphics.DrawLine(pen, 0, 0, bottomBar.Width, 0);
            };

            statusLabel.Dock = DockStyle.Fill;
            statusLabel.Font = TaiTheme.SmallStrong;
            statusLabel.ForeColor = TaiTheme.TextMuted;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.AutoEllipsis = true;

            var actionButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.Transparent
            };

            StyleButton(cancelButton, "Hủy", ModernButtonKind.Standard, TaiIcon.Undo, 64);
            cancelButton.Margin = new Padding(0, 0, 6, 0);

            StyleButton(saveButton, "✓ Lưu", ModernButtonKind.Primary, TaiIcon.Check, 74);
            saveButton.Name = "saveAddinSettings";
            saveButton.Margin = Padding.Empty;

            actionButtons.Controls.Add(cancelButton);
            actionButtons.Controls.Add(saveButton);

            bottomBar.Controls.Add(statusLabel);
            bottomBar.Controls.Add(actionButtons);
            Controls.Add(bottomBar);

            // 2. Scrollable Body
            var scrollBody = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = TaiTheme.Page,
                Padding = new Padding(8, 8, 8, 8)
            };
            Controls.Add(scrollBody);
            // Dock Fill must be laid out after the bottom bar so it cannot
            // cover the Save/Cancel buttons with the scrolling surface.
            scrollBody.BringToFront();

            var cardsPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            cardsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            cardsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cardsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            cardsPanel.Controls.Add(CreateBendToolCard(), 0, 0);
            // Existing Bend Table/Kegaki preferences remain independent.
            cardsPanel.Controls.Add(CreateBendTableCard(), 0, 1);

            // Card 2: Đường dẫn tham chiếu mặc định của SOLIDWORKS
            cardsPanel.Controls.Add(CreateReferenceDefaultsCard(), 0, 2);

            scrollBody.Controls.Add(cardsPanel);
        }

        private Control CreateBendTableCard()
        {
            var card = new ModernCard
            {
                Text = "BẢNG UỐN (BEND TABLES)",
                HeaderStyle = ModernCardHeaderStyle.DarkNavy,
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12, 42, 12, 12),
                Margin = new Padding(0, 0, 0, 8)
            };

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 7,
                BackColor = TaiTheme.Surface,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 7; i++) content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            useCustomBendStandards.Name = "useCustomBendStandards";
            useCustomBendStandards.Text = "Dùng bộ Excel chuẩn riêng";
            useCustomBendStandards.AutoSize = true;
            useCustomBendStandards.Font = TaiTheme.SmallStrong;
            useCustomBendStandards.Margin = new Padding(0, 0, 0, 8);
            content.Controls.Add(useCustomBendStandards, 0, 0);

            // 1. Thư mục Excel (Bend Tables)
            var excelBox = CreatePathField("Thư mục Excel (Bend Tables)", excelFolderBox, browseExcelBtn, (s, e) => BrowseExcelFolder());
            excelFolderBox.Name = "bendExcelFolder";
            content.Controls.Add(excelBox, 0, 1);

            // 2. 2x2 Grid of action buttons (Responsive 50%-50%, no text clipping)
            var buttonsGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 8),
                Padding = Padding.Empty
            };
            buttonsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            buttonsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            buttonsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            buttonsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));

            StyleButton(btnScanExcel, "⚡ Quét lại Excel", ModernButtonKind.Primary, TaiIcon.None, 0);
            btnScanExcel.Dock = DockStyle.Fill;
            btnScanExcel.Margin = new Padding(0, 0, 3, 4);
            btnScanExcel.Click += (s, e) => ScanAndSyncExcel();

            StyleButton(btnOpenFolder, "📂 Mở thư mục", ModernButtonKind.Standard, TaiIcon.None, 0);
            btnOpenFolder.Dock = DockStyle.Fill;
            btnOpenFolder.Margin = new Padding(3, 0, 0, 4);
            btnOpenFolder.Click += (s, e) => OpenExcelFolderInExplorer();

            StyleButton(btnUseDefault, "↺ Mặc định", ModernButtonKind.Standard, TaiIcon.None, 0);
            btnUseDefault.Dock = DockStyle.Fill;
            btnUseDefault.Margin = new Padding(0, 0, 3, 0);
            btnUseDefault.Click += (s, e) => ResetExcelToDefault();

            StyleButton(btnDetailFiles, "⚙ Gán chi tiết…", ModernButtonKind.Standard, TaiIcon.None, 0);
            btnDetailFiles.Name = "chooseBendStandards";
            btnDetailFiles.Dock = DockStyle.Fill;
            btnDetailFiles.Margin = new Padding(3, 0, 0, 0);
            btnDetailFiles.Click += (s, e) => OpenBendStandardFilesDialog();

            buttonsGrid.Controls.Add(btnScanExcel, 0, 0);
            buttonsGrid.Controls.Add(btnOpenFolder, 1, 0);
            buttonsGrid.Controls.Add(btnUseDefault, 0, 1);
            buttonsGrid.Controls.Add(btnDetailFiles, 1, 1);
            content.Controls.Add(buttonsGrid, 0, 2);

            // 3. Status summary of detected tables
            lblBendSummary.AutoSize = true;
            lblBendSummary.Dock = DockStyle.Top;
            lblBendSummary.Font = TaiTheme.SmallStrong;
            lblBendSummary.ForeColor = TaiTheme.Success;
            lblBendSummary.Margin = new Padding(0, 2, 0, 6);
            lblBendSummary.Name = "bendStandardsSummary";
            content.Controls.Add(lblBendSummary, 0, 3);

            // 4. Custom Cache checkbox
            chkCustomCache.Text = "Tùy chỉnh thư mục lưu Cache database.json";
            chkCustomCache.Font = TaiTheme.Small;
            chkCustomCache.ForeColor = TaiTheme.TextMuted;
            chkCustomCache.AutoSize = true;
            chkCustomCache.Margin = new Padding(0, 2, 0, 4);
            content.Controls.Add(chkCustomCache, 0, 4);

            // 5. Cache folder Box (Hidden by default, shown only when custom checked)
            cacheBox = CreatePathField("Vị trí Cache database.json", cacheFolderBox, browseCacheBtn, (s, e) => BrowseCacheFolder());
            cacheBox.Visible = false;
            content.Controls.Add(cacheBox, 0, 5);

            // 6. Note caption
            var noteLabel = new Label
            {
                Text = "ℹ️ Cache tự động cập nhật ngầm từ Excel nguồn mỗi lần chạy CHECK.",
                Font = TaiTheme.Small,
                ForeColor = TaiTheme.TextMuted,
                AutoSize = true,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 4, 0, 2)
            };
            content.Controls.Add(noteLabel, 0, 6);

            card.Controls.Add(content);
            return card;
        }

        private Control CreateReferenceDefaultsCard()
        {
            var card = new ModernCard
            {
                Text = "THAM CHIẾU SOLIDWORKS",
                HeaderStyle = ModernCardHeaderStyle.DarkNavy,
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12, 42, 12, 14),
                Margin = new Padding(0, 0, 0, 8)
            };

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = TaiTheme.Surface
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var desc = new Label
            {
                Text = "Chỉ thay đổi khi bạn chủ động bấm nút. Add-in không còn tự động đặt lại khi khởi động.",
                ForeColor = TaiTheme.TextMuted,
                Font = TaiTheme.Small,
                AutoSize = true,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 8)
            };
            content.Controls.Add(desc, 0, 0);

            var btnReset = new ModernButton();
            StyleButton(btnReset, "↺ Đặt lại đường dẫn mặc định", ModernButtonKind.Standard, TaiIcon.None, 0);
            btnReset.Dock = DockStyle.Top;
            btnReset.Height = 34;
            btnReset.Click += (s, e) => ResetSolidWorksDefaultReferences();
            content.Controls.Add(btnReset, 0, 1);

            card.Controls.Add(content);
            return card;
        }

        private static Control CreatePathField(string title, TextBox textBox, ModernButton browseBtn, EventHandler onBrowse)
        {
            var container = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 8),
                Padding = Padding.Empty
            };
            container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            container.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));

            var lbl = new Label
            {
                Text = title,
                Font = TaiTheme.SmallStrong,
                ForeColor = TaiTheme.Text,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };
            container.Controls.Add(lbl, 0, 0);

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

            textBox.Dock = DockStyle.Fill;
            textBox.Margin = new Padding(0, 2, 6, 0);
            textBox.Font = TaiTheme.Body;
            textBox.BorderStyle = BorderStyle.FixedSingle;

            StyleButton(browseBtn, "Chọn…", ModernButtonKind.Standard, TaiIcon.None, 70);
            browseBtn.Dock = DockStyle.Fill;
            browseBtn.Margin = Padding.Empty;
            browseBtn.Click += onBrowse;

            row.Controls.Add(textBox, 0, 0);
            row.Controls.Add(browseBtn, 1, 0);
            container.Controls.Add(row, 0, 1);
            return container;
        }

        private void WireEvents()
        {
            excelFolderBox.TextChanged += (s, e) => ExcelDirectoryChanged();
            useCustomBendStandards.CheckedChanged += (s, e) => StandardModeChanged();
            cacheFolderBox.TextChanged += (s, e) => DraftChanged();
            chkCustomCache.CheckedChanged += (s, e) =>
            {
                cacheBox.Visible = chkCustomCache.Checked;
                cacheFolderBox.Enabled = browseCacheBtn.Enabled = chkCustomCache.Checked;
                if (!chkCustomCache.Checked) cacheFolderBox.Text = settings.DefaultDirectory;
                DraftChanged();
            };
            saveButton.Click += (s, e) => SaveChanges();
            cancelButton.Click += (s, e) => LoadSavedLocation();
        }

        private void BrowseCacheFolder()
        {
            using (var browser = new FolderBrowserDialog
            {
                Description = "Chọn thư mục lưu file database.json",
                ShowNewFolderButton = true
            })
            {
                if (Directory.Exists(cacheFolderBox.Text)) browser.SelectedPath = cacheFolderBox.Text;
                if (browser.ShowDialog(this) != DialogResult.OK) return;
                cacheFolderBox.Text = browser.SelectedPath;
                DraftChanged();
            }
        }

        private void LoadSavedLocation()
        {
            loading = true;
            ResetBendToolPathDraft();
            try
            {
                BendTableStorageSelection selection = settings.LoadSelection();
                LoadStandardSelection(selection);
                savedCacheDirectory = selection.CustomDatabaseDirectory;
                savedExcelDirectory = selection.CustomSourceDirectory ?? selection.StandardProfile?.SourceDirectory;
                customExcelDirectory = savedExcelDirectory;
                savedUseCustomStandards = selection.UseCustomStandards;
                savedSelection = selection;
                useCustomBendStandards.Checked = selection.UseCustomStandards;
                loadFailed = false;

                excelFolderBox.Text = selection.UseCustomStandards ? savedExcelDirectory ?? "" : settings.DefaultSourceDirectory;
                chkCustomCache.Checked = savedCacheDirectory != null;
                cacheBox.Visible = chkCustomCache.Checked;
                cacheFolderBox.Text = savedCacheDirectory ?? settings.DefaultDirectory;

                RefreshExcelSummary();
                ShowStatus("✓ Cài đặt đã sẵn sàng.", false, true);
            }
            catch (Exception ex)
            {
                loadFailed = true;
                standardProfile = null;
                customExcelDirectory = null;
                useCustomBendStandards.Checked = false;
                excelFolderBox.Text = settings.DefaultSourceDirectory;
                chkCustomCache.Checked = false;
                cacheBox.Visible = false;
                cacheFolderBox.Text = settings.DefaultDirectory;
                ShowStatus(ex.Message, true);
            }
            finally
            {
                loading = false;
                UpdateDraftControls();
            }
        }

        private bool IsDraftChanged()
        {
            return IsBendToolPathChanged() || IsBendTableDraftChanged();
        }

        private bool IsBendTableDraftChanged()
        {
            if (loadFailed || StandardsChanged()) return true;
            if (useCustomBendStandards.Checked != savedUseCustomStandards) return true;
            if (!string.Equals(customExcelDirectory ?? "", savedExcelDirectory ?? "", StringComparison.OrdinalIgnoreCase)) return true;
            if (chkCustomCache.Checked != (savedCacheDirectory != null)) return true;
            if (chkCustomCache.Checked && !string.Equals(cacheFolderBox.Text.Trim(), savedCacheDirectory, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void DraftChanged()
        {
            if (loading) return;
            UpdateDraftControls();
            if (IsDraftChanged()) ShowStatus("● Có thay đổi chưa lưu.", false, false);
        }

        private void UpdateDraftControls()
        {
            excelFolderBox.ReadOnly = !useCustomBendStandards.Checked;
            browseExcelBtn.Enabled = btnDetailFiles.Enabled = useCustomBendStandards.Checked;
            btnUseDefault.Enabled = useCustomBendStandards.Checked;
            cacheFolderBox.Enabled = browseCacheBtn.Enabled = chkCustomCache.Checked;
            saveButton.Enabled = !settingsSaving && IsDraftChanged();
            cancelButton.Enabled = !settingsSaving && IsDraftChanged();
            bendToolPath.ReadOnly = settingsSaving;
            bendToolChoose.Enabled = !settingsSaving;
        }

        private bool SaveBendTableChanges()
        {
            try
            {
                bool useCustomStandards = useCustomBendStandards.Checked;
                string customCacheDir = chkCustomCache.Checked ? cacheFolderBox.Text.Trim() : null;

                BendTableStorageSelection selection = settings.SaveSelection(
                    standardProfile,
                    customCacheDir,
                    useCustomStandards,
                    customExcelDirectory,
                    savedSelection?.Tolerance ?? "0.01",
                    savedSelection?.Scale ?? "1:1",
                    savedSelection?.ExportExt ?? "dxf",
                    savedSelection?.QtyType ?? "数量+Qty"
                );

                loading = true;
                LoadStandardSelection(selection);
                savedExcelDirectory = selection.CustomSourceDirectory;
                customExcelDirectory = savedExcelDirectory;
                savedUseCustomStandards = selection.UseCustomStandards;
                savedSelection = selection;
                savedCacheDirectory = selection.CustomDatabaseDirectory;
                loadFailed = false;

                excelFolderBox.Text = selection.UseCustomStandards ? savedExcelDirectory ?? "" : settings.DefaultSourceDirectory;
                cacheFolderBox.Text = savedCacheDirectory ?? settings.DefaultDirectory;
                RefreshExcelSummary();

                return true;
            }
            catch (Exception ex)
            {
                ShowStatus("Chưa lưu: " + ex.Message, true);
                return false;
            }
            finally
            {
                loading = false;
                UpdateDraftControls();
            }
        }

        private void ResetSolidWorksDefaultReferences()
        {
            try
            {
                string defaultLib = @"C:\sw共有ライブラリ\共有ライブラリ\無加工品";
                if (swApp != null)
                {
                    try
                    {
                        swApp.SetSearchFolders((int)swSearchFolderTypes_e.swDocumentType, defaultLib);
                    }
                    catch { }
                }
                ShowStatus("✓ Đã đặt lại đường dẫn tham chiếu: " + defaultLib, false, true);
            }
            catch (Exception ex)
            {
                ShowStatus("Lỗi đặt lại: " + ex.Message, true);
            }
        }

        private void ShowStatus(string message, bool error, bool success = false)
        {
            statusLabel.Text = message;
            statusLabel.ForeColor = error ? TaiTheme.Danger : success ? TaiTheme.Success : TaiTheme.Warning;
        }

        private static void StyleButton(ModernButton button, string text,
            ModernButtonKind kind, TaiIcon icon, int width)
        {
            button.Text = text;
            button.Kind = kind;
            button.Icon = icon;
            button.Font = TaiTheme.SmallStrong;
            if (width > 0) button.Size = new Size(width, 30);
        }
    }
}
