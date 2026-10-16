using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ADDIN.Commands;

namespace ADDIN.UI
{
    internal sealed class BendStandardFilesForm : Form
    {
        private readonly TextBox directoryBox = new TextBox();
        private readonly DataGridView materials = Grid();
        private readonly DataGridView grooves = Grid();
        private readonly Label status = new Label();
        internal BendStandardProfile Selection { get; private set; }

        internal BendStandardFilesForm(BendStandardProfile profile)
        {
            Text = "Chọn bộ Excel chuẩn riêng";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(900, 620);
            MinimumSize = new Size(740, 480);
            Font = TaiTheme.Small;
            BackColor = TaiTheme.Page;
            ShowInTaskbar = false;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12),
                ColumnCount = 1, RowCount = 4 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            Controls.Add(root);
            var folder = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
            folder.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 134));
            folder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folder.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            folder.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            folder.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            folder.Controls.Add(new Label { Text = "Thư mục tìm Excel", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            directoryBox.Dock = DockStyle.Fill;
            folder.Controls.Add(directoryBox, 1, 0);
            var chooseFolder = Button("Chọn thư mục…", 126, ModernButtonKind.Standard);
            chooseFolder.Click += ChooseDirectory;
            folder.Controls.Add(chooseFolder, 2, 0);
            var help = new Label { Text = "Gán file theo nhóm bên dưới. Tên file có thể tùy ý; nhóm chưa chọn sẽ báo CHECK khi cần dùng.",
                Dock = DockStyle.Fill, ForeColor = TaiTheme.TextMuted };
            folder.Controls.Add(help, 0, 1);
            folder.SetColumnSpan(help, 3);
            root.Controls.Add(folder, 0, 0);
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var materialPage = new TabPage("Vật liệu") { Padding = new Padding(6) };
            var groovePage = new TabPage("Bảng bào") { Padding = new Padding(6) };
            tabs.TabPages.Add(materialPage);
            tabs.TabPages.Add(groovePage);
            root.Controls.Add(tabs, 0, 1);
            materials.Columns.Add(new DataGridViewTextBoxColumn { Name = "Group", HeaderText = "Nhóm", ReadOnly = true, Width = 80 });
            materials.Columns.Add(new DataGridViewTextBoxColumn { Name = "File", HeaderText = "Excel chuẩn", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            AddChooseColumns(materials);
            materialPage.Controls.Add(materials);
            for (int i = 0; i < BendStandardProfile.MaterialGroups.Length; i++)
            {
                string group = BendStandardProfile.MaterialGroups[i];
                string path = profile?.MaterialFiles.FirstOrDefault(f => f.Group == group)?.FilePath;
                materials.Rows.Add(group, path ?? "", "Chọn…", "Bỏ");
            }
            var grooveLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            grooveLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            grooveLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grooveLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            groovePage.Controls.Add(grooveLayout);
            grooveLayout.Controls.Add(new Label { Text = "Nhập bề dày còn lại sau bào (mm). Hệ số C được đọc từ Excel.\nVí dụ: tấm 6 mm, bào còn 2.5 mm, bảng có thể dùng C=2.3 mm.",
                Dock = DockStyle.Fill, ForeColor = TaiTheme.TextMuted }, 0, 0);
            grooves.Columns.Add(new DataGridViewTextBoxColumn { Name = "Remaining", HeaderText = "Bào còn (mm)", Width = 124 });
            grooves.Columns.Add(new DataGridViewTextBoxColumn { Name = "File", HeaderText = "Excel chuẩn", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            AddChooseColumns(grooves);
            grooveLayout.Controls.Add(grooves, 0, 1);
            var addGroove = Button("Thêm bảng bào", 160, ModernButtonKind.Standard);
            addGroove.Click += (sender, e) =>
            {
                string path = PickFile("Chọn Excel chuẩn bào rãnh");
                if (path == null) return;
                double? hint = KegakiChecker.ReadCanonicalGrooveRemainingThickness(path);
                int index = grooves.Rows.Add(hint.HasValue ? hint.Value.ToString("0.###", CultureInfo.InvariantCulture) : "", path, "Chọn…", "Xóa");
                grooves.CurrentCell = grooves.Rows[index].Cells[0];
                grooves.BeginEdit(true);
            };
            grooveLayout.Controls.Add(addGroove, 0, 2);
            if (profile != null)
                foreach (BendStandardGrooveFile file in profile.GrooveFiles)
                    grooves.Rows.Add(file.RemainingThicknessMm.ToString("0.###", CultureInfo.InvariantCulture), file.FilePath, "Chọn…", "Xóa");
            directoryBox.Text = profile?.SourceDirectory ?? "";
            materials.CellContentClick += MaterialClicked;
            grooves.CellContentClick += GrooveClicked;
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = Button("Hủy", 90, ModernButtonKind.Standard);
            cancel.DialogResult = DialogResult.Cancel;
            var accept = Button("Áp dụng", 110, ModernButtonKind.Primary);
            accept.Click += (sender, e) => ApplySelection();
            actions.Controls.Add(cancel);
            actions.Controls.Add(accept);
            root.Controls.Add(actions, 0, 2);
            status.Dock = DockStyle.Fill;
            status.ForeColor = TaiTheme.Danger;
            root.Controls.Add(status, 0, 3);
            CancelButton = cancel;
        }

        private void ChooseDirectory(object sender, EventArgs e)
        {
            using (var browser = new FolderBrowserDialog { Description = "Chọn thư mục chứa bộ Excel chuẩn riêng" })
            {
                if (Directory.Exists(directoryBox.Text)) browser.SelectedPath = directoryBox.Text;
                if (browser.ShowDialog(this) == DialogResult.OK) directoryBox.Text = browser.SelectedPath;
            }
        }

        private string PickFile(string title)
        {
            using (var dialog = new OpenFileDialog { Title = title, Filter = "Excel Bend Table (*.xlsx)|*.xlsx", CheckFileExists = true })
            {
                if (Directory.Exists(directoryBox.Text)) dialog.InitialDirectory = directoryBox.Text;
                if (dialog.ShowDialog(this) != DialogResult.OK) return null;
                if (string.IsNullOrWhiteSpace(directoryBox.Text)) directoryBox.Text = Path.GetDirectoryName(dialog.FileName);
                return dialog.FileName;
            }
        }

        private void MaterialClicked(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (e.ColumnIndex == 2)
            {
                string path = PickFile("Chọn Excel chuẩn " + materials.Rows[e.RowIndex].Cells[0].Value);
                if (path != null) materials.Rows[e.RowIndex].Cells[1].Value = path;
            }
            else if (e.ColumnIndex == 3) materials.Rows[e.RowIndex].Cells[1].Value = "";
        }

        private void GrooveClicked(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (e.ColumnIndex == 2)
            {
                string path = PickFile("Chọn Excel chuẩn bào rãnh");
                if (path != null) grooves.Rows[e.RowIndex].Cells[1].Value = path;
            }
            else if (e.ColumnIndex == 3) grooves.Rows.RemoveAt(e.RowIndex);
        }

        private void ApplySelection()
        {
            try
            {
                grooves.EndEdit();
                var profile = new BendStandardProfile { SourceDirectory = directoryBox.Text };
                foreach (DataGridViewRow row in materials.Rows)
                {
                    string path = Convert.ToString(row.Cells[1].Value);
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    profile.MaterialFiles.Add(new BendStandardMaterialFile { Group = Convert.ToString(row.Cells[0].Value), FilePath = path });
                }
                foreach (DataGridViewRow row in grooves.Rows)
                {
                    double remaining;
                    if (!double.TryParse(Convert.ToString(row.Cells[0].Value).Replace(',', '.'), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out remaining)) throw new InvalidDataException("Nhập bề dày còn lại cho từng bảng bào.");
                    profile.GrooveFiles.Add(new BendStandardGrooveFile { FilePath = Convert.ToString(row.Cells[1].Value), RemainingThicknessMm = remaining });
                }
                Selection = BendStandardProfile.Validate(profile, true);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex) { status.Text = "Chưa áp dụng: " + ex.Message; }
        }

        private static DataGridView Grid()
        {
            return new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AutoGenerateColumns = false, RowHeadersVisible = false, BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowTemplate = { Height = 34 }, MultiSelect = false };
        }
        private static void AddChooseColumns(DataGridView grid)
        {
            grid.Columns.Add(new DataGridViewButtonColumn { Name = "Choose", HeaderText = "", Width = 76 });
            grid.Columns.Add(new DataGridViewButtonColumn { Name = "Clear", HeaderText = "", Width = 56 });
        }
        private static ModernButton Button(string text, int width, ModernButtonKind kind)
        { return new ModernButton { Text = text, Width = width, Height = 32, Kind = kind }; }
    }
}
