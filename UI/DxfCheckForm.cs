using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ADDIN.Commands.DxfChecking;

namespace ADDIN.UI
{
    internal sealed class DxfCheckForm : Form
    {
        private static readonly Color Ink = Color.FromArgb(24, 42, 66), Muted = Color.FromArgb(105, 122, 143), Blue = Color.FromArgb(37, 99, 211), Green = Color.FromArgb(16, 130, 98), Red = Color.FromArgb(207, 65, 78), Amber = Color.FromArgb(166, 106, 21);
        private readonly TextBox folderBox = new TextBox { ReadOnly = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, BackColor = Color.FromArgb(248, 250, 252) };
        private readonly TextBox searchBox = new TextBox { BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, BackColor = Color.FromArgb(248, 250, 252) };
        private readonly CheckBox recursiveBox = new CheckBox { Text = "Quét cả thư mục con", AutoSize = true, Checked = true, Font = new Font("Segoe UI", 9F) };
        private readonly CheckBox needsRepairBox = new CheckBox { Text = "Chỉ file lỗi", AutoSize = true, Font = new Font("Segoe UI", 9F) };
        private readonly CheckBox yellowOnlyBox = new CheckBox { Text = "Có màu vàng (0)", AutoSize = true, Font = new Font("Segoe UI", 9F) };
        private readonly ModernButton chooseButton = Button("Chọn thư mục", true, 130, 32);
        private readonly ModernButton cancelButton = Button("Dừng", false, 64, 32);
        private readonly ModernButton excelButton = Button("📊 Excel", false, 94, 32);
        private readonly ModernButton outputButton = Button("📂 Output", false, 100, 32);
        private readonly ModernButton editButton = Button("✏️ Mở DXF để sửa", true, 150, 32);
        private readonly ModernButton cleanButton = Button("↻ Kiểm tra lại DXF đã sửa", true, 196, 32);
        private readonly ModernButton writeBackButton = Button("↩ Ghi đè DXF gốc", true, 166, 30);
        private readonly Label sourceTarget = Label("File gốc: chưa chọn DXF", 9F, false, Muted);
        private readonly ModernButton fitButton = Button("🔍 Toàn bộ", false, 88, 32);
        private readonly ModernButton duplicateButton = Button("Xem tên trùng", false, 120, 24);
        private readonly ModernButton conditionsButton = Button("Điều kiện (" + DxfCheckOptions.Default.Count + ")", false, 124, 24);
        private readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 2), Height = 3 };
        private readonly Label status = Label("Chọn thư mục DXF để bắt đầu kiểm tra.", 9F, false, Muted);
        private readonly Label fileCount = Label("(0 file)", 9F, false, Muted);
        private readonly Label selectedName = Label("Biên dạng DXF", 12F, true, Ink);
        private readonly Label selectedStatus = Label("", 10F, true, Muted);
        private readonly Label issueTitle = Label("Chọn file DXF để xem vị trí lỗi.", 10F, true, Muted);
        private readonly Font badgeFont = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        private readonly DataGridView files = Grid(), issues = Grid(), yellowGrid = Grid(), closureGrid = Grid();
        private readonly TabControl detailTabs = new TabControl { Dock = DockStyle.Fill, Multiline = true, Padding = new Point(12, 6), Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) };
        private readonly TabPage issuePage = new TabPage("Vị trí lỗi (0)") { BackColor = Color.White, Padding = new Padding(3) };
        private readonly TabPage yellowPage = new TabPage("Layer vàng") { BackColor = Color.White, Padding = new Padding(3) };
        private readonly TabPage closurePage = new TabPage("Độ kín") { BackColor = Color.White, Padding = new Padding(3) };
        private readonly Label yellowSummary = Label("Chọn DXF để xem thống kê layer vàng.", 9.5F, true, Muted);
        private readonly Label closureSummary = Label("Chọn DXF để xem độ kín biên dạng.", 9.5F, true, Muted);
        private readonly DxfPreview preview = new DxfPreview();
        private readonly DxfStatPill totalCard = new DxfStatPill("Tổng", Color.FromArgb(71, 85, 105), Color.FromArgb(241, 245, 249), Color.FromArgb(226, 232, 240));
        private readonly DxfStatPill okCard = new DxfStatPill("OK", Color.FromArgb(22, 101, 52), Color.FromArgb(220, 252, 231), Color.FromArgb(187, 247, 208));
        private readonly DxfStatPill ngCard = new DxfStatPill("NG", Color.FromArgb(153, 27, 27), Color.FromArgb(254, 226, 226), Color.FromArgb(254, 202, 202));
        private readonly DxfStatPill errorCard = new DxfStatPill("Lỗi", Color.FromArgb(146, 64, 14), Color.FromArgb(254, 243, 199), Color.FromArgb(253, 230, 138));
        private readonly SplitContainer outerSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 5, BackColor = Color.FromArgb(226, 232, 240) };
        private readonly SplitContainer innerSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 5, BackColor = Color.FromArgb(226, 232, 240) };
        private readonly List<DxfFileResult> completed = new List<DxfFileResult>();
        private readonly List<DxfDuplicateName> duplicateNames = new List<DxfDuplicateName>();
        private CancellationTokenSource cancellation;
        private bool running, closeAfterCancel, selecting, exportReview;
        private DxfBatchResult result;
        private DxfCheckOptions selectedOptions = DxfCheckOptions.Default;
        internal bool IsChecking { get { return running; } }
        internal event Action<DxfBatchResult> BatchChanged;
        private static readonly string PreferencePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TAI_TOOL", "check-dxf-folder.txt");

        public DxfCheckForm() : this(true) { }
        internal DxfCheckForm(bool autoChoose)
        {
            Text = "TAI TOOL — CHECK DXF FILE"; Size = new Size(1360, 820); MinimumSize = new Size(1020, 640);
            StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 9F); ForeColor = Ink;
            BackColor = Color.FromArgb(243, 246, 251); DoubleBuffered = true;
            cancelButton.Enabled = excelButton.Enabled = outputButton.Enabled = editButton.Enabled = cleanButton.Enabled = writeBackButton.Enabled = false;
            cancelButton.ForeColor = Red;
            editButton.NormalColor = Green; editButton.HoverColor = Color.FromArgb(12, 116, 87); editButton.PressColor = Color.FromArgb(9, 99, 75); editButton.BorderColor = Green;
            writeBackButton.NormalColor = Green; writeBackButton.HoverColor = Color.FromArgb(12, 116, 87); writeBackButton.PressColor = Color.FromArgb(9, 99, 75); writeBackButton.BorderColor = Green;
            status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft;
            duplicateButton.Visible = false; duplicateButton.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold); duplicateButton.ForeColor = Amber;
            duplicateButton.NormalColor = Color.FromArgb(255, 244, 214); duplicateButton.HoverColor = Color.FromArgb(255, 235, 186); duplicateButton.BorderColor = Color.FromArgb(236, 210, 151);
            folderBox.ForeColor = Ink; folderBox.Font = new Font("Segoe UI", 9.5F);
            searchBox.ForeColor = Ink; searchBox.Font = new Font("Segoe UI", 9F);

            // Tooltips
            ToolTip tips = new ToolTip { ShowAlways = true, InitialDelay = 350, ReshowDelay = 200, AutoPopDelay = 8000 };
            tips.SetToolTip(chooseButton, "Chọn thư mục chứa các file DXF để bắt đầu kiểm tra.");
            tips.SetToolTip(recursiveBox, "Kiểm tra tất cả file DXF trong các thư mục con.");
            tips.SetToolTip(needsRepairBox, "Chỉ hiển thị các file có lỗi (NG) hoặc lỗi đọc file.");
            tips.SetToolTip(yellowOnlyBox, "Chỉ hiển thị DXF có layer vàng chứa đối tượng hoặc đối tượng được gán màu vàng riêng. Không tính layer trống. Kết hợp với ô Chỉ file lỗi để xem các file NG có màu vàng.");
            tips.SetToolTip(cancelButton, "Dừng quá trình kiểm tra.");
            tips.SetToolTip(excelButton, "Mở file Excel tổng hợp kết quả kiểm tra.");
            tips.SetToolTip(outputButton, "Mở thư mục DXF NG và báo cáo Excel. Bản DXF đã kiểm tra OK và bỏ dấu NG nằm trong DXF_SACH.");
            tips.SetToolTip(fitButton, "Thu phóng hiển thị toàn bộ biên dạng file DXF.");
            tips.SetToolTip(editButton, "Mở file DXF này bằng phần mềm CAD để chỉnh sửa lỗi.");
            tips.SetToolTip(cleanButton, "Lưu DXF đã sửa trong CAD trước. Nút này kiểm tra lại file đã lưu và cập nhật kết quả. Khi OK, tạo bản DXF bỏ dấu NG để ghi đè file gốc hoặc copy. Nếu còn NG, mở bản đánh dấu mới để sửa tiếp.");
            tips.SetToolTip(writeBackButton, "Dùng bản DXF đã kiểm tra OK và bỏ dấu NG để ghi đè đúng file gốc của dòng đang chọn. Tool kiểm tra lại và sao lưu file gốc trong SAO_LUU_GOC. Đóng DXF gốc trong CAD trước khi bấm.");
            tips.SetToolTip(duplicateButton, "Xem danh sách các file DXF trùng tên ở các thư mục khác nhau.");
            tips.SetToolTip(conditionsButton, "Chọn các điều kiện kiểm tra có sẵn rồi quét lại thư mục đang chọn. Mỗi bộ kết quả giữ riêng các điều kiện đã dùng.");
            conditionsButton.MouseEnter += (s, e) => {
                DxfCheckOptions options = result?.Options ?? selectedOptions;
                tips.SetToolTip(conditionsButton, "Chế độ: " + DxfCheckConditionsForm.ModeName(options) + "\r\nĐiều kiện của bộ kết quả: " + options.SelectedNames + "\r\nBấm để chọn chế độ và quét lại thư mục.");
            };
            tips.SetToolTip(searchBox, "Nhập từ khóa để lọc nhanh file theo tên.");

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 6, 8, 6), ColumnCount = 1, RowCount = 4, BackColor = BackColor };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 4));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            Controls.Add(root);

            // Row 0: Unified Slim Ribbon Bar (Single sleek row)
            Panel ribbon = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = Padding.Empty, Padding = new Padding(8, 6, 8, 6) };
            TableLayoutPanel ribbonLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.White, Margin = Padding.Empty };
            ribbonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ribbonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            ribbonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            // Ribbon Left: Brand + Choose Button + Folder Path + Recursive Checkbox
            TableLayoutPanel leftTools = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = Color.White, Margin = Padding.Empty };
            leftTools.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            leftTools.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            leftTools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            leftTools.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            FlowLayoutPanel brandFlow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
            DxfLogo logo = new DxfLogo { Margin = new Padding(0, 3, 6, 0) };
            Label brandTitle = Label("CHECK DXF FILE", 11.5F, true, Ink); brandTitle.Margin = new Padding(0, 5, 12, 0); brandTitle.AutoSize = true;
            brandFlow.Controls.Add(logo); brandFlow.Controls.Add(brandTitle);
            leftTools.Controls.Add(brandFlow, 0, 0);

            chooseButton.Margin = new Padding(0, 0, 6, 0);
            leftTools.Controls.Add(chooseButton, 1, 0);

            Panel folderContainer = new Panel { Dock = DockStyle.Fill, Height = 32, BackColor = Color.FromArgb(248, 250, 252), Margin = new Padding(0, 0, 6, 0), Padding = new Padding(8, 7, 8, 4) };
            folderContainer.Controls.Add(folderBox);
            folderContainer.Paint += (s, e) => {
                using (Pen p = new Pen(Color.FromArgb(218, 226, 236)))
                using (GraphicsPath gp = UIHelper.GetRoundPath(new Rectangle(0, 0, folderContainer.Width - 1, folderContainer.Height - 1), 4))
                { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.DrawPath(p, gp); }
            };
            leftTools.Controls.Add(folderContainer, 2, 0);

            recursiveBox.ForeColor = Muted; recursiveBox.Margin = new Padding(0, 6, 8, 0);
            leftTools.Controls.Add(recursiveBox, 3, 0);
            ribbonLayout.Controls.Add(leftTools, 0, 0);

            // Ribbon Middle: Stat Pills
            FlowLayoutPanel pillsPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 6, 0), AutoSize = true };
            pillsPanel.Controls.Add(totalCard); pillsPanel.Controls.Add(okCard); pillsPanel.Controls.Add(ngCard); pillsPanel.Controls.Add(errorCard);
            ribbonLayout.Controls.Add(pillsPanel, 1, 0);

            // Ribbon Right: Action Buttons
            FlowLayoutPanel actionPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
            cancelButton.Margin = outputButton.Margin = excelButton.Margin = new Padding(4, 0, 0, 0);
            actionPanel.Controls.Add(cancelButton); actionPanel.Controls.Add(outputButton); actionPanel.Controls.Add(excelButton);
            ribbonLayout.Controls.Add(actionPanel, 2, 0);

            ribbon.Controls.Add(ribbonLayout);
            ribbon.Paint += (s, e) => {
                using (Pen p = new Pen(Color.FromArgb(224, 231, 241)))
                    e.Graphics.DrawRectangle(p, 0, 0, ribbon.Width - 1, ribbon.Height - 1);
            };
            root.Controls.Add(ribbon, 0, 0);

            // Row 1: Slim Progress Bar
            root.Controls.Add(progress, 0, 1);

            // Row 2: 3-COLUMN WORKBENCH (Outer Split + Inner Split)
            // Left Pane: File list (Column 1)
            Panel filePane = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            TableLayoutPanel fileLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.White };
            fileLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
            fileLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            TableLayoutPanel fileHead = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.White, Margin = new Padding(6, 4, 6, 2) };
            fileHead.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            fileHead.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            fileHead.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

            Panel searchContainer = new Panel { Dock = DockStyle.Fill, Height = 28, BackColor = Color.FromArgb(248, 250, 252), Padding = new Padding(6, 5, 6, 4), Margin = Padding.Empty };
            Label searchIcon = new Label { Text = "🔍", AutoSize = true, Dock = DockStyle.Left, ForeColor = Muted, Font = new Font("Segoe UI", 8F) };
            searchContainer.Controls.Add(searchBox); searchContainer.Controls.Add(searchIcon);
            searchContainer.Paint += (s, e) => {
                using (Pen p = new Pen(Color.FromArgb(220, 228, 238)))
                using (GraphicsPath gp = UIHelper.GetRoundPath(new Rectangle(0, 0, searchContainer.Width - 1, searchContainer.Height - 1), 4))
                { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.DrawPath(p, gp); }
            };
            fileHead.Controls.Add(searchContainer, 0, 0);

            TableLayoutPanel fileSubHead = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1, BackColor = Color.White, Margin = new Padding(0, 2, 0, 0) };
            fileSubHead.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            FlowLayoutPanel fileTitleFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
            Label listTitle = Label("Danh sách file", 9.5F, true, Ink); listTitle.AutoSize = true; listTitle.Margin = new Padding(0, 3, 4, 0);
            fileCount.AutoSize = true; fileCount.Margin = new Padding(0, 4, 0, 0);
            fileTitleFlow.Controls.Add(listTitle); fileTitleFlow.Controls.Add(fileCount);
            fileSubHead.Controls.Add(fileTitleFlow, 0, 0);

            fileHead.Controls.Add(fileSubHead, 0, 1);
            FlowLayoutPanel fileFilters = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
            needsRepairBox.ForeColor = Muted; needsRepairBox.Margin = new Padding(0, 3, 12, 0);
            yellowOnlyBox.ForeColor = Amber; yellowOnlyBox.Margin = new Padding(0, 3, 0, 0);
            fileFilters.Controls.Add(needsRepairBox); fileFilters.Controls.Add(yellowOnlyBox);
            fileHead.Controls.Add(fileFilters, 0, 2);

            fileLayout.Controls.Add(fileHead, 0, 0);
            fileLayout.Controls.Add(files, 0, 1);
            filePane.Controls.Add(fileLayout);
            filePane.Paint += (s, e) => {
                using (Pen p = new Pen(Color.FromArgb(224, 231, 241)))
                    e.Graphics.DrawRectangle(p, 0, 0, filePane.Width - 1, filePane.Height - 1);
            };
            outerSplit.Panel1.Controls.Add(filePane);

            // Center Pane: CAD Preview (Column 2) - FULL HEIGHT!
            Panel previewPane = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            TableLayoutPanel previewLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Color.White };
            previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));

            TableLayoutPanel previewHead = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.White, Margin = new Padding(8, 4, 8, 2) };
            previewHead.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            previewHead.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); previewHead.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            TableLayoutPanel previewTitleFlow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            previewTitleFlow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); previewTitleFlow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            selectedName.AutoSize = false; selectedName.Dock = DockStyle.Fill; selectedName.Margin = new Padding(0, 3, 8, 0);
            selectedStatus.AutoSize = true; selectedStatus.Margin = new Padding(0, 7, 0, 0); selectedStatus.Font = badgeFont;
            previewTitleFlow.Controls.Add(selectedName, 0, 0); previewTitleFlow.Controls.Add(selectedStatus, 1, 0);
            previewHead.Controls.Add(previewTitleFlow, 0, 0);

            FlowLayoutPanel previewButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = Padding.Empty };
            cleanButton.Margin = editButton.Margin = fitButton.Margin = new Padding(4, 1, 0, 1);
            previewButtons.Controls.Add(fitButton); previewButtons.Controls.Add(editButton); previewButtons.Controls.Add(cleanButton);
            previewHead.Controls.Add(previewButtons, 0, 1);
            previewHead.SizeChanged += (s, e) => {
                float height = 28 + 6 + (previewButtons.Controls.Cast<Control>().Sum(c => c.Width + c.Margin.Horizontal) <= previewHead.ClientSize.Width ? 36 : 70);
                if (Math.Abs(previewLayout.RowStyles[0].Height - height) > .1) previewLayout.RowStyles[0].Height = height;
            };

            TableLayoutPanel sourceActions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.FromArgb(248, 250, 252), Margin = new Padding(8, 0, 8, 2) };
            sourceActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 174));
            sourceActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            writeBackButton.Margin = new Padding(0, 3, 8, 3);
            sourceTarget.Dock = DockStyle.Fill; sourceTarget.TextAlign = ContentAlignment.MiddleLeft; sourceTarget.Margin = new Padding(0, 0, 3, 0);
            sourceActions.Controls.Add(writeBackButton, 0, 0); sourceActions.Controls.Add(sourceTarget, 1, 0);
            sourceTarget.MouseEnter += (s, e) => {
                DxfFileResult file = SelectedFile();
                tips.SetToolTip(sourceTarget, file == null ? "Chọn DXF để xem đường dẫn file gốc." : file.SourcePath + (file.SourceUpdatedAt.HasValue ? "\r\nĐã ghi đè lúc " + file.SourceUpdatedAt.Value.ToString("HH:mm:ss") + "\r\nBản sao lưu: " + file.SourceBackupPath : ""));
            };

            Label previewHint = Label("Lăn chuột để Zoom  ·  Giữ chuột trái để Kéo Pan  ·  Chọn lỗi hoặc dòng Layer vàng để phóng tới vị trí", 8.5F, false, Muted);
            previewHint.Dock = DockStyle.Fill; previewHint.TextAlign = ContentAlignment.MiddleLeft; previewHint.Padding = new Padding(8, 0, 0, 0);

            previewLayout.Controls.Add(previewHead, 0, 0);
            previewLayout.Controls.Add(sourceActions, 0, 1);
            previewLayout.Controls.Add(preview, 0, 2);
            previewLayout.Controls.Add(previewHint, 0, 3);
            previewPane.Controls.Add(previewLayout);
            previewPane.Paint += (s, e) => {
                using (Pen p = new Pen(Color.FromArgb(224, 231, 241)))
                    e.Graphics.DrawRectangle(p, 0, 0, previewPane.Width - 1, previewPane.Height - 1);
            };
            innerSplit.Panel1.Controls.Add(previewPane);

            // Right Pane: Inspector & Issues (Column 3) - FULL HEIGHT!
            Panel inspectorPane = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            issueTitle.Dock = DockStyle.Top; issueTitle.Height = 28; issueTitle.TextAlign = ContentAlignment.MiddleLeft;
            issueTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold); issueTitle.Padding = new Padding(6, 0, 0, 0);
            issuePage.Controls.Add(issues); issuePage.Controls.Add(issueTitle);

            yellowSummary.Dock = DockStyle.Top; yellowSummary.Height = 28; yellowSummary.TextAlign = ContentAlignment.MiddleLeft;
            yellowSummary.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold); yellowSummary.Padding = new Padding(6, 0, 0, 0);
            yellowPage.Controls.Add(yellowGrid); yellowPage.Controls.Add(yellowSummary);

            closureSummary.Dock = DockStyle.Top; closureSummary.Height = 28; closureSummary.TextAlign = ContentAlignment.MiddleLeft;
            closureSummary.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold); closureSummary.Padding = new Padding(6, 0, 0, 0);
            closurePage.Controls.Add(closureGrid); closurePage.Controls.Add(closureSummary);

            detailTabs.TabPages.Add(issuePage);
            detailTabs.TabPages.Add(yellowPage);
            detailTabs.TabPages.Add(closurePage);
            inspectorPane.Controls.Add(detailTabs);
            inspectorPane.Paint += (s, e) => {
                using (Pen p = new Pen(Color.FromArgb(224, 231, 241)))
                    e.Graphics.DrawRectangle(p, 0, 0, inspectorPane.Width - 1, inspectorPane.Height - 1);
            };
            innerSplit.Panel2.Controls.Add(inspectorPane);

            outerSplit.Panel2.Controls.Add(innerSplit);
            root.Controls.Add(outerSplit, 0, 2);

            // Row 3: Bottom Status Bar
            TableLayoutPanel statusBar = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 4, RowCount = 1 };
            statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Label brandTag = Label("TAI TOOL WORKBENCH", 8.5F, true, Color.FromArgb(160, 175, 195));
            brandTag.Margin = new Padding(8, 4, 4, 0); brandTag.AutoSize = true;

            duplicateButton.Margin = new Padding(6, 1, 0, 1);
            statusBar.Controls.Add(status, 0, 0);
            statusBar.Controls.Add(duplicateButton, 1, 0);
            conditionsButton.Margin = new Padding(6, 1, 0, 1); conditionsButton.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            statusBar.Controls.Add(conditionsButton, 2, 0); statusBar.Controls.Add(brandTag, 3, 0);
            root.Controls.Add(statusBar, 0, 3);

            // Grid column setups
            files.Columns.Add("File", "FILE DXF"); files.Columns.Add("Result", "KẾT QUẢ"); files.Columns.Add("Count", "LỖI");
            files.Columns.Add("Yellow", "VÀNG");
            files.Columns[0].FillWeight = 100; files.Columns[0].MinimumWidth = 80;
            foreach (DataGridViewColumn column in files.Columns.Cast<DataGridViewColumn>().Skip(1)) {
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                column.HeaderCell.Style.Padding = Padding.Empty;
            }
            files.Columns[1].Width = 76; files.Columns[2].Width = 42;
            files.Columns[3].Width = Math.Max(120, TextRenderer.MeasureText("Đối tượng vàng", badgeFont).Width + files.DefaultCellStyle.Padding.Horizontal + 6);
            files.Columns[1].HeaderCell.ToolTipText = "OK/NG theo những điều kiện đã chọn trong từng file DXF. ERROR: không đọc được file.";
            files.Columns[3].HeaderCell.ToolTipText = "1, 2…: số layer vàng có đối tượng, không tính layer trống. Đối tượng vàng: đối tượng được gán màu vàng riêng trên layer màu khác. Không có: không có nội dung màu vàng, kể cả khi file có khai báo layer vàng trống. ?: lỗi đọc, chưa thống kê. Bấm ô để xem chi tiết và vị trí.";
            issues.Columns.Add("Id", "DẤU"); issues.Columns.Add("Code", "LOẠI LỖI"); issues.Columns.Add("Text", "MÔ TẢ CHI TIẾT");
            issues.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.None; issues.Columns[0].Width = 56;
            issues.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.None; issues.Columns[1].Width = 118;
            issues.Columns[2].FillWeight = 100;
            issues.Columns[0].DefaultCellStyle.ForeColor = Red; issues.Columns[0].DefaultCellStyle.Font = badgeFont;
            issues.Columns[0].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            yellowGrid.Columns.Add("Layer", "LAYER"); yellowGrid.Columns.Add("Shape", "HÌNH HỌC"); yellowGrid.Columns.Add("Quantity", "SỐ LƯỢNG"); yellowGrid.Columns.Add("Source", "NGUỒN MÀU");
            yellowGrid.Columns[0].FillWeight = 42; yellowGrid.Columns[1].FillWeight = 28;
            yellowGrid.Columns[2].AutoSizeMode = yellowGrid.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            yellowGrid.Columns[2].Width = 80; yellowGrid.Columns[3].Width = 95;
            yellowGrid.Columns[2].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            yellowGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(255, 248, 221);
            yellowGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(126, 95, 15);
            closureGrid.Columns.Add("Id", "BIÊN"); closureGrid.Columns.Add("Role", "VỊ TRÍ"); closureGrid.Columns.Add("Parent", "TRONG BIÊN"); closureGrid.Columns.Add("Closed", "ĐỘ KÍN"); closureGrid.Columns.Add("Gap", "HỞ (mm)");
            closureGrid.Columns[0].AutoSizeMode = closureGrid.Columns[2].AutoSizeMode = closureGrid.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            closureGrid.Columns[0].Width = 48; closureGrid.Columns[2].Width = 80; closureGrid.Columns[3].Width = 112;
            closureGrid.Columns[1].FillWeight = 45; closureGrid.Columns[4].FillWeight = 55;
            closureGrid.Columns[4].DefaultCellStyle.Format = "0.000";
            closureGrid.CellFormatting += (s, e) => {
                if (e.RowIndex < 0 || e.ColumnIndex != 3) return;
                bool closed = Convert.ToString(e.Value) == "Kín";
                e.CellStyle.ForeColor = closed ? Green : Red; e.CellStyle.Font = badgeFont;
            };
            files.CellFormatting += (s, e) => {
                if (e.RowIndex >= 0 && e.ColumnIndex == 0 && (files.Rows[e.RowIndex].Tag as DxfFileResult)?.DuplicateNameCount > 1) {
                    e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = Amber;
                    e.CellStyle.BackColor = Color.FromArgb(255, 248, 229); return;
                }
                if (e.RowIndex < 0 || e.ColumnIndex != 1) return;
                string value = Convert.ToString(e.Value); e.CellStyle.Font = badgeFont;
                e.CellStyle.ForeColor = value == "OK" ? Green : value == "NG" ? Red : Color.FromArgb(165, 106, 21);
                e.CellStyle.BackColor = value == "OK" ? Color.FromArgb(234, 248, 240) : value == "NG" ? Color.FromArgb(255, 239, 241) : Color.FromArgb(255, 247, 229);
            };
            chooseButton.Click += async (s, e) => await ChooseAndRun();
            conditionsButton.Click += async (s, e) => { if (string.IsNullOrWhiteSpace(folderBox.Text)) await ChooseAndRun(); else await ChooseConditionsAndRun(folderBox.Text); };
            cancelButton.Click += (s, e) => { cancellation?.Cancel(); cancelButton.Enabled = false; status.Text = "Đang dừng…"; };
            excelButton.Click += (s, e) => Open(result?.ExcelPath); outputButton.Click += (s, e) => Open(result?.OutputFolder);
            editButton.Click += (s, e) => OpenSelectedForEditing(); cleanButton.Click += async (s, e) => await CreateCleanCopy();
            writeBackButton.Click += async (s, e) => await WriteBackSelectedFile();
            fitButton.Click += (s, e) => {
                selecting = true;
                try { issues.ClearSelection(); yellowGrid.ClearSelection(); closureGrid.ClearSelection(); }
                finally { selecting = false; }
                ResetYellowSummary(SelectedFile()); preview.Set(SelectedFile(), null);
            };
            duplicateButton.Click += (s, e) => { using (Form dialog = CreateDuplicateNamesDialog()) dialog.ShowDialog(this); };
            needsRepairBox.CheckedChanged += (s, e) => { if (!selecting) PopulateFiles(); };
            yellowOnlyBox.CheckedChanged += (s, e) => { if (!selecting) PopulateFiles(); };
            searchBox.TextChanged += (s, e) => { if (!selecting) PopulateFiles(); };
            files.SelectionChanged += (s, e) => { if (!selecting) SelectFile(); };
            files.CellClick += (s, e) => {
                if (selecting || e.RowIndex < 0) return;
                if (e.ColumnIndex == files.Columns["Yellow"].Index) ShowFileYellow(e.RowIndex);
            };
            issues.SelectionChanged += (s, e) => { if (!selecting) SelectIssue(); };
            yellowGrid.SelectionChanged += (s, e) => { if (!selecting) SelectYellowShape(); };
            yellowGrid.CellClick += (s, e) => { if (!selecting && e.RowIndex >= 0) SelectYellowShape(); };
            closureGrid.SelectionChanged += (s, e) => {
                if (selecting) return;
                DxfContourResult contour = closureGrid.SelectedRows.Count > 0 ? closureGrid.SelectedRows[0].Tag as DxfContourResult : null;
                DxfFileResult file = SelectedFile();
                preview.Set(file, contour == null ? null : file?.Issues.FirstOrDefault(i => i.ContourId == contour.Id));
            };
            files.CellDoubleClick += (s, e) => {
                if (e.RowIndex < 0) return;
                if (e.ColumnIndex == files.Columns["Yellow"].Index) { ShowFileYellow(e.RowIndex); return; }
                OpenSelectedForEditing();
            };
            Shown += async (s, e) => {
                LayoutWorkspace();
                if (autoChoose) await ChooseAndRun();
            };
            FormClosing += (s, e) => { if (running) { closeAfterCancel = true; cancellation?.Cancel(); e.Cancel = true; } };
        }

        private void LayoutWorkspace()
        {
            try {
                int filePaneMinimum = Math.Max(322, files.Columns.Cast<DataGridViewColumn>().Skip(1).Sum(c => c.Width) + files.Columns[0].MinimumWidth + 32);
                if (outerSplit.Width > 500) outerSplit.SplitterDistance = Math.Max(filePaneMinimum, Math.Min(390, outerSplit.Width * 24 / 100));
                if (innerSplit.Width > 500) innerSplit.SplitterDistance = Math.Max(320, innerSplit.Width - 390);
            }
            catch { }
        }

        internal void PositionOnScreen(Rectangle area)
        {
            StartPosition = FormStartPosition.Manual;
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2));
        }

        internal bool ShowExportedBatch(DxfBatchResult batch, string selectedSource = null)
        {
            if (running || IsDisposed || Disposing || batch == null) return false;
            exportReview = true; selectedOptions = batch.Options;
            editButton.Text = "Mở DXF để sửa"; cleanButton.Text = "Kiểm tra lại"; writeBackButton.Text = "Ghi đè DXF gốc";
            Text = "TAI TOOL — CHECK DXF · DRAWING BOM";
            chooseButton.Enabled = conditionsButton.Enabled = recursiveBox.Enabled = false;
            if (!ReferenceEquals(result, batch)) {
                selecting = true;
                try { needsRepairBox.Checked = yellowOnlyBox.Checked = false; searchBox.Clear(); }
                finally { selecting = false; }
                DisplayBatch(batch);
                progress.Maximum = Math.Max(1, batch.Files.Count); progress.Value = batch.Files.Count;
                status.Text = "DXF vừa xuất  ·  " + batch.Files.Count + " file  ·  " + batch.NgCount + " NG  ·  " + batch.ErrorCount + " lỗi đọc  ·  Check biên CUT";
                detailTabs.SelectedTab = issuePage;
            }
            if (!string.IsNullOrWhiteSpace(selectedSource)) {
                var wanted = batch.Files.FirstOrDefault(f => string.Equals(f.SourcePath, selectedSource, StringComparison.OrdinalIgnoreCase));
                if (wanted != null) {
                    selecting = true;
                    try { needsRepairBox.Checked = yellowOnlyBox.Checked = false; searchBox.Clear(); }
                    finally { selecting = false; }
                    PopulateFiles();
                    var row = files.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => ReferenceEquals(r.Tag, wanted));
                    if (row != null) { files.ClearSelection(); row.Selected = true; files.CurrentCell = row.Cells[0]; }
                    SelectFile(); detailTabs.SelectedTab = issuePage;
                }
            }
            return true;
        }

        private async Task ChooseAndRun()
        {
            if (running) return;
            string folder;
            using (FolderBrowserDialog dialog = new FolderBrowserDialog()) {
                dialog.Description = "Chọn thư mục chứa các file DXF"; dialog.ShowNewFolderButton = false;
                try { if (File.Exists(PreferencePath)) dialog.SelectedPath = File.ReadAllText(PreferencePath); } catch (IOException) { }
                if (!string.IsNullOrEmpty(folderBox.Text)) dialog.SelectedPath = folderBox.Text;
                if (dialog.ShowDialog(this) != DialogResult.OK) return; folder = dialog.SelectedPath;
            }
            await ChooseConditionsAndRun(folder);
        }
        private async Task ChooseConditionsAndRun(string folder)
        {
            if (running) return;
            using (DxfCheckConditionsForm dialog = new DxfCheckConditionsForm(folder, selectedOptions, recursiveBox.Checked)) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                selectedOptions = dialog.SelectedOptions; recursiveBox.Checked = dialog.Recursive;
            }
            try { Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)); File.WriteAllText(PreferencePath, folder); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            await RunFolder(folder, recursiveBox.Checked);
        }
        private Task RunFolder(string folder, bool recursive)
        {
            if (running || IsDisposed) return Task.FromResult(false);
            folderBox.Text = folder;
            DxfCheckOptions scanOptions = selectedOptions;
            CancellationTokenSource scanCancellation = new CancellationTokenSource();
            cancellation = scanCancellation; running = true;
            CancellationToken token = scanCancellation.Token;
            chooseButton.Enabled = conditionsButton.Enabled = recursiveBox.Enabled = excelButton.Enabled = outputButton.Enabled = editButton.Enabled = cleanButton.Enabled = writeBackButton.Enabled = false; cancelButton.Enabled = true;
            result = null; completed.Clear(); selecting = true; files.Rows.Clear(); issues.Rows.Clear(); selecting = false;
            SetDuplicateNames(new List<DxfDuplicateName>());
            preview.Set(null, null); RefreshMetrics(); progress.Value = 0; status.ForeColor = Muted; status.Text = "Đang kiểm tra DXF…";
            Exception progressFailure = null;
            // SolidWorks is a native host: its callbacks need not have a WinForms
            // SynchronizationContext. Marshal to this form instead of relying on await.
            IProgress<DxfProgress> reporter = new DxfUiProgress(p => {
                PostProgress(() => {
                    if (!running || cancellation != scanCancellation) return;
                    try {
                        progress.Maximum = Math.Max(1, p.Total); progress.Value = Math.Min(p.Done, progress.Maximum);
                        if (p.DuplicateNames != null) SetDuplicateNames(p.DuplicateNames);
                        if (p.File != null) {
                            completed.Add(p.File);
                            if (MatchesFileFilters(p.File)) AddFileRow(p.File);
                        }
                        RefreshMetrics(); status.ForeColor = duplicateNames.Count == 0 ? Muted : Amber;
                        status.Text = (p.Message ?? ("Đang kiểm tra " + p.Done + "/" + p.Total + (p.File == null ? "" : "  ·  " + p.File.RelativeName))) + DuplicateWarningSuffix();
                    }
                    catch (Exception ex) { progressFailure = ex; scanCancellation.Cancel(); Trace.WriteLine("[CHECK DXF FILE] Progress failed: " + ex); }
                });
            });
            return Task.Run(() => {
                DxfBatchResult batch = null; Exception failure = null;
                try {
                    try { batch = DxfCheckEngine.Run(folder, recursive, null, reporter, token, scanOptions); }
                    catch (Exception ex) { failure = ex; Trace.WriteLine("[CHECK DXF FILE] Failed: " + ex); }
                    if (!IsDisposed && !Disposing && IsHandleCreated)
                        Invoke((MethodInvoker)(() => FinishScan(batch, failure ?? progressFailure, scanCancellation)));
                }
                catch (Exception ex) { Trace.WriteLine("[CHECK DXF FILE] Completion failed: " + ex); }
                finally { scanCancellation.Dispose(); }
            });
        }
        private void PostProgress(Action action)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke((MethodInvoker)(() => { if (!IsDisposed && !Disposing) action(); })); }
            catch (InvalidOperationException) { /* The owning window closed while posting. */ }
        }
        private void FinishScan(DxfBatchResult batch, Exception failure, CancellationTokenSource scanCancellation)
        {
            try {
                if (IsDisposed || Disposing) return;
                if (failure == null) DisplayBatch(batch);
                else { status.Text = "Không hoàn tất kiểm tra."; MessageBox.Show(this, failure.Message, "CHECK DXF FILE", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
            catch (Exception ex) { Trace.WriteLine("[CHECK DXF FILE] UI failed: " + ex); if (!IsDisposed && !Disposing) { status.Text = "Không hoàn tất kiểm tra."; MessageBox.Show(this, ex.Message, "CHECK DXF FILE", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
            finally {
                running = false; if (cancellation == scanCancellation) cancellation = null;
                if (!IsDisposed && !Disposing) { chooseButton.Enabled = conditionsButton.Enabled = recursiveBox.Enabled = true; cancelButton.Enabled = false; SelectFile(); if (closeAfterCancel) Close(); }
            }
        }
        private sealed class DxfUiProgress : IProgress<DxfProgress>
        {
            private readonly Action<DxfProgress> report;
            internal DxfUiProgress(Action<DxfProgress> report) { this.report = report; }
            public void Report(DxfProgress value) { report(value); }
        }
        private void DisplayBatch(DxfBatchResult batch)
        {
            result = batch; completed.Clear(); completed.AddRange(batch.Files); folderBox.Text = batch.SourceFolder;
            conditionsButton.Text = "Điều kiện (" + batch.Options.Count + ")";
            yellowOnlyBox.Enabled = batch.Options.Has(DxfCheckCondition.YellowLayers);
            selecting = true;
            if (!yellowOnlyBox.Enabled) yellowOnlyBox.Checked = false;
            selecting = false;
            SetDuplicateNames(batch.DuplicateNames);
            status.ForeColor = batch.InputMessage == null && duplicateNames.Count == 0 ? Muted : Amber;
            status.Text = batch.InputMessage ?? ((batch.Cancelled ? "Đã dừng" : "Hoàn tất") + "  ·  " + batch.Files.Count + " file  ·  " + batch.NgCount + " NG  ·  " + batch.ErrorCount + " lỗi đọc"
                + "  ·  " + batch.Seconds.ToString("0.00") + " giây" + DuplicateWarningSuffix()
                + "  ·  " + DxfCheckConditionsForm.ModeName(batch.Options));
            excelButton.Enabled = File.Exists(batch.ExcelPath); outputButton.Enabled = Directory.Exists(batch.OutputFolder);
            RefreshMetrics(); PopulateFiles();
        }
        private void RefreshMetrics()
        {
            totalCard.Count = completed.Count; okCard.Count = completed.Count(f => f.Status == "OK"); ngCard.Count = completed.Count(f => f.Status == "NG"); errorCard.Count = completed.Count(f => f.Status == "ERROR");
            bool filtered = needsRepairBox.Checked || yellowOnlyBox.Checked || !string.IsNullOrWhiteSpace(searchBox.Text);
            fileCount.Text = filtered ? "(" + files.Rows.Count + " / " + completed.Count + " file)" : "(" + completed.Count + " file)";
            yellowOnlyBox.Text = "Có màu vàng (" + completed.Count(HasYellow) + ")";
        }
        private void SetDuplicateNames(List<DxfDuplicateName> names)
        {
            duplicateNames.Clear(); duplicateNames.AddRange(names);
            duplicateButton.Visible = duplicateNames.Count > 0;
            duplicateButton.Text = "Tên trùng (" + duplicateNames.Count + ")";
        }
        private string DuplicateWarningSuffix()
        { return duplicateNames.Count == 0 ? "" : "  ·  Cảnh báo: " + duplicateNames.Count + " tên trùng (" + duplicateNames.Sum(d => d.RelativeNames.Count) + " file)"; }
        private Form CreateDuplicateNamesDialog()
        {
            Form dialog = new Form { Text = "CHECK DXF FILE — Cảnh báo tên file trùng", Size = new Size(980, 530), MinimumSize = new Size(680, 380),
                StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, BackColor = Color.White, Font = new Font("Segoe UI", 9F), Padding = new Padding(16) };
            Label heading = Label(duplicateNames.Count + " tên DXF trùng ở " + duplicateNames.Sum(d => d.RelativeNames.Count) + " file thuộc các thư mục khác nhau.\r\nOutput giữ riêng từng thư mục; xem đường dẫn dưới đây để chọn đúng file cần sửa.", 9F, false, Amber);
            heading.Dock = DockStyle.Top; heading.Height = 50;
            DataGridView table = Grid(); table.Name = "duplicateNamesGrid";
            table.Columns.Add("Name", "TÊN FILE"); table.Columns.Add("Count", "SỐ FILE CÙNG TÊN"); table.Columns.Add("Folder", "THƯ MỤC CON"); table.Columns.Add("Path", "ĐƯỜNG DẪN TRONG BỘ DXF");
            table.Columns[0].FillWeight = 24; table.Columns[1].FillWeight = 18; table.Columns[2].FillWeight = 27; table.Columns[3].FillWeight = 45;
            foreach (DxfDuplicateName duplicate in duplicateNames) foreach (string name in duplicate.RelativeNames) {
                int row = table.Rows.Add(Path.GetFileName(name), duplicate.RelativeNames.Count, string.IsNullOrEmpty(Path.GetDirectoryName(name)) ? "Thư mục đã chọn" : Path.GetDirectoryName(name), name);
                foreach (DataGridViewCell cell in table.Rows[row].Cells) cell.ToolTipText = Path.Combine(folderBox.Text, name);
            }
            table.ClearSelection();
            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 45 };
            ModernButton close = Button("Đóng", true, 88); close.Anchor = AnchorStyles.Top | AnchorStyles.Right; close.DialogResult = DialogResult.Cancel;
            footer.Controls.Add(close); footer.Resize += (s, e) => close.Location = new Point(Math.Max(0, footer.Width - close.Width), 8);
            dialog.Controls.Add(table); dialog.Controls.Add(heading); dialog.Controls.Add(footer); dialog.CancelButton = close;
            return dialog;
        }
        private void AddFileRow(DxfFileResult file)
        {
            string yellow = !file.Options.Has(DxfCheckCondition.YellowLayers) ? "TẮT" : file.Error != null ? "?" : !HasYellow(file) ? "Không có" : file.YellowLayerWithEntitiesCount > 0 ? file.YellowLayerWithEntitiesCount.ToString(CultureInfo.InvariantCulture) : "Đối tượng vàng";
            int row = files.Rows.Add(file.RelativeName, file.Status, file.Issues.Count, yellow); files.Rows[row].Tag = file;
            files.Rows[row].Cells[0].ToolTipText = (file.DuplicateNameCount > 1 ? "Cảnh báo: " + file.DuplicateNameCount + " file cùng tên ở các thư mục khác nhau.\r\n" : "") + file.SourcePath;
            DataGridViewCell yellowCell = files.Rows[row].Cells["Yellow"];
            yellowCell.ToolTipText = YellowFileTooltip(file);
            if (HasYellow(file)) {
                yellowCell.Style.ForeColor = yellowCell.Style.SelectionForeColor = Amber;
                yellowCell.Style.BackColor = yellowCell.Style.SelectionBackColor = Color.FromArgb(255, 248, 221);
                yellowCell.Style.Font = badgeFont;
            }
            if (file.RecheckedAt.HasValue) files.Rows[row].Cells[0].ToolTipText += "\r\nKiểm tra lại lúc " + file.RecheckedAt.Value.ToString("HH:mm:ss") + " từ bản đã lưu: " + file.CheckedPath;
            if (file.SourceUpdatedAt.HasValue) files.Rows[row].Cells[0].ToolTipText += "\r\nĐã ghi đè DXF gốc lúc " + file.SourceUpdatedAt.Value.ToString("HH:mm:ss") + "\r\nBản sao lưu: " + file.SourceBackupPath;
        }
        private static bool HasYellow(DxfFileResult file)
        { return file != null && file.HasYellowContent; }
        private static string YellowFileTooltip(DxfFileResult file)
        {
            if (!file.Options.Has(DxfCheckCondition.YellowLayers)) return "Không chọn điều kiện thống kê layer / đối tượng màu vàng trong lượt kiểm tra này.";
            if (file.Error != null) return "Chưa thống kê màu vàng: DXF có lỗi đọc.\r\n" + file.Error;
            List<string> lines = new List<string> { HasYellow(file)
                ? file.YellowLayerWithEntitiesCount + " layer vàng có đối tượng · " + file.YellowContourCount + " biên dạng kín"
                : "Không có layer vàng chứa đối tượng hoặc đối tượng vàng riêng." };
            if (HasYellow(file) && file.YellowLayerWithEntitiesCount == 0) lines.Insert(0, "Có đối tượng được gán màu vàng riêng trên layer màu khác.");
            if (file.YellowShapes.Any(s => s.Shape == "Empty")) lines.Add("Layer vàng trống không tính vào cột VÀNG hoặc bộ lọc Có màu vàng.");
            foreach (var group in file.YellowShapes.GroupBy(s => new { s.Layer, s.YellowLayer, s.Space }).Take(12))
                lines.Add(group.Key.Layer + " · " + (group.Key.YellowLayer ? "Layer vàng" : "Màu vàng riêng")
                    + " · " + group.Key.Space + ": " + string.Join("; ", group.Select(s => s.ShapeName + " × " + s.Count)));
            lines.Add("Bấm ô VÀNG để xem đầy đủ và chọn biên dạng cần kiểm tra.");
            return string.Join("\r\n", lines);
        }
        private bool MatchesFileFilters(DxfFileResult file)
        {
            string search = (searchBox.Text ?? "").Trim();
            return (!needsRepairBox.Checked || file.Status != "OK") && (!yellowOnlyBox.Checked || HasYellow(file))
                && (search.Length == 0 || file.RelativeName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        private void ShowFileYellow(int row)
        {
            if (row < 0 || row >= files.Rows.Count) return;
            selecting = true;
            try { files.ClearSelection(); files.Rows[row].Selected = true; files.CurrentCell = files.Rows[row].Cells["Yellow"]; }
            finally { selecting = false; }
            SelectFile(); detailTabs.SelectedTab = yellowPage;
        }
        private void PopulateFiles()
        {
            DxfFileResult before = SelectedFile(); selecting = true; files.Rows.Clear();
            foreach (DxfFileResult file in completed) {
                if (!MatchesFileFilters(file)) continue;
                AddFileRow(file);
            }
            files.ClearSelection();
            DataGridViewRow preferred = files.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag == before)
                ?? files.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => (r.Tag as DxfFileResult)?.Status != "OK") ?? files.Rows.Cast<DataGridViewRow>().FirstOrDefault();
            if (preferred != null) { preferred.Selected = true; files.CurrentCell = preferred.Cells[0]; }
            selecting = false; RefreshMetrics(); SelectFile();
        }
        private DxfFileResult SelectedFile() { return files.SelectedRows.Count > 0 ? files.SelectedRows[0].Tag as DxfFileResult : null; }
        private void OpenSelectedForEditing()
        {
            if (running) return;
            var file = SelectedFile();
            if (file == null) return;
            try {
                PrepareEditingCopy(file); SelectFile();
                Open(file.OutputPath);
            }
            catch (Exception ex) { status.ForeColor = Amber; status.Text = "Không mở được DXF để sửa: " + ex.Message; }
        }
        private void PrepareEditingCopy(DxfFileResult file)
        {
            if (!exportReview || file.OutputPath != null || file.Status != "OK" || !File.Exists(file.SourcePath)) return;
            string workFolder = Path.GetFullPath(Path.Combine(result.OutputFolder, "CHINH_SUA"));
            string workPath = Path.GetFullPath(Path.Combine(workFolder, file.RelativeName));
            if (!workPath.StartsWith(workFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Đường dẫn bản chỉnh sửa nằm ngoài thư mục Check DXF.");
            Directory.CreateDirectory(Path.GetDirectoryName(workPath));
            if (!File.Exists(workPath)) File.Copy(file.SourcePath, workPath);
            file.OutputPath = workPath;
        }
        private void SelectFile()
        {
            DxfFileResult file = SelectedFile(); selecting = true; issues.Rows.Clear();
            selectedName.Text = file?.RelativeName ?? "Biên dạng DXF";
            selectedStatus.Text = file == null ? "" : file.Status == "OK" ? file.ReviewStatus : file.Status == "NG" ? "NG (" + file.Issues.Count + " lỗi)" : "LỖI ĐỌC";
            selectedStatus.ForeColor = file?.Status == "OK" ? Green : Red;
            bool hasEditableCopy = file?.OutputPath != null && File.Exists(file.OutputPath);
            editButton.Enabled = !running && (hasEditableCopy || exportReview && file?.Status == "OK" && File.Exists(file.SourcePath));
            cleanButton.Enabled = !running && hasEditableCopy;
            writeBackButton.Enabled = !running && file?.Status == "OK" && file.CleanPath != null && File.Exists(file.CleanPath);
            sourceTarget.Text = file == null ? "File gốc: chưa chọn DXF" : "File gốc: " + file.SourcePath;
            issuePage.Text = file == null ? "Vị trí lỗi (0)" : file.Issues.Count > 0 ? "Vị trí lỗi (" + file.Issues.Count + ")" : "Vị trí lỗi (0)";
            issueTitle.Text = file == null ? "Chọn file DXF để xem vị trí lỗi." : file.Issues.Count > 0 ? "Phát hiện " + file.Issues.Count + " vị trí lỗi cần sửa:" : file.Error != null ? "Không đọc được file DXF: " + file.Error
                : file.Options.Conditions == DxfCheckCondition.All ? "Biên dạng đạt yêu cầu (0 lỗi)." : "Đạt các điều kiện đã tick (0 lỗi).";
            issueTitle.ForeColor = file == null ? Muted : file.Issues.Count > 0 ? Red : file.Error != null ? Amber : Green;
            if (file != null) {
                foreach (DxfIssue issue in file.Issues) { int row = issues.Rows.Add("NG" + issue.Id, IssueName(issue.Code), issue.Description); issues.Rows[row].Tag = issue; }
                if (file.Error != null) issues.Rows.Add("", "Không đọc được", file.Error);
            }
            yellowGrid.Rows.Clear();
            ResetYellowSummary(file);
            if (file != null && file.Error == null && file.Options.Has(DxfCheckCondition.YellowLayers)) {
                foreach (DxfShapeStatistic shape in file.YellowShapes) {
                    int row = yellowGrid.Rows.Add(shape.Layer, shape.ShapeName, shape.Count, shape.ColorSource);
                    yellowGrid.Rows[row].Tag = shape;
                    foreach (DataGridViewCell cell in yellowGrid.Rows[row].Cells) cell.ToolTipText = shape.Layer + " · " + shape.Space + " · " + shape.EntityCount + " đối tượng DXF" + (string.IsNullOrEmpty(shape.Note) ? "" : " · " + shape.Note) + (shape.Edges.Count > 0 ? "\r\nBấm để phóng tới và làm nổi bật các biên dạng thuộc dòng này." : "\r\nKhông có biên dạng cắt để định vị.");
                }
                if (file.YellowShapes.Count == 0) yellowSummary.Text = "Không có layer vàng hoặc đối tượng vàng riêng.";
            }
            yellowGrid.ClearSelection();
            closureGrid.Rows.Clear();
            closureSummary.Text = file == null ? "Chọn DXF để xem độ kín biên dạng." : file.Error != null ? "Không kiểm tra được: DXF có lỗi đọc." : file.Contours.Count(c => c.Role == "Outer") + " biên ngoài  ·  " + file.Contours.Count(c => c.Role == "Inner") + " biên bên trong  ·  " + file.Contours.Count(c => !c.Closed) + " biên chưa nối kín  ·  Ngưỡng: 0.001 mm";
            if (file != null && !file.Options.Has(DxfCheckCondition.Closure)) closureSummary.Text = "Không chọn điều kiện biên kín / đầu nét nối đúng.";
            if (file != null && file.Error == null && file.Options.Has(DxfCheckCondition.Closure)) foreach (DxfContourResult contour in file.Contours) {
                int row = closureGrid.Rows.Add("C" + contour.Id, contour.RoleName, contour.Parent == null ? "—" : "C" + contour.Parent.Id, contour.ClosureName, contour.GapLength);
                closureGrid.Rows[row].Tag = contour;
                foreach (DataGridViewCell cell in closureGrid.Rows[row].Cells) cell.ToolTipText = contour.Space + " · " + contour.Layers + " · " + contour.OpenGapCount + " khoảng hở · " + contour.Gaps.Count(g => g.Untrimmed) + " chỗ cần cắt/nối đầu nét · Ngưỡng nối: 0.001 mm" + (string.IsNullOrEmpty(contour.Note) ? "" : " · " + contour.Note);
            }
            closureGrid.ClearSelection();
            issues.ClearSelection(); selecting = false; preview.Set(file, null);
        }
        private void SelectIssue() { preview.Set(SelectedFile(), issues.SelectedRows.Count > 0 ? issues.SelectedRows[0].Tag as DxfIssue : null); }
        private void ResetYellowSummary(DxfFileResult file)
        {
            yellowSummary.ForeColor = Muted;
            yellowSummary.Text = file == null ? "Chọn DXF để xem thống kê layer vàng." : file.Error != null ? "Không thống kê được: DXF có lỗi đọc." : !HasYellow(file) ? "Không có đối tượng vàng." : file.YellowLayerWithEntitiesCount + " layer vàng có đối tượng  ·  " + file.YellowContourCount + " biên dạng kín";
            if (file != null && !file.Options.Has(DxfCheckCondition.YellowLayers)) yellowSummary.Text = "Không chọn điều kiện thống kê layer / đối tượng vàng.";
        }
        private void SelectYellowShape()
        {
            DxfShapeStatistic shape = yellowGrid.SelectedRows.Count > 0 ? yellowGrid.SelectedRows[0].Tag as DxfShapeStatistic : null;
            if (shape == null) return;
            selecting = true;
            try { issues.ClearSelection(); closureGrid.ClearSelection(); }
            finally { selecting = false; }
            preview.SetYellow(SelectedFile(), shape);
            yellowSummary.ForeColor = Amber;
            yellowSummary.Text = shape.Edges.Count > 0 ? "Đang xem: " + shape.ShapeName + " × " + shape.Count : shape.Shape == "Empty" ? "Layer trống · Không có biên dạng để định vị." : "Không có biên dạng cắt để định vị.";
        }
        private Task CreateCleanCopy() { return RecheckSelectedFile(true); }
        private Task RecheckSelectedFile(bool openResult)
        { return CheckSelectedFile(openResult, false); }
        private Task WriteBackSelectedFile()
        { return CheckSelectedFile(false, true); }
        private Task CheckSelectedFile(bool openResult, bool writeToSource)
        {
            if (running || IsDisposed || Disposing) return Task.FromResult(false);
            DxfFileResult previous = SelectedFile(); DxfBatchResult currentBatch = result;
            if (currentBatch == null || previous?.OutputPath == null) return Task.FromResult(false);
            CancellationTokenSource recheckCancellation = new CancellationTokenSource();
            cancellation = recheckCancellation; running = true;
            chooseButton.Enabled = conditionsButton.Enabled = recursiveBox.Enabled = excelButton.Enabled = outputButton.Enabled = editButton.Enabled = cleanButton.Enabled = writeBackButton.Enabled = false;
            files.Enabled = detailTabs.Enabled = needsRepairBox.Enabled = yellowOnlyBox.Enabled = searchBox.Enabled = false; cancelButton.Enabled = true;
            progress.Maximum = 1; progress.Value = 0; status.ForeColor = Muted;
            status.Text = (writeToSource ? "Đang kiểm tra bản OK để ghi đè DXF gốc  ·  " : "Đang kiểm tra lại DXF đã sửa và lưu  ·  ") + previous.RelativeName;
            return Task.Run(() => {
                DxfRecheckResult outcome = null; Exception failure = null;
                try {
                    try { outcome = writeToSource ? DxfCheckEngine.WriteBack(currentBatch, previous, recheckCancellation.Token) : DxfCheckEngine.Recheck(currentBatch, previous, recheckCancellation.Token); }
                    catch (OperationCanceledException) { outcome = new DxfRecheckResult { Cancelled = true }; }
                    catch (Exception ex) { failure = ex; Trace.WriteLine("[CHECK DXF FILE] Recheck failed: " + ex); }
                    if (!IsDisposed && !Disposing && IsHandleCreated)
                        Invoke((MethodInvoker)(() => FinishRecheck(previous, outcome, failure, recheckCancellation, openResult)));
                }
                catch (Exception ex) { Trace.WriteLine("[CHECK DXF FILE] Recheck completion failed: " + ex); }
                finally { recheckCancellation.Dispose(); }
            });
        }
        private void FinishRecheck(DxfFileResult previous, DxfRecheckResult outcome, Exception failure, CancellationTokenSource recheckCancellation, bool openResult)
        {
            string openPath = null;
            try {
                if (IsDisposed || Disposing) return;
                if (failure != null) { status.ForeColor = Amber; status.Text = "Không kiểm tra lại được: " + failure.Message; return; }
                if (outcome == null || outcome.Cancelled || recheckCancellation.IsCancellationRequested && !outcome.SourceWritten) {
                    status.ForeColor = Amber; status.Text = "Đã dừng kiểm tra lại  ·  " + previous.RelativeName; return;
                }
                DxfFileResult updated = outcome.File;
                // Keep the just-checked row selected, including an NG-to-OK
                // transition while the user was viewing only NG files.
                selecting = true;
                foreach (DataGridViewRow row in files.Rows) if (row.Tag == previous) row.Tag = updated;
                if (updated.Status == "OK" && needsRepairBox.Checked) needsRepairBox.Checked = false;
                if (!HasYellow(updated) && yellowOnlyBox.Checked) yellowOnlyBox.Checked = false;
                selecting = false;
                DisplayBatch(outcome.Batch); detailTabs.SelectedTab = issuePage;
                progress.Value = 1;
                status.ForeColor = updated.Status == "OK" ? Green : updated.Status == "NG" ? Red : Amber;
                status.Text = "Kiểm tra lại  ·  " + updated.RelativeName + "  ·  " + updated.Status + (updated.Status == "NG" ? " (" + updated.Issues.Count + " lỗi) · Sửa tiếp trên bản đánh dấu mới." : updated.Status == "OK" ? " · Đã tạo DXF bỏ dấu NG. Có thể ghi đè DXF gốc hoặc copy." : " · " + updated.Error);
                if (outcome.SourceWritten) { status.ForeColor = Green; status.Text = "Đã ghi đè DXF gốc  ·  " + updated.RelativeName + "  ·  OK  ·  Bản cũ đã sao lưu trong SAO_LUU_GOC."; }
                else if (outcome.SourceWriteError != null) { status.ForeColor = updated.Status == "NG" ? Red : Amber; status.Text = outcome.SourceWriteError; }
                if (outcome.ReportWarning != null) status.Text += "  ·  " + outcome.ReportWarning;
                if (openResult && updated.Error == null) openPath = updated.CleanPath ?? updated.OutputPath;
            }
            catch (Exception ex) { Trace.WriteLine("[CHECK DXF FILE] Recheck UI failed: " + ex); status.ForeColor = Amber; status.Text = "Không cập nhật được kết quả kiểm tra lại: " + ex.Message; }
            finally {
                selecting = false; running = false; if (cancellation == recheckCancellation) cancellation = null;
                if (!IsDisposed && !Disposing) {
                    chooseButton.Enabled = conditionsButton.Enabled = recursiveBox.Enabled = !exportReview;
                    files.Enabled = detailTabs.Enabled = needsRepairBox.Enabled = searchBox.Enabled = true;
                    yellowOnlyBox.Enabled = result?.Options.Has(DxfCheckCondition.YellowLayers) ?? true;
                    cancelButton.Enabled = false; SelectFile();
                    if (closeAfterCancel) Close();
                }
            }
            if (!IsDisposed && !Disposing && outcome?.Batch != null) BatchChanged?.Invoke(outcome.Batch);
            if (!IsDisposed && !Disposing && !closeAfterCancel && openPath != null) Open(openPath);
        }
        private void Open(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "CHECK DXF FILE", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }
        private static string IssueName(string code)
        {
            switch (code) {
                case "OVERLAP": return "Đường trùng"; case "ROUND_DEFORMED": return "Lỗ tròn méo"; case "ELLIPSE": return "Ellipse"; case "POLYLINE": return "Polyline"; case "SHORT_EDGE": return "Đoạn < 0,5 mm"; case "HOLE_INTERSECTION": return "Lỗ giao nhau";
                case "SPLINE": return "Đường SPLINE";
                case "CONTOUR_JOIN_UNTRIMMED": return "Đầu nét chưa nối";
                case "OPEN_CONTOUR_OUTER": return "Biên ngoài hở"; case "OPEN_CONTOUR_INNER": return "Lỗ / rãnh hở"; case "OPEN_CONTOUR_UNKNOWN": return "Biên hở chưa phân loại"; case "ROUND_HOLE_TO_EDGE": return "Lỗ tròn thông mép"; case "SPLINE_ROUND_HOLE": return "Lỗ tròn SPLINE"; default: return code;
            }
        }
        private static Label Label(string text, float size, bool bold, Color color)
        { return new Label { Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color, AutoEllipsis = true }; }
        private static ModernButton Button(string text, bool primary, int width, int height = 32)
        {
            return new ModernButton { Text = text, Size = new Size(width, height), BorderRadius = 6, TabStop = true,
                NormalColor = primary ? Blue : Color.White, HoverColor = primary ? Color.FromArgb(29, 83, 185) : Color.FromArgb(240, 245, 252),
                PressColor = primary ? Color.FromArgb(26, 71, 154) : Color.FromArgb(226, 235, 248), BorderColor = primary ? Blue : Color.FromArgb(218, 226, 236),
                ForeColor = primary ? Color.White : Ink, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) };
        }
        private static DataGridView Grid()
        {
            DataGridView grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White, BorderStyle = BorderStyle.None, EnableHeadersVisualStyles = false, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = Color.FromArgb(235, 240, 246),
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, ColumnHeadersHeight = 32, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(243, 246, 250), ForeColor = Muted, Font = new Font("Segoe UI", 9F, FontStyle.Bold), Padding = new Padding(6, 0, 0, 0) },
                DefaultCellStyle = new DataGridViewCellStyle { Font = new Font("Segoe UI", 9.5F), ForeColor = Ink, SelectionBackColor = Color.FromArgb(227, 239, 255), SelectionForeColor = Ink, Padding = new Padding(6, 3, 4, 3) },
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(251, 253, 255) }, RowTemplate = { Height = 30 } };
            UIHelper.EnableDoubleBuffer(grid); return grid;
        }
    }

    internal sealed class DxfCheckConditionsForm : Form
    {
        private static readonly string[] ModeNames = { "Tiêu chuẩn (khuyên dùng)", "Báo thêm SPLINE / POLYLINE", "Tùy chọn riêng" };
        private readonly Dictionary<DxfCheckCondition, CheckBox> conditionBoxes = new Dictionary<DxfCheckCondition, CheckBox>();
        private readonly CheckBox subfoldersBox = new CheckBox { Text = "Quét cả thư mục con", AutoSize = true, Checked = true };
        private readonly ComboBox modeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = Padding.Empty };
        private readonly Label modeDescription = new Label { Dock = DockStyle.Fill, Margin = new Padding(0, 5, 0, 0) };
        private readonly TableLayoutPanel rootLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 18, 14), ColumnCount = 1, RowCount = 6 };
        private readonly TableLayoutPanel ruleGroups = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        private readonly Label selectionSummary = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ModernButton allButton = MakeButton("Chọn tất cả", false, 108), noneButton = MakeButton("Bỏ chọn tất cả", false, 124);
        private readonly ModernButton advancedButton = MakeButton("Xem / chỉnh điều kiện", false, 180);
        private readonly ModernButton startButton;
        private readonly ToolTip tips = new ToolTip { ShowAlways = true, InitialDelay = 300, AutoPopDelay = 10000 };
        private bool applyingMode;
        public DxfCheckOptions SelectedOptions
        { get { return new DxfCheckOptions(conditionBoxes.Where(pair => pair.Value.Checked).Aggregate(DxfCheckCondition.None, (value, pair) => value | pair.Key)); } }
        public bool Recursive { get { return subfoldersBox.Checked; } }

        public DxfCheckConditionsForm(string folder, DxfCheckOptions options, bool recursive)
        {
            options = options ?? DxfCheckOptions.Default;
            Text = "CHECK DXF FILE — Chọn chế độ kiểm tra"; Size = new Size(860, 450); MinimumSize = new Size(740, 430);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; Font = new Font("Segoe UI", 9.5F);
            BackColor = Color.FromArgb(243, 246, 251); ForeColor = Color.FromArgb(24, 42, 66); MaximizeBox = false; MinimizeBox = false;
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 138)); rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            Controls.Add(rootLayout);
            Panel heading = new Panel { Dock = DockStyle.Fill };
            Label title = new Label { Text = "Chọn cách kiểm tra DXF", Font = new Font("Segoe UI", 17F, FontStyle.Bold), Dock = DockStyle.Top, Height = 33 };
            Label subtitle = new Label { Text = "Thông thường dùng Tiêu chuẩn. Chọn Tùy chọn riêng khi cần chỉnh từng điều kiện.", Dock = DockStyle.Bottom, Height = 22, ForeColor = Color.FromArgb(105, 122, 143) };
            heading.Controls.Add(title); heading.Controls.Add(subtitle); rootLayout.Controls.Add(heading, 0, 0);
            TableLayoutPanel folderPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.White, Padding = new Padding(10, 6, 10, 4), Margin = new Padding(0, 3, 0, 8) };
            folderPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 17)); folderPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 23)); folderPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            folderPanel.Controls.Add(new Label { Text = "THƯ MỤC DXF", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(105, 122, 143), Font = new Font("Segoe UI", 8F, FontStyle.Bold) }, 0, 0);
            TextBox pathBox = new TextBox { Text = folder, ReadOnly = true, TabStop = false, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, BackColor = Color.White, ForeColor = ForeColor, Margin = Padding.Empty };
            folderPanel.Controls.Add(pathBox, 0, 1); tips.SetToolTip(pathBox, folder);
            subfoldersBox.Checked = recursive; subfoldersBox.Margin = Padding.Empty; subfoldersBox.ForeColor = Color.FromArgb(105, 122, 143);
            folderPanel.Controls.Add(subfoldersBox, 0, 2); rootLayout.Controls.Add(folderPanel, 0, 1);

            TableLayoutPanel modePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.White, Padding = new Padding(12, 8, 12, 8), Margin = new Padding(0, 0, 0, 8) };
            modePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 18)); modePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); modePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            modePanel.Controls.Add(new Label { Text = "CHẾ ĐỘ KIỂM TRA", Dock = DockStyle.Fill, Margin = Padding.Empty, ForeColor = Color.FromArgb(105, 122, 143), Font = new Font("Segoe UI", 8F, FontStyle.Bold) }, 0, 0);
            modeCombo.Items.AddRange(ModeNames); modePanel.Controls.Add(modeCombo, 0, 1); modePanel.Controls.Add(modeDescription, 0, 2);
            rootLayout.Controls.Add(modePanel, 0, 2);

            ruleGroups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 61)); ruleGroups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 39));
            Panel geometry = CreateGroup("ĐIỀU KIỆN BÁO NG", true, options), reports = CreateGroup("THỐNG KÊ VÀ ĐỐI CHIẾU", false, options);
            geometry.Margin = new Padding(0, 0, 10, 0); reports.Margin = Padding.Empty;
            ruleGroups.Controls.Add(geometry, 0, 0); ruleGroups.Controls.Add(reports, 1, 0); rootLayout.Controls.Add(ruleGroups, 0, 3);
            rootLayout.Controls.Add(selectionSummary, 0, 4);

            TableLayoutPanel footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            FlowLayoutPanel left = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            allButton.Click += (s, e) => SetAll(true); noneButton.Click += (s, e) => SetAll(false);
            advancedButton.Click += (s, e) => modeCombo.SelectedIndex = 2;
            left.Controls.Add(allButton); left.Controls.Add(noneButton); left.Controls.Add(advancedButton);
            FlowLayoutPanel right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            ModernButton cancel = MakeButton("Quay lại", false, 86); cancel.DialogResult = DialogResult.Cancel;
            startButton = MakeButton("Bắt đầu kiểm tra", true, 164);
            startButton.Click += (s, e) => { if (SelectedOptions.HasAny) { DialogResult = DialogResult.OK; Close(); } };
            right.Controls.Add(cancel); right.Controls.Add(startButton); footer.Controls.Add(left, 0, 0); footer.Controls.Add(right, 1, 0);
            rootLayout.Controls.Add(footer, 0, 5); AcceptButton = startButton; CancelButton = cancel;
            modeCombo.SelectedIndexChanged += (s, e) => SelectMode();
            modeCombo.SelectedIndex = ModeIndex(options);
            UpdateSelection();
            ActiveControl = startButton;
        }
        private static int ModeIndex(DxfCheckOptions options)
        { return options.Conditions == DxfCheckOptions.Default.Conditions ? 0 : options.Conditions == DxfCheckCondition.All ? 1 : 2; }
        internal static string ModeName(DxfCheckOptions options) { return ModeNames[ModeIndex(options)]; }
        private void SelectMode()
        {
            if (applyingMode) return;
            int index = modeCombo.SelectedIndex;
            if (index < 0) return;
            applyingMode = true;
            try {
                if (index != 2) {
                    DxfCheckOptions preset = index == 0 ? DxfCheckOptions.Default : DxfCheckOptions.All;
                    foreach (var pair in conditionBoxes) pair.Value.Checked = preset.Has(pair.Key);
                }
            }
            finally { applyingMode = false; }
            modeDescription.Text = index == 0
                ? "Kiểm tra các lỗi hình học, độ kín, màu vàng và tên file trùng.\r\nPolyline hợp lệ được chấp nhận; lỗ tròn bên trong bằng SPLINE báo NG."
                : index == 1 ? "Kiểm tra như Tiêu chuẩn và báo thêm NG cho mọi SPLINE, POLYLINE / LWPOLYLINE.\r\nÁp dụng cả với biên dạng kín hoặc slot hợp lệ."
                : "Bật / tắt từng điều kiện bên dưới theo nhu cầu.\r\nKết quả OK/NG chỉ phản ánh các mục đã tick.";
            bool expanded = index == 2;
            SuspendLayout(); rootLayout.SuspendLayout();
            ruleGroups.Visible = allButton.Visible = noneButton.Visible = expanded; advancedButton.Visible = !expanded;
            rootLayout.RowStyles[3].SizeType = expanded ? SizeType.Percent : SizeType.Absolute;
            rootLayout.RowStyles[3].Height = expanded ? 100 : 0;
            int availableHeight = Screen.FromControl(this).WorkingArea.Height;
            MinimumSize = new Size(740, Math.Min(expanded ? 680 : 430, availableHeight));
            Height = Math.Max(MinimumSize.Height, Math.Min(expanded ? 890 : 450, availableHeight));
            rootLayout.ResumeLayout(true); ResumeLayout(true);
            if (IsHandleCreated) {
                Rectangle area = Screen.FromControl(this).WorkingArea;
                Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
            }
            UpdateSelection();
        }
        private Panel CreateGroup(string title, bool geometry, DxfCheckOptions options)
        {
            Panel card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12, 8, 10, 8) };
            Panel scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            TableLayoutPanel items = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = Padding.Empty };
            int row = 0;
            foreach (DxfCheckRule rule in DxfCheckOptions.Rules.Where(rule => rule.Geometry == geometry)) {
                CheckBox box = new CheckBox { Text = rule.Name, Checked = options.Has(rule.Condition), Dock = DockStyle.Fill,
                    Height = geometry ? 37 : 54, AutoSize = false, AutoEllipsis = true, Margin = new Padding(0, 1, 0, 1), Tag = rule.Condition };
                box.CheckedChanged += (s, e) => {
                    if (startButton == null || applyingMode) return;
                    if (modeCombo.SelectedIndex != 2) modeCombo.SelectedIndex = 2;
                    UpdateSelection();
                };
                conditionBoxes.Add(rule.Condition, box); tips.SetToolTip(box, rule.Hint);
                items.RowStyles.Add(new RowStyle(SizeType.Absolute, box.Height + 2)); items.Controls.Add(box, 0, row++);
            }
            string note = geometry ? "Hai mục “Báo NG mọi…” mặc định bỏ tick. Đơn vị mm; kiểm tra mọi layer." : "Cảnh báo và thống kê được hiển thị riêng.\r\n\r\nCác mục bỏ tick sẽ không được kiểm tra trong lượt này.";
            Label help = new Label { Text = note, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(105, 122, 143), Font = new Font("Segoe UI", 9F), Margin = new Padding(0, 10, 0, 0) };
            items.RowStyles.Add(new RowStyle(SizeType.Absolute, geometry ? 58 : 110)); items.Controls.Add(help, 0, row);
            scroll.Controls.Add(items);
            Label header = new Label { Text = title, Font = new Font("Segoe UI", 9F, FontStyle.Bold), Dock = DockStyle.Top, Height = 29,
                ForeColor = geometry ? Color.FromArgb(37, 99, 211) : Color.FromArgb(166, 106, 21) };
            card.Controls.Add(scroll); card.Controls.Add(header); return card;
        }
        private static ModernButton MakeButton(string text, bool primary, int width)
        {
            return new ModernButton { Text = text, Size = new Size(width, 34), BorderRadius = 5, Margin = new Padding(0, 5, 6, 0),
                NormalColor = primary ? Color.FromArgb(37, 99, 211) : Color.White, HoverColor = primary ? Color.FromArgb(29, 83, 185) : Color.FromArgb(240, 245, 252),
                PressColor = primary ? Color.FromArgb(26, 71, 154) : Color.FromArgb(226, 235, 248), BorderColor = primary ? Color.FromArgb(37, 99, 211) : Color.FromArgb(218, 226, 236),
                ForeColor = primary ? Color.White : Color.FromArgb(24, 42, 66), Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) };
        }
        private void SetAll(bool value)
        {
            if (modeCombo.SelectedIndex != 2) modeCombo.SelectedIndex = 2;
            applyingMode = true;
            try { foreach (CheckBox box in conditionBoxes.Values) box.Checked = value; }
            finally { applyingMode = false; }
            UpdateSelection();
        }
        private void UpdateSelection()
        {
            DxfCheckOptions options = SelectedOptions; startButton.Enabled = options.HasAny;
            selectionSummary.Text = !options.HasAny ? "Hãy tick ít nhất một điều kiện để bắt đầu."
                : modeCombo.SelectedIndex == 2 ? "Đã chọn " + options.Count + " / " + DxfCheckOptions.Rules.Count + " điều kiện. Kết quả OK/NG dựa trên các mục đã tick."
                : "Sẵn sàng kiểm tra theo chế độ " + ModeNames[modeCombo.SelectedIndex] + ".";
            selectionSummary.ForeColor = options.HasAny ? Color.FromArgb(105, 122, 143) : Color.FromArgb(207, 65, 78);
        }
        protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
    }

    internal sealed class DxfStatPill : Control
    {
        private readonly string prefix;
        private readonly Color textColor;
        private readonly Color bgColor;
        private readonly Color borderColor;
        private int count;

        public int Count
        {
            get => count;
            set
            {
                if (count != value)
                {
                    count = value;
                    Invalidate();
                }
            }
        }

        public DxfStatPill(string prefix, Color textColor, Color bgColor, Color borderColor)
        {
            this.prefix = prefix;
            this.textColor = textColor;
            this.bgColor = bgColor;
            this.borderColor = borderColor;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            int textW = TextRenderer.MeasureText(prefix + ": 9999", Font).Width;
            Size = new Size(Math.Max(76, textW + 16), 32);
            Margin = new Padding(0, 1, 6, 1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = UIHelper.GetRoundPath(rect, 6))
            using (SolidBrush brush = new SolidBrush(bgColor))
            using (Pen pen = new Pen(borderColor))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
            string text = prefix + ": " + count.ToString(CultureInfo.InvariantCulture);
            TextRenderer.DrawText(e.Graphics, text, Font, ClientRectangle, textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    internal sealed class DxfLogo : Control
    {
        public DxfLogo() { DoubleBuffered = true; Size = new Size(28, 28); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = UIHelper.GetRoundPath(new Rectangle(0, 0, Width - 1, Height - 1), 5))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(37, 99, 211)))
                e.Graphics.FillPath(fill, path);
            using (Font f = new Font("Segoe UI", 8F, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, "DXF", f, ClientRectangle, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    internal sealed class DxfPreview : Control
    {
        internal bool ShowIssueLegend { get; set; } = true;
        private DxfFileResult file; private DxfIssue selected; private DxfShapeStatistic selectedShape;
        private DxfPartComparison comparison;
        private List<List<DxfPoint>> comparisonOverlay;
        private DxfBox comparisonBounds;
        private DxfPoint comparisonA, comparisonB;
        private double zoom = 1; private PointF pan; private Point dragStart; private bool dragging;
        public DxfPreview() { Dock = DockStyle.Fill; BackColor = Color.White; DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
        public void Set(DxfFileResult value, DxfIssue issue) { file = value; selected = issue; selectedShape = null; comparison = null; comparisonOverlay = null; zoom = 1; pan = PointF.Empty; Invalidate(); }
        public void SetYellow(DxfFileResult value, DxfShapeStatistic shape)
        { Set(value, null); if (value != null && shape != null && shape.Edges.Count > 0) selectedShape = shape; Invalidate(); }
        public void SetComparison(DxfFileResult value, DxfPartComparison match, bool focusDifference)
        {
            Set(value, null); if (value == null || match == null) return;
            comparison = match; DxfTransform transform = match.Alignment(value);
            comparisonOverlay = match.Other(value).Edges.Where(edge => string.Equals(edge.Space, "MODEL", StringComparison.OrdinalIgnoreCase))
                .Select(edge => edge.Preview.Select(transform.Apply).ToList()).ToList();
            comparisonBounds = DxfBox.From(value.Edges.Where(edge => string.Equals(edge.Space, "MODEL", StringComparison.OrdinalIgnoreCase)).SelectMany(edge => edge.Preview).Concat(comparisonOverlay.SelectMany(points => points)));
            comparisonA = match.DifferencePoint(value);
            comparisonB = value == match.First ? match.DifferenceB : transform.Apply(match.DifferenceA);
            if (focusDifference && !match.Exact) {
                double radius = Math.Max(8, Math.Max(match.MaxBoundaryGap, match.MaxInnerGap) * 2.5);
                DxfPoint center = (comparisonA + comparisonB) * .5;
                comparisonBounds = new DxfBox { MinX = center.X - radius, MaxX = center.X + radius, MinY = center.Y - radius, MaxY = center.Y + radius };
            }
            Invalidate();
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e); if (file == null) return;
            double before = zoom; zoom = Math.Max(.15, Math.Min(250, zoom * (e.Delta > 0 ? 1.25 : .8))); double ratio = zoom / before;
            pan = new PointF((float)((pan.X - (e.X - Width / 2.0)) * ratio + e.X - Width / 2.0), (float)((pan.Y - (e.Y - Height / 2.0)) * ratio + e.Y - Height / 2.0)); Invalidate();
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); if (e.Button == MouseButtons.Left) { dragging = true; dragStart = e.Location; Capture = true; Cursor = Cursors.SizeAll; } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) { pan.X += e.X - dragStart.X; pan.Y += e.Y - dragStart.Y; dragStart = e.Location; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; Capture = false; Cursor = Cursors.Default; }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) { dragging = false; Cursor = Cursors.Default; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen grid = new Pen(Color.FromArgb(243, 246, 250)))
            { for (int x = 0; x < Width; x += 24) e.Graphics.DrawLine(grid, x, 0, x, Height); for (int y = 0; y < Height; y += 24) e.Graphics.DrawLine(grid, 0, y, Width, y); }
            if (file == null || file.Edges.Count == 0) {
                using (Font title = new Font("Segoe UI", 12F, FontStyle.Bold))
                    TextRenderer.DrawText(e.Graphics, "Chọn một file DXF", title, new Rectangle(0, Height / 2 - 26, Width, 30), Color.FromArgb(82, 104, 130), TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(e.Graphics, ShowIssueLegend ? "Biên dạng và vị trí lỗi sẽ xuất hiện ở đây." : "Biên dạng xuất sẽ hiển thị tại đây.", Font, new Rectangle(0, Height / 2 + 10, Width, 30), Color.FromArgb(132, 147, 166), TextFormatFlags.HorizontalCenter); return;
            }
            DxfBox box = comparison != null ? comparisonBounds : selectedShape != null ? DxfBox.From(selectedShape.Edges.SelectMany(edge => edge.Preview)) : selected == null ? DxfBox.From(file.Edges.SelectMany(edge => edge.Preview)) : selected.Bounds;
            double padding = Math.Max(2, box.Size * .08), w = Math.Max(1, box.Width + 2 * padding), h = Math.Max(1, box.Height + 2 * padding);
            double scale = Math.Max(.001, Math.Min(Math.Max(1, Width - 42) / w, Math.Max(1, Height - 48) / h)) * zoom;
            Func<DxfPoint, PointF> map = p => new PointF((float)(Width / 2.0 + pan.X + (p.X - box.Center.X) * scale), (float)(Height / 2.0 + pan.Y - (p.Y - box.Center.Y) * scale));
            HashSet<string> highlights = new HashSet<string>((selected?.Handle ?? "").Split(';').Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
            HashSet<DxfEdge> yellowEdges = selectedShape == null ? new HashSet<DxfEdge>() : new HashSet<DxfEdge>(selectedShape.Edges);
            using (Pen geometry = new Pen(comparison == null ? Color.FromArgb(51, 70, 93) : Color.FromArgb(37, 99, 211), comparison == null ? 1.2F : 2F)) using (Pen yellow = new Pen(Color.FromArgb(211, 143, 17), 3F)) using (Pen red = new Pen(Color.FromArgb(218, 65, 82), 1.8F)) using (SolidBrush tint = new SolidBrush(Color.FromArgb(16, 218, 65, 82))) {
                foreach (DxfEdge edge in file.Edges) {
                    if (comparison != null && !string.Equals(edge.Space, "MODEL", StringComparison.OrdinalIgnoreCase)) continue;
                    if (selectedShape != null && !string.Equals(edge.Space, selectedShape.Space, StringComparison.OrdinalIgnoreCase)) continue;
                    PointF[] points = edge.Preview.Select(map).ToArray(); string key = edge.Handle + (edge.Segment > 0 ? ":" + edge.Segment : "");
                    if (points.Length > 1) e.Graphics.DrawLines(yellowEdges.Contains(edge) ? yellow : highlights.Contains(key) ? red : geometry, points);
                }
                if (comparison != null) {
                    using (Pen reference = new Pen(Color.FromArgb(232, 143, 23), 1.8F)) {
                        reference.DashStyle = DashStyle.Dash;
                        foreach (List<DxfPoint> line in comparisonOverlay) if (line.Count > 1) e.Graphics.DrawLines(reference, line.Select(map).ToArray());
                    }
                    if (!comparison.Exact) {
                        PointF a = map(comparisonA), b = map(comparisonB);
                        e.Graphics.DrawLine(red, a, b); e.Graphics.DrawEllipse(red, a.X - 7, a.Y - 7, 14, 14);
                        TextRenderer.DrawText(e.Graphics, "Khác", Font, new Point((int)a.X + 9, (int)a.Y - 18), Color.FromArgb(207, 65, 78));
                    }
                }
                foreach (DxfIssue issue in file.Issues) {
                    if (comparison != null) continue;
                    if (selected != null && issue != selected) continue;
                    if (selectedShape != null && !string.Equals(issue.Space, selectedShape.Space, StringComparison.OrdinalIgnoreCase)) continue;
                    PointF a = map(new DxfPoint(issue.Bounds.MinX, issue.Bounds.MaxY)), b = map(new DxfPoint(issue.Bounds.MaxX, issue.Bounds.MinY));
                    RectangleF mark = new RectangleF(a.X - 5, a.Y - 5, Math.Max(10, b.X - a.X + 10), Math.Max(10, b.Y - a.Y + 10));
                    e.Graphics.FillRectangle(tint, mark); e.Graphics.DrawRectangle(red, mark.X, mark.Y, mark.Width, mark.Height);
                    TextRenderer.DrawText(e.Graphics, "NG" + issue.Id, Font, new Point((int)mark.X, (int)mark.Y - 19), Color.FromArgb(207, 65, 78));
                }
            }
            string legend = comparison != null ? "Xanh: " + file.RelativeName + " · Cam: " + comparison.Other(file).RelativeName + " · Đỏ: một vị trí khác"
                : selectedShape == null ? "Biên dạng gốc   ·   Dấu đỏ: vị trí NG" : "Vàng: " + selectedShape.Layer + " · " + selectedShape.ShapeName + " × " + selectedShape.Count + "   ·   Đỏ: vị trí NG";
            if (!ShowIssueLegend) legend = "Flat Pattern · mm · 1:1";
            TextRenderer.DrawText(e.Graphics, legend, Font, new Rectangle(8, Height - 22, Math.Max(1, Width - 16), 20), selectedShape == null ? Color.FromArgb(120, 135, 154) : Color.FromArgb(166, 106, 21), TextFormatFlags.Right | TextFormatFlags.EndEllipsis);
        }
    }
}
