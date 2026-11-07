using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ADDIN.Commands;

namespace ADDIN.UI
{
    internal sealed class SamePartCheckReviewForm : Form
    {
        private readonly SamePartCheckResult result;
        private readonly SamePartReportV2Snapshot reportSnapshot;
        private readonly string excelOutputDirectory;
        private readonly Action<SamePartItemResult, string> navigate;

        private readonly DataGridView groupGrid = Grid("groupGrid");
        private readonly DataGridView partGrid = Grid("partGrid");

        private readonly Label selectedGroupTitle = TextLabel("Chọn nhóm để xem so sánh chi tiết.", true);
        private readonly Label selectedGroupSubtext = TextLabel("", false);
        private readonly Label groupCountBadge = new Label();
        private readonly Label calloutHeader = new Label();
        private readonly Label calloutBody = new Label();
        private readonly Label calloutFooter = new Label();
        private readonly Label status = TextLabel("", false);

        private readonly Panel calloutCard = new Panel();
        private readonly Panel detailBanner = new Panel();

        private readonly ModernButton btnOpenPart = Button("Mở Part 3D", true, 140);
        private readonly ModernButton btnOpenAssembly = Button("Mở Assembly", false, 130);
        private readonly ModernButton btnOpenFolder = Button("Mở thư mục", false, 120);
        private readonly ModernButton btnExportExcel = Button("Xuất Excel...", false, 130);

        private readonly FilterPillButton pillAll;
        private readonly FilterPillButton pillFull;
        private readonly FilterPillButton pillTolerance;
        private readonly FilterPillButton pillGeo;
        private readonly FilterPillButton pillFlat;
        private readonly FilterPillButton pillMirror;
        private readonly FilterPillButton pillUnverified;
        private readonly FilterPillButton pillError;
        private readonly FilterPillButton pillNoMatch;

        private string activeFilter;
        private bool selecting;

        internal SamePartGroupResult SelectedGroup
        {
            get { return groupGrid.CurrentRow?.Tag as SamePartGroupResult; }
        }

        internal SamePartItemResult SelectedItem
        {
            get { return partGrid.CurrentRow?.Tag as SamePartItemResult; }
        }

        internal SamePartCheckReviewForm(SamePartCheckResult completed,
            Action<SamePartItemResult, string> openLocation,
            string excelDir = null)
        {
            result = completed ?? throw new ArgumentNullException(nameof(completed));
            reportSnapshot = result.EnsureSnapshot();
            excelOutputDirectory = !string.IsNullOrWhiteSpace(excelDir) ? excelDir : result.OutputDirectory;
            navigate = openLocation;
            Name = "SamePartCheckReviewForm";
            Text = "TAI TOOL — KẾT QUẢ CHECK SAME PART";
            Size = new Size(1320, 820);
            MinimumSize = new Size(1020, 640);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            ForeColor = TaiTheme.Text;
            BackColor = TaiTheme.Page;
            DoubleBuffered = true;

            ConfigureGrids();

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12, 10, 12, 8),
                BackColor = TaiTheme.Page
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

            // 1. Top Header Card (Style C: Modern SaaS Inspector Ribbon)
            var ribbonCard = CreateCardPanel(new Padding(12, 6, 12, 6));
            var ribbon = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = TaiTheme.Surface,
                Margin = Padding.Empty
            };
            ribbon.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
            ribbon.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ribbon.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138));

            var brandPanel = new Panel { Dock = DockStyle.Fill, BackColor = TaiTheme.Surface, Margin = Padding.Empty };
            brandPanel.Paint += PaintBrandHeader;
            ribbon.Controls.Add(brandPanel, 0, 0);

            var filterBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = TaiTheme.Surface,
                Margin = Padding.Empty,
                Padding = new Padding(0, 9, 8, 0)
            };

            int countFull = result.Groups.Count(g => g.Status == "SAME FULL");
            int countTolerance = result.Groups.Count(g => g.Status == "SAME TOLERANCE");
            int countGeo = result.Groups.Count(g => g.Status == "SAME GEOMETRY");
            int countFlat = result.Groups.Count(g => g.Status == "SAME FLAT");
            int countMirror = result.Groups.Count(g => g.Status == "CHECK MIRROR");
            int countUnverified = reportSnapshot.NotVerifiedCount;
            int countError = reportSnapshot.ErrorCount;
            int countNoMatch = reportSnapshot.NoMatchCount;
            int totalCards = result.Groups.Count + (countUnverified > 0 ? 1 : 0) + (countError > 0 ? 1 : 0) + (countNoMatch > 0 ? 1 : 0);

            pillAll = new FilterPillButton("Tổng", totalCards, null, TaiTheme.Accent, TaiTheme.AccentSoft, TaiTheme.AccentBorder);
            pillFull = new FilterPillButton("Trùng 100%", countFull, "FULL", TaiTheme.Success, TaiTheme.SuccessSoft, TaiTheme.SuccessBorder);
            pillTolerance = new FilterPillButton("Dung sai", countTolerance, "TOLERANCE", Color.FromArgb(13, 148, 136), Color.FromArgb(204, 251, 241), Color.FromArgb(153, 246, 228));
            pillGeo = new FilterPillButton("Trùng 3D", countGeo, "GEOMETRY", TaiTheme.Accent, TaiTheme.AccentSoft, TaiTheme.AccentBorder);
            pillFlat = new FilterPillButton("Trùng Phôi", countFlat, "FLAT", TaiTheme.Warning, TaiTheme.WarningSoft, TaiTheme.WarningBorder);
            pillMirror = new FilterPillButton("Mirror", countMirror, "MIRROR", Color.FromArgb(124, 58, 237), Color.FromArgb(245, 243, 255), Color.FromArgb(221, 214, 254));
            pillUnverified = new FilterPillButton("Chưa xác minh", countUnverified, "UNVERIFIED", Color.FromArgb(217, 119, 6), Color.FromArgb(254, 243, 199), Color.FromArgb(253, 230, 138));
            pillError = new FilterPillButton("Lỗi", countError, "ERROR", TaiTheme.Danger, TaiTheme.DangerSoft, TaiTheme.DangerBorder);
            pillNoMatch = new FilterPillButton("Chưa tìm thấy nhóm trùng", countNoMatch, "NO_MATCH", Color.FromArgb(100, 116, 139), Color.FromArgb(241, 245, 249), Color.FromArgb(203, 213, 225));

            pillAll.Click += (s, e) => ApplyFilter(null);
            pillFull.Click += (s, e) => ApplyFilter(activeFilter == "FULL" ? null : "FULL");
            pillTolerance.Click += (s, e) => ApplyFilter(activeFilter == "TOLERANCE" ? null : "TOLERANCE");
            pillGeo.Click += (s, e) => ApplyFilter(activeFilter == "GEOMETRY" ? null : "GEOMETRY");
            pillFlat.Click += (s, e) => ApplyFilter(activeFilter == "FLAT" ? null : "FLAT");
            pillMirror.Click += (s, e) => ApplyFilter(activeFilter == "MIRROR" ? null : "MIRROR");
            pillUnverified.Click += (s, e) => ApplyFilter(activeFilter == "UNVERIFIED" ? null : "UNVERIFIED");
            pillError.Click += (s, e) => ApplyFilter(activeFilter == "ERROR" ? null : "ERROR");
            pillNoMatch.Click += (s, e) => ApplyFilter(activeFilter == "NO_MATCH" ? null : "NO_MATCH");

            if (countNoMatch > 0) filterBar.Controls.Add(pillNoMatch);
            if (countError > 0) filterBar.Controls.Add(pillError);
            if (countUnverified > 0) filterBar.Controls.Add(pillUnverified);
            if (countMirror > 0 || (countFull == 0 && countTolerance == 0 && countGeo == 0 && countFlat == 0)) filterBar.Controls.Add(pillMirror);
            filterBar.Controls.Add(pillFlat);
            filterBar.Controls.Add(pillGeo);
            if (countTolerance > 0) filterBar.Controls.Add(pillTolerance);
            filterBar.Controls.Add(pillFull);
            filterBar.Controls.Add(pillAll);
            ribbon.Controls.Add(filterBar, 1, 0);

            var reportWrap = new Panel { Dock = DockStyle.Fill, BackColor = TaiTheme.Surface, Padding = new Padding(4, 9, 0, 9), Margin = Padding.Empty };
            btnExportExcel.Name = "btnExportExcel";
            btnExportExcel.Height = 34;
            btnExportExcel.Dock = DockStyle.Fill;
            btnExportExcel.Click += btnExportExcel_Click;
            btnExportExcel.Enabled = true;
            reportWrap.Controls.Add(btnExportExcel);
            ribbon.Controls.Add(reportWrap, 2, 0);

            ribbonCard.Controls.Add(ribbon);
            root.Controls.Add(ribbonCard, 0, 0);

            // 2. Main Workbench (Left Master Card + Right Inspector Card)
            var workbench = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 10, 0, 0),
                BackColor = TaiTheme.Page
            };
            workbench.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36F));
            workbench.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64F));

            // Left Master Card (Nhóm chi tiết trùng lặp)
            var itemCard = CreateCardPanel(new Padding(1));
            itemCard.Margin = new Padding(0, 0, 9, 0);
            var itemPane = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = TaiTheme.Surface,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            itemPane.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            itemPane.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Dark Navy Banner for Left Header
            var itemHeader = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 41, 59),
                Margin = Padding.Empty,
                Padding = new Padding(26, 0, 12, 0)
            };
            itemHeader.Paint += PaintLeftHeader;

            var itemTitle = TextLabel("NHÓM CHI TIẾT ĐỒNG DẠNG", true);
            itemTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            itemTitle.ForeColor = Color.White;

            groupCountBadge.AutoSize = false;
            groupCountBadge.Dock = DockStyle.Right;
            groupCountBadge.Width = 120;
            groupCountBadge.TextAlign = ContentAlignment.MiddleRight;
            groupCountBadge.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            groupCountBadge.ForeColor = Color.FromArgb(203, 213, 225);
            groupCountBadge.BackColor = Color.Transparent;

            itemHeader.Controls.Add(itemTitle);
            itemHeader.Controls.Add(groupCountBadge);

            var itemGridWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 4, 6, 6), BackColor = TaiTheme.Surface, Margin = Padding.Empty };
            itemGridWrap.Controls.Add(groupGrid);
            itemPane.Controls.Add(itemHeader, 0, 0);
            itemPane.Controls.Add(itemGridWrap, 0, 1);
            itemCard.Controls.Add(itemPane);
            workbench.Controls.Add(itemCard, 0, 0);

            // Right Inspector Card
            var detailCard = CreateCardPanel(new Padding(1));
            detailCard.Margin = Padding.Empty;
            var detailPane = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = TaiTheme.Surface,
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };
            detailPane.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            detailPane.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            detailPane.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
            detailPane.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

            // Selected Group Header Banner (Dark Navy)
            detailBanner.Dock = DockStyle.Fill;
            detailBanner.BackColor = Color.FromArgb(30, 41, 59);
            detailBanner.Padding = new Padding(26, 10, 120, 8);
            detailBanner.Margin = Padding.Empty;
            UIHelper.EnableDoubleBuffer(detailBanner);
            detailBanner.Paint += PaintDetailBanner;
            detailBanner.Resize += (s, e) => detailBanner.Invalidate();

            selectedGroupTitle.Name = "selectedGroupTitle";
            selectedGroupTitle.Dock = DockStyle.Top;
            selectedGroupTitle.Height = 24;
            selectedGroupTitle.Font = new Font("Segoe UI", 11.5F, FontStyle.Bold);
            selectedGroupTitle.ForeColor = Color.White;
            selectedGroupTitle.AutoEllipsis = true;
            selectedGroupTitle.BackColor = Color.Transparent;

            selectedGroupSubtext.Dock = DockStyle.Fill;
            selectedGroupSubtext.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            selectedGroupSubtext.ForeColor = Color.FromArgb(148, 163, 184);
            selectedGroupSubtext.AutoEllipsis = true;
            selectedGroupSubtext.BackColor = Color.Transparent;

            detailBanner.Controls.Add(selectedGroupSubtext);
            detailBanner.Controls.Add(selectedGroupTitle);
            detailPane.Controls.Add(detailBanner, 0, 0);

            var partGridWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4), BackColor = TaiTheme.Surface, Margin = Padding.Empty };
            partGridWrap.Controls.Add(partGrid);
            detailPane.Controls.Add(partGridWrap, 0, 1);

            // Callout Card for Difference / Similarity Analysis
            var calloutWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 6), BackColor = TaiTheme.Surface, Margin = Padding.Empty };
            calloutCard.Dock = DockStyle.Fill;
            calloutCard.Padding = new Padding(20, 10, 16, 10);
            calloutCard.BackColor = TaiTheme.WarningSoft;
            UIHelper.EnableDoubleBuffer(calloutCard);
            calloutCard.Paint += PaintCalloutCard;
            calloutCard.Resize += (s, e) => calloutCard.Invalidate();

            calloutHeader.Dock = DockStyle.Top;
            calloutHeader.Height = 24;
            calloutHeader.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            calloutHeader.ForeColor = TaiTheme.Success;
            calloutHeader.BackColor = Color.Transparent;

            calloutBody.Dock = DockStyle.Fill;
            calloutBody.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            calloutBody.ForeColor = TaiTheme.Text;
            calloutBody.BackColor = Color.Transparent;

            calloutFooter.Dock = DockStyle.Bottom;
            calloutFooter.Height = 24;
            calloutFooter.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            calloutFooter.ForeColor = TaiTheme.TextMuted;
            calloutFooter.BackColor = Color.Transparent;

            calloutCard.Controls.Add(calloutBody);
            calloutCard.Controls.Add(calloutHeader);
            calloutCard.Controls.Add(calloutFooter);
            calloutWrap.Controls.Add(calloutCard);
            detailPane.Controls.Add(calloutWrap, 0, 2);

            // Bottom Action Bar
            var actionFooter = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(250, 251, 253),
                Padding = new Padding(12, 8, 16, 8),
                Margin = Padding.Empty
            };
            actionFooter.Paint += (s, e) =>
            {
                using (var pen = new Pen(TaiTheme.Divider))
                    e.Graphics.DrawLine(pen, 0, 0, actionFooter.Width, 0);
            };

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            actions.Controls.Add(btnOpenPart);
            actions.Controls.Add(btnOpenAssembly);
            actions.Controls.Add(btnOpenFolder);

            var hintLabel = TextLabel("Double-click vào dòng chi tiết để mở nhanh Part 3D trong SOLIDWORKS", false);
            hintLabel.Dock = DockStyle.Fill;
            hintLabel.TextAlign = ContentAlignment.MiddleRight;
            hintLabel.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            hintLabel.ForeColor = TaiTheme.TextMuted;
            hintLabel.BackColor = Color.Transparent;

            actionFooter.Controls.Add(hintLabel);
            actionFooter.Controls.Add(actions);
            detailPane.Controls.Add(actionFooter, 0, 3);

            detailCard.Controls.Add(detailPane);
            workbench.Controls.Add(detailCard, 1, 0);
            root.Controls.Add(workbench, 0, 1);

            // 3. Footer Status Bar
            var statusWrap = new Panel { Dock = DockStyle.Fill, BackColor = TaiTheme.Page, Padding = new Padding(6, 4, 6, 0), Margin = Padding.Empty };
            status.Name = "reviewStatus";
            status.Text = result.Message;
            status.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            status.ForeColor = TaiTheme.TextMuted;
            status.AutoEllipsis = true;
            statusWrap.Controls.Add(status);
            root.Controls.Add(statusWrap, 0, 2);

            Controls.Add(root);

            // Events
            groupGrid.SelectionChanged += (sender, args) =>
            {
                groupGrid.Invalidate();
                SelectGroup();
            };
            partGrid.SelectionChanged += (sender, args) =>
            {
                partGrid.Invalidate();
                UpdateActionButtons();
            };
            partGrid.CellDoubleClick += (sender, args) =>
            {
                if (args.RowIndex >= 0) NavigateSelected("part");
            };
            btnOpenPart.Click += (sender, args) => NavigateSelected("part");
            btnOpenAssembly.Click += (sender, args) => NavigateSelected("assembly");
            btnOpenFolder.Click += (sender, args) => NavigateSelected("folder");

            UpdateFilterPills();
            DisplayGroups();
        }

        private void ConfigureGrids()
        {
            // Left Master Grid (Groups)
            groupGrid.Columns.Add("Status", "Kết quả");
            groupGrid.Columns.Add("Group", "Nhóm");
            groupGrid.Columns.Add("Count", "SL");
            groupGrid.Columns.Add("Parts", "Chi tiết trong nhóm");
            groupGrid.Columns[0].Width = 175;
            groupGrid.Columns[1].Width = 60;
            groupGrid.Columns[1].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            groupGrid.Columns[1].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            groupGrid.Columns[2].Width = 50;
            groupGrid.Columns[2].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            groupGrid.Columns[2].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            groupGrid.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            groupGrid.Columns[3].DefaultCellStyle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);

            // Right Inspector Grid (Parts in selected group)
            partGrid.Columns.Add("BuhinNo", "BOM (部品番号)");
            partGrid.Columns.Add("ComponentName", "Tên chi tiết / Model");
            partGrid.Columns.Add("Material", "Vật liệu");
            partGrid.Columns.Add("Thickness", "Bề dày");
            partGrid.Columns.Add("Config", "Cấu hình");
            partGrid.Columns.Add("FlatConfig", "Cấu hình trải");
            partGrid.Columns.Add("PartPath", "Đường dẫn File Part");
            partGrid.Columns[0].Width = 135;
            partGrid.Columns[0].DefaultCellStyle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            partGrid.Columns[1].Width = 180;
            partGrid.Columns[2].Width = 100;
            partGrid.Columns[3].Width = 75;
            partGrid.Columns[4].Width = 110;
            partGrid.Columns[5].Width = 110;
            partGrid.Columns[6].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            partGrid.Columns[6].DefaultCellStyle.ForeColor = TaiTheme.TextMuted;

            groupGrid.CellPainting += PaintGroupCell;
        }

        private void ApplyFilter(string filter)
        {
            if (string.Equals(activeFilter, filter, StringComparison.OrdinalIgnoreCase)) return;
            activeFilter = filter;
            UpdateFilterPills();
            DisplayGroups();
        }

        private void UpdateFilterPills()
        {
            pillAll.IsActive = activeFilter == null;
            pillFull.IsActive = string.Equals(activeFilter, "FULL", StringComparison.OrdinalIgnoreCase);
            pillTolerance.IsActive = string.Equals(activeFilter, "TOLERANCE", StringComparison.OrdinalIgnoreCase);
            pillGeo.IsActive = string.Equals(activeFilter, "GEOMETRY", StringComparison.OrdinalIgnoreCase);
            pillFlat.IsActive = string.Equals(activeFilter, "FLAT", StringComparison.OrdinalIgnoreCase);
            pillMirror.IsActive = string.Equals(activeFilter, "MIRROR", StringComparison.OrdinalIgnoreCase);
            pillUnverified.IsActive = string.Equals(activeFilter, "UNVERIFIED", StringComparison.OrdinalIgnoreCase);
            pillError.IsActive = string.Equals(activeFilter, "ERROR", StringComparison.OrdinalIgnoreCase);
            pillNoMatch.IsActive = string.Equals(activeFilter, "NO_MATCH", StringComparison.OrdinalIgnoreCase);
        }

        internal void PositionOnScreen(Rectangle workingArea)
        {
            StartPosition = FormStartPosition.Manual;
            Width = Math.Min(Width, workingArea.Width);
            Height = Math.Min(Height, workingArea.Height);
            Left = workingArea.Left + Math.Max(0, (workingArea.Width - Width) / 2);
            Top = workingArea.Top + Math.Max(0, (workingArea.Height - Height) / 2);
        }

        private void DisplayGroups()
        {
            selecting = true;
            try
            {
                groupGrid.Rows.Clear();
                var allGroups = result.Groups.Where(g => g != null).ToList();

                List<SamePartGroupResult> filtered = new List<SamePartGroupResult>();
                if (string.IsNullOrEmpty(activeFilter))
                {
                    filtered.AddRange(allGroups);
                }
                else if (string.Equals(activeFilter, "FULL", StringComparison.OrdinalIgnoreCase))
                {
                    filtered.AddRange(allGroups.Where(g => g.Status == "SAME FULL"));
                }
                else if (string.Equals(activeFilter, "TOLERANCE", StringComparison.OrdinalIgnoreCase))
                {
                    filtered.AddRange(allGroups.Where(g => g.Status == "SAME TOLERANCE"));
                }
                else if (string.Equals(activeFilter, "GEOMETRY", StringComparison.OrdinalIgnoreCase))
                {
                    filtered.AddRange(allGroups.Where(g => g.Status == "SAME GEOMETRY"));
                }
                else if (string.Equals(activeFilter, "FLAT", StringComparison.OrdinalIgnoreCase))
                {
                    filtered.AddRange(allGroups.Where(g => g.Status == "SAME FLAT"));
                }
                else if (string.Equals(activeFilter, "MIRROR", StringComparison.OrdinalIgnoreCase))
                {
                    filtered.AddRange(allGroups.Where(g => g.Status == "CHECK MIRROR"));
                }

                foreach (SamePartGroupResult group in filtered)
                {
                    string partList = FormatBuhinList(group.Items);
                    int index = groupGrid.Rows.Add(
                        FormatStatusLabel(group.Status),
                        "G" + group.GroupNumber,
                        group.Items.Count.ToString(),
                        partList);
                    groupGrid.Rows[index].Tag = group;
                    ColorGroupRow(groupGrid.Rows[index], group.Status);
                }

                // Unverified items (from result.Errors classified as UNVERIFIED)
                var unverifiedItems = result.Errors.Where(e => e != null && SamePartReportV2Snapshot.ClassifyFailure(e.Error) == "UNVERIFIED").ToList();
                if ((string.IsNullOrEmpty(activeFilter) || string.Equals(activeFilter, "UNVERIFIED", StringComparison.OrdinalIgnoreCase)) && unverifiedItems.Count > 0)
                {
                    var fakeUnverifiedGroup = new SamePartGroupResult
                    {
                        GroupNumber = 0,
                        Status = "UNVERIFIED",
                        Note = "Chi tiết chưa đủ dữ liệu hình học hoặc biên dạng cong phức tạp để xác minh tự động (chưa chứng minh khác nhau từng cặp)."
                    };
                    fakeUnverifiedGroup.Items.AddRange(unverifiedItems);
                    string partList = FormatBuhinList(unverifiedItems);
                    int index = groupGrid.Rows.Add("Chưa xác minh", "UNV", unverifiedItems.Count.ToString(), partList);
                    groupGrid.Rows[index].Tag = fakeUnverifiedGroup;
                    ColorGroupRow(groupGrid.Rows[index], "UNVERIFIED");
                }

                // Error items (from result.Errors classified as ERROR)
                var errorItems = result.Errors.Where(e => e != null && SamePartReportV2Snapshot.ClassifyFailure(e.Error) == "ERROR").ToList();
                if ((string.IsNullOrEmpty(activeFilter) || string.Equals(activeFilter, "ERROR", StringComparison.OrdinalIgnoreCase)) && errorItems.Count > 0)
                {
                    var fakeErrorGroup = new SamePartGroupResult
                    {
                        GroupNumber = 0,
                        Status = "ERROR",
                        Note = "Chi tiết gặp lỗi đọc file hoặc lỗi SOLIDWORKS API khi xử lý."
                    };
                    fakeErrorGroup.Items.AddRange(errorItems);
                    string partList = FormatBuhinList(errorItems);
                    int index = groupGrid.Rows.Add("Lỗi", "ERR", errorItems.Count.ToString(), partList);
                    groupGrid.Rows[index].Tag = fakeErrorGroup;
                    ColorGroupRow(groupGrid.Rows[index], "ERROR");
                }

                // Unmatched items (from result.UnmatchedItems)
                var unmatchedItems = (result.UnmatchedItems ?? new List<SamePartItemResult>()).Where(u => u != null).ToList();
                if ((string.IsNullOrEmpty(activeFilter) || string.Equals(activeFilter, "NO_MATCH", StringComparison.OrdinalIgnoreCase)) && unmatchedItems.Count > 0)
                {
                    var fakeNoMatchGroup = new SamePartGroupResult
                    {
                        GroupNumber = 0,
                        Status = "NO_MATCH",
                        Note = "Chưa tìm thấy nhóm trùng trong lần quét này; chưa chứng minh khác nhau từng cặp."
                    };
                    fakeNoMatchGroup.Items.AddRange(unmatchedItems);
                    string partList = FormatBuhinList(unmatchedItems);
                    int index = groupGrid.Rows.Add("Chưa tìm thấy nhóm trùng", "-", unmatchedItems.Count.ToString(), partList);
                    groupGrid.Rows[index].Tag = fakeNoMatchGroup;
                    ColorGroupRow(groupGrid.Rows[index], "NO_MATCH");
                }

                int totalCount = allGroups.Count + (unverifiedItems.Count > 0 ? 1 : 0) + (errorItems.Count > 0 ? 1 : 0) + (unmatchedItems.Count > 0 ? 1 : 0);
                groupCountBadge.Text = activeFilter == null
                    ? totalCount + " nhóm"
                    : groupGrid.Rows.Count + " / " + totalCount + " nhóm";

                if (groupGrid.Rows.Count > 0)
                    groupGrid.CurrentCell = groupGrid.Rows[0].Cells[0];
            }
            finally { selecting = false; }
            SelectGroup();
        }

        private static string FormatBuhinList(IEnumerable<SamePartItemResult> items)
        {
            if (items == null) return "";
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                string id = (item.BuhinNo ?? "").Trim();
                if (id.Length == 0) id = Path.GetFileName(item.PartPath ?? "");
                if (id.Length == 0) id = "?";
                if (!counts.ContainsKey(id)) counts[id] = 0;
                counts[id]++;
            }
            return string.Join(", ", counts.Select(kv => kv.Value > 1 ? kv.Key + " x" + kv.Value : kv.Key));
        }

        private void SelectGroup()
        {
            if (selecting) return;
            SamePartGroupResult group = SelectedGroup;
            selecting = true;
            try
            {
                partGrid.Rows.Clear();
                if (group == null)
                {
                    selectedGroupTitle.Text = string.IsNullOrEmpty(activeFilter)
                        ? "Không tìm thấy nhóm chi tiết đồng dạng."
                        : "Không có nhóm ở bộ lọc đã chọn.";
                    selectedGroupSubtext.Text = "Chọn bộ lọc khác trên thanh thống kê để xem danh sách.";
                }
                else
                {
                    string statusDesc = FormatStatusDescription(group.Status);
                    string prefix = group.Status == "UNVERIFIED" ? "Chưa xác minh: "
                        : (group.Status == "ERROR" ? "Lỗi: "
                        : (group.Status == "NO_MATCH" ? ""
                        : (group.GroupNumber > 0 ? "Nhóm " + group.GroupNumber + "  ·  " : "")));
                    string titleText = group.Status == "NO_MATCH"
                        ? "Chưa tìm thấy nhóm trùng (" + group.Items.Count + " chi tiết)"
                        : prefix + FormatStatusLabel(group.Status) + " (" + group.Items.Count + " chi tiết)";
                    selectedGroupTitle.Text = titleText;
                    selectedGroupSubtext.Text = !string.IsNullOrWhiteSpace(group.Note)
                        ? group.Note
                        : statusDesc;

                    foreach (SamePartItemResult item in group.Items)
                    {
                        int index = partGrid.Rows.Add(
                            item.BuhinNo ?? "",
                            item.ComponentName ?? item.BomFileName ?? "",
                            item.Material ?? "",
                            item.Thickness ?? "",
                            item.Configuration ?? "",
                            item.FlatConfiguration ?? "",
                            item.PartPath ?? "");
                        partGrid.Rows[index].Tag = item;
                        ColorPartRow(partGrid.Rows[index]);
                    }

                    if (partGrid.Rows.Count > 0)
                        partGrid.CurrentCell = partGrid.Rows[0].Cells[0];
                }
                detailBanner.Invalidate();
            }
            finally { selecting = false; }

            UpdateDifferenceCallout();
            UpdateActionButtons();
        }

        private void UpdateDifferenceCallout()
        {
            SamePartGroupResult group = SelectedGroup;
            if (group == null)
            {
                calloutHeader.Text = "THÔNG TIN CHI TIẾT";
                calloutHeader.ForeColor = TaiTheme.TextMuted;
                calloutBody.Text = "Chọn một nhóm để xem phân tích so sánh chi tiết giữa các Part.";
                calloutFooter.Text = "";
                calloutCard.Invalidate();
                return;
            }

            string status = group.Status ?? "";
            if (status == "SAME FULL")
            {
                calloutHeader.Text = "✔ TRÙNG KHỚP HOÀN TOÀN  —  [ Cùng hình học 3D, vật liệu, bề dày & phôi trải ]";
                calloutHeader.ForeColor = TaiTheme.Success;
                string diff = ExcelSamePartExporter.FormatDifferenceValues(group);
                calloutBody.Text = string.IsNullOrWhiteSpace(diff)
                    ? ExcelSamePartExporter.FormatNoteForExcel(group) + "\r\nCác chi tiết trong nhóm này hoàn toàn giống nhau, có thể tái sử dụng chung phôi dập và chương trình CNC."
                    : ExcelSamePartExporter.FormatNoteForExcel(group) + "\r\n" + diff;
                calloutFooter.Text = "Bấm nút \"Mở Part 3D\" hoặc Double-click vào dòng để phóng tới mô hình trong SOLIDWORKS.";
            }
            else if (status == "SAME TOLERANCE")
            {
                calloutHeader.Text = "✔ TRÙNG KHỚP TRONG DUNG SAI  —  [ Đã xác minh B-Rep đạt dung sai cho phép ]";
                calloutHeader.ForeColor = Color.FromArgb(13, 148, 136);
                string diff = ExcelSamePartExporter.FormatDifferenceValues(group);
                calloutBody.Text = string.IsNullOrWhiteSpace(diff)
                    ? ExcelSamePartExporter.FormatNoteForExcel(group) + "\r\nCác chi tiết trong nhóm này có hình học sai lệch nằm trong dung sai cho phép (" + (group.GeometryToleranceMm > 0 ? group.GeometryToleranceMm + " mm" : "dung sai chuẩn") + ")."
                    : ExcelSamePartExporter.FormatNoteForExcel(group) + "\r\n" + diff;
                calloutFooter.Text = "Bấm nút \"Mở Part 3D\" hoặc Double-click vào dòng để phóng tới mô hình trong SOLIDWORKS.";
            }
            else if (status == "SAME GEOMETRY")
            {
                calloutHeader.Text = "● TRÙNG HÌNH HỌC 3D  —  [ Khác vật liệu hoặc bề dày tôn ]";
                calloutHeader.ForeColor = TaiTheme.Accent;
                string diff = ExcelSamePartExporter.FormatDifferenceValues(group);
                calloutBody.Text = string.IsNullOrWhiteSpace(diff)
                    ? ExcelSamePartExporter.FormatNoteForExcel(group)
                    : ExcelSamePartExporter.FormatNoteForExcel(group) + "\r\n" + diff;
                calloutFooter.Text = "Bấm nút \"Mở Part 3D\" hoặc Double-click vào dòng để phóng tới mô hình trong SOLIDWORKS.";
            }
            else if (status == "SAME FLAT")
            {
                calloutHeader.Text = "▲ TRÙNG PHÔI TRẢI FLAT-PATTERN  —  [ Trải ra giống nhau nhưng uốn/chấn khác ]";
                calloutHeader.ForeColor = TaiTheme.Warning;
                string diff = ExcelSamePartExporter.FormatDifferenceValues(group);
                calloutBody.Text = string.IsNullOrWhiteSpace(diff)
                    ? ExcelSamePartExporter.FormatNoteForExcel(group)
                    : ExcelSamePartExporter.FormatNoteForExcel(group) + "\r\n" + diff;
                calloutFooter.Text = "Bấm nút \"Mở Part 3D\" hoặc Double-click vào dòng để phóng tới mô hình trong SOLIDWORKS.";
            }
            else if (status == "CHECK MIRROR")
            {
                calloutHeader.Text = "◆ CHI TIẾT ĐỐI XỨNG (MIRROR)  —  [ Cần kiểm tra đối chiếu chiều uốn trái/phải ]";
                calloutHeader.ForeColor = Color.FromArgb(124, 58, 237);
                string diff = ExcelSamePartExporter.FormatDifferenceValues(group);
                calloutBody.Text = string.IsNullOrWhiteSpace(diff)
                    ? ExcelSamePartExporter.FormatNoteForExcel(group)
                    : ExcelSamePartExporter.FormatNoteForExcel(group) + "\r\n" + diff;
                calloutFooter.Text = "Bấm nút \"Mở Part 3D\" hoặc Double-click vào dòng để phóng tới mô hình trong SOLIDWORKS.";
            }
            else if (status == "UNVERIFIED")
            {
                calloutHeader.Text = "⚠ CHI TIẾT CHƯA ĐỦ DỮ LIỆU HÌNH HỌC ĐỂ XÁC MINH (UNVERIFIED)";
                calloutHeader.ForeColor = Color.FromArgb(217, 119, 6);
                calloutBody.Text = (group.Note ?? "Chưa đủ dữ liệu hình học.")
                    + "\r\nCác chi tiết này có biên dạng cong phức tạp hoặc chưa đủ anchor độc lập phép quay để kiểm chứng tự động. Để đảm bảo an toàn, hệ thống từ chối kết luận giống nhau; điều này KHÔNG đồng nghĩa chi tiết bị lỗi hay khác nhau.";
                calloutFooter.Text = "Bấm 'Mở Part 3D' hoặc Double-click vào dòng để kiểm tra thủ công trong SOLIDWORKS.";
            }
            else if (status == "NO_MATCH")
            {
                calloutHeader.Text = "○ CHI TIẾT CHƯA TÌM THẤY NHÓM TRÙNG (NO MATCH)";
                calloutHeader.ForeColor = Color.FromArgb(100, 116, 139);
                calloutBody.Text = (group.Note ?? "Chưa tìm thấy nhóm trùng trong lần quét này; chưa chứng minh khác nhau từng cặp.")
                    + "\r\nCác chi tiết này không tìm thấy đối tượng trùng trong phạm vi các chi tiết được chọn. Lưu ý: Điều này chỉ xác nhận chưa tìm thấy nhóm trùng trong đợt quét hiện tại, chưa chứng minh khác nhau từng cặp đối với mọi chi tiết.";
                calloutFooter.Text = "Bấm 'Mở Part 3D' hoặc Double-click vào dòng để xem chi tiết trong SOLIDWORKS.";
            }
            else
            {
                calloutHeader.Text = "■ CHI TIẾT GẶP LỖI ĐỌC / XỬ LÝ (ERROR)";
                calloutHeader.ForeColor = TaiTheme.Danger;
                calloutBody.Text = group.Note ?? "Chi tiết gặp lỗi trong quá trình đọc thông số hoặc gọi SOLIDWORKS API.";
                calloutFooter.Text = "Kiểm tra lại đường dẫn file hoặc quyền truy cập của chi tiết.";
            }

            calloutCard.Invalidate();
        }

        private void UpdateActionButtons()
        {
            SamePartItemResult item = SelectedItem;
            bool hasItem = item != null && !string.IsNullOrWhiteSpace(item.PartPath);
            btnOpenPart.Enabled = hasItem && File.Exists(item.PartPath);
            btnOpenAssembly.Enabled = hasItem && item.AssemblyPaths != null && item.AssemblyPaths.Any(p => File.Exists(p));
            btnOpenFolder.Enabled = hasItem;
        }

        private void NavigateSelected(string targetType)
        {
            SamePartItemResult item = SelectedItem;
            if (item == null || navigate == null) return;
            try
            {
                navigate(item, targetType);
            }
            catch (Exception ex)
            {
                status.ForeColor = TaiTheme.Warning;
                status.Text = "Không mở được đối tượng: " + ex.Message;
            }
        }

        private void btnExportExcel_Click(object sender, EventArgs e)
        {
            if (reportSnapshot == null) return;
            btnExportExcel.Enabled = false;
            try
            {
                string path;
                if (SamePartReportV2Excel.SaveWithDialog(reportSnapshot, this, excelOutputDirectory, out path))
                {
                    status.ForeColor = TaiTheme.Success;
                    status.Text = "Đã xuất Excel thành công: " + Path.GetFileName(path);
                    MessageBox.Show(this, "Đã xuất Excel thành công:\n" + path,
                        "CHECK SAME PART", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            finally
            {
                btnExportExcel.Enabled = true;
            }
        }

        private static string FormatStatusLabel(string status)
        {
            switch (status)
            {
                case "SAME FULL": return "Trùng 100%";
                case "SAME TOLERANCE": return "Dung sai";
                case "SAME GEOMETRY": return "Trùng 3D";
                case "SAME FLAT": return "Trùng Phôi";
                case "CHECK MIRROR": return "Mirror";
                case "UNVERIFIED": return "Chưa xác minh";
                case "ERROR": return "Lỗi";
                case "NO_MATCH": return "Chưa tìm thấy nhóm trùng";
                default: return status ?? "";
            }
        }

        private static string FormatStatusDescription(string status)
        {
            switch (status)
            {
                case "SAME FULL": return "Các chi tiết hoàn toàn giống nhau về cả hình học 3D, vật liệu, bề dày và phôi trải.";
                case "SAME TOLERANCE": return "Các chi tiết giống nhau trong phạm vi dung sai cho phép.";
                case "SAME GEOMETRY": return "Biên dạng 3D giống nhau nhưng vật liệu hoặc bề dày tôn khác nhau.";
                case "SAME FLAT": return "Biên dạng trải phẳng giống nhau nhưng trạng thái uốn/chấn khác nhau.";
                case "CHECK MIRROR": return "Biên dạng tương đương đối xứng Mirror qua mặt phẳng.";
                case "UNVERIFIED": return "Chi tiết chưa đủ dữ liệu hình học hoặc biên dạng cong phức tạp để xác minh tự động.";
                case "ERROR": return "Có lỗi phát sinh trong quá trình đọc chi tiết.";
                case "NO_MATCH": return "Chưa tìm thấy nhóm trùng trong lần quét này; chưa chứng minh khác nhau từng cặp.";
                default: return "";
            }
        }

        private static void ColorGroupRow(DataGridViewRow row, string status)
        {
            row.DefaultCellStyle.BackColor = TaiTheme.Surface;
            row.DefaultCellStyle.SelectionBackColor = TaiTheme.AccentSoft;
            row.DefaultCellStyle.SelectionForeColor = TaiTheme.Text;
        }

        private static void ColorPartRow(DataGridViewRow row)
        {
            row.DefaultCellStyle.BackColor = TaiTheme.Surface;
            row.DefaultCellStyle.SelectionBackColor = TaiTheme.AccentSoft;
            row.DefaultCellStyle.SelectionForeColor = TaiTheme.Text;
        }

        private void PaintBrandHeader(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            TaiTheme.Prepare(g);

            Rectangle iconRect = new Rectangle(2, 6, 36, 36);
            TaiTheme.FillRound(g, iconRect, 8, TaiTheme.Accent, TaiTheme.Accent);

            // Draw overlapping squares icon (symbolizing Same / Duplication)
            using (var pen = new Pen(Color.White, 2F))
            {
                pen.LineJoin = LineJoin.Round;
                Rectangle sq1 = new Rectangle(iconRect.X + 7, iconRect.Y + 7, 14, 14);
                Rectangle sq2 = new Rectangle(iconRect.X + 13, iconRect.Y + 13, 14, 14);

                g.DrawRectangle(pen, sq2);
                using (var fillBrush = new SolidBrush(TaiTheme.Accent))
                    g.FillRectangle(fillBrush, sq1);
                g.DrawRectangle(pen, sq1);
            }

            int textLeft = iconRect.Right + 12;
            using (var titleFont = new Font("Segoe UI", 12F, FontStyle.Bold))
            using (var subFont = new Font("Segoe UI", 9F, FontStyle.Regular))
            {
                TextRenderer.DrawText(g, "KẾT QUẢ CHECK SAME PART", titleFont, new Point(textLeft, 6), TaiTheme.Text,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, "So khớp hình học 3D, phôi trải và đối xứng", subFont, new Point(textLeft, 28), TaiTheme.TextMuted,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
        }

        private void PaintLeftHeader(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            TaiTheme.Prepare(g);

            int pillX = 14;
            int pillH = 18;
            int pillY = (44 - pillH) / 2;
            using (var brush = new SolidBrush(Color.FromArgb(56, 189, 248))) // Cyan-400
                g.FillRectangle(brush, pillX, pillY, 3.5F, pillH);
        }

        private void PaintDetailBanner(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            TaiTheme.Prepare(g);

            int pillX = 14;
            int pillH = 26;
            int pillY = (detailBanner.Height - pillH) / 2;
            using (var brush = new SolidBrush(Color.FromArgb(56, 189, 248)))
                g.FillRectangle(brush, pillX, pillY, 3.5F, pillH);

            SamePartGroupResult group = SelectedGroup;
            if (group == null) return;

            Rectangle badgeRect = new Rectangle(detailBanner.Width - 180, (detailBanner.Height - 28) / 2, 168, 28);
            DrawStatusPill(g, badgeRect, group.Status);
        }

        private void PaintCalloutCard(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            TaiTheme.Prepare(g);

            string status = SelectedGroup?.Status ?? "SAME FULL";

            Color fill = TaiTheme.SuccessSoft;
            Color border = TaiTheme.SuccessBorder;
            Color stripe = TaiTheme.Success;

            if (status == "SAME TOLERANCE")
            {
                fill = Color.FromArgb(204, 251, 241);
                border = Color.FromArgb(153, 246, 228);
                stripe = Color.FromArgb(13, 148, 136);
            }
            else if (status == "SAME GEOMETRY")
            {
                fill = TaiTheme.AccentSoft;
                border = TaiTheme.AccentBorder;
                stripe = TaiTheme.Accent;
            }
            else if (status == "SAME FLAT")
            {
                fill = TaiTheme.WarningSoft;
                border = TaiTheme.WarningBorder;
                stripe = TaiTheme.Warning;
            }
            else if (status == "CHECK MIRROR")
            {
                fill = Color.FromArgb(245, 243, 255);
                border = Color.FromArgb(221, 214, 254);
                stripe = Color.FromArgb(124, 58, 237);
            }
            else if (status == "UNVERIFIED")
            {
                fill = Color.FromArgb(254, 243, 199);
                border = Color.FromArgb(253, 230, 138);
                stripe = Color.FromArgb(217, 119, 6);
            }
            else if (status == "NO_MATCH")
            {
                fill = Color.FromArgb(241, 245, 249);
                border = Color.FromArgb(203, 213, 225);
                stripe = Color.FromArgb(100, 116, 139);
            }
            else if (status == "ERROR")
            {
                fill = TaiTheme.DangerSoft;
                border = TaiTheme.DangerBorder;
                stripe = TaiTheme.Danger;
            }

            e.Graphics.Clear(TaiTheme.Surface);
            Rectangle rect = new Rectangle(0, 0, calloutCard.Width - 1, calloutCard.Height - 1);
            TaiTheme.FillRound(g, rect, 8, fill, border);

            using (var stripeBrush = new SolidBrush(stripe))
            {
                g.FillRectangle(stripeBrush, new Rectangle(1, 6, 5, Math.Max(4, calloutCard.Height - 12)));
            }
        }

        private static void PaintGroupCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var grid = sender as DataGridView;
            if (grid == null) return;

            bool isSelected = (e.State & DataGridViewElementStates.Selected) == DataGridViewElementStates.Selected;

            if (e.ColumnIndex == 0)
            {
                e.PaintBackground(e.CellBounds, true);

                if (isSelected)
                {
                    using (var accentBrush = new SolidBrush(TaiTheme.Accent))
                        e.Graphics.FillRectangle(accentBrush, e.CellBounds.X, e.CellBounds.Y, 3.5F, e.CellBounds.Height);
                }

                string cellStatus = "SAME FULL";
                object tag = grid.Rows[e.RowIndex].Tag;
                if (tag is SamePartGroupResult groupTag) cellStatus = groupTag.Status;

                int pillW = Math.Min(165, e.CellBounds.Width - 10);
                int pillH = 24;
                Rectangle pillRect = new Rectangle(
                    e.CellBounds.X + (e.CellBounds.Width - pillW) / 2,
                    e.CellBounds.Y + (e.CellBounds.Height - pillH) / 2,
                    pillW,
                    pillH);

                DrawStatusPill(e.Graphics, pillRect, cellStatus);
                e.Handled = true;
            }
        }

        private static void DrawStatusPill(Graphics g, Rectangle bounds, string status)
        {
            TaiTheme.Prepare(g);

            Color fill = TaiTheme.SuccessSoft;
            Color border = TaiTheme.SuccessBorder;
            Color fg = TaiTheme.Success;

            if (status == "SAME TOLERANCE")
            {
                fill = Color.FromArgb(204, 251, 241);
                border = Color.FromArgb(153, 246, 228);
                fg = Color.FromArgb(13, 148, 136);
            }
            else if (status == "SAME GEOMETRY")
            {
                fill = TaiTheme.AccentSoft;
                border = TaiTheme.AccentBorder;
                fg = TaiTheme.Accent;
            }
            else if (status == "SAME FLAT")
            {
                fill = TaiTheme.WarningSoft;
                border = TaiTheme.WarningBorder;
                fg = TaiTheme.Warning;
            }
            else if (status == "CHECK MIRROR")
            {
                fill = Color.FromArgb(245, 243, 255);
                border = Color.FromArgb(221, 214, 254);
                fg = Color.FromArgb(124, 58, 237);
            }
            else if (status == "UNVERIFIED")
            {
                fill = Color.FromArgb(254, 243, 199);
                border = Color.FromArgb(253, 230, 138);
                fg = Color.FromArgb(217, 119, 6);
            }
            else if (status == "NO_MATCH")
            {
                fill = Color.FromArgb(241, 245, 249);
                border = Color.FromArgb(203, 213, 225);
                fg = Color.FromArgb(100, 116, 139);
            }
            else if (status == "ERROR")
            {
                fill = TaiTheme.DangerSoft;
                border = TaiTheme.DangerBorder;
                fg = TaiTheme.Danger;
            }

            TaiTheme.FillRound(g, bounds, bounds.Height / 2, fill, border);

            int dotSize = 7;
            int dotX = bounds.X + 8;
            int dotY = bounds.Y + (bounds.Height - dotSize) / 2;
            using (var brush = new SolidBrush(fg))
                g.FillEllipse(brush, dotX, dotY, dotSize, dotSize);

            Rectangle textRect = new Rectangle(dotX + dotSize + 4, bounds.Y, bounds.Width - (dotSize + 14), bounds.Height);
            using (var font = new Font("Segoe UI", 8.5F, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, FormatStatusLabel(status), font, textRect, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
        }

        private static Panel CreateCardPanel(Padding padding)
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = TaiTheme.Surface,
                Padding = padding,
                Margin = Padding.Empty
            };
            UIHelper.EnableDoubleBuffer(card);
            card.Paint += (s, e) =>
            {
                e.Graphics.Clear(TaiTheme.Page);
                TaiTheme.Prepare(e.Graphics);
                Rectangle frame = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                TaiTheme.FillRound(e.Graphics, frame, 8, TaiTheme.Surface, TaiTheme.Border);
            };
            card.Resize += (s, e) => card.Invalidate();
            return card;
        }

        private static DataGridView Grid(string name)
        {
            var grid = new DataGridView
            {
                Name = name,
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                BorderStyle = BorderStyle.None,
                BackgroundColor = TaiTheme.Surface,
                GridColor = TaiTheme.Divider,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 40,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                RowTemplate = { Height = 38 },
                Margin = Padding.Empty,
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(245, 247, 250),
                    ForeColor = Color.FromArgb(30, 41, 59),
                    Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                    SelectionBackColor = Color.FromArgb(245, 247, 250),
                    Padding = new Padding(8, 0, 8, 0)
                },
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    ForeColor = TaiTheme.Text,
                    BackColor = TaiTheme.Surface,
                    SelectionBackColor = TaiTheme.AccentSoft,
                    SelectionForeColor = TaiTheme.Text,
                    Padding = new Padding(8, 2, 8, 2),
                    Font = new Font("Segoe UI", 10F, FontStyle.Regular)
                }
            };
            UIHelper.EnableDoubleBuffer(grid);
            return grid;
        }

        private static Label TextLabel(string text, bool bold)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = bold ? TaiTheme.Text : TaiTheme.TextMuted,
                Font = new Font("Segoe UI", 10F, bold ? FontStyle.Bold : FontStyle.Regular),
                Margin = Padding.Empty,
                UseMnemonic = false
            };
        }

        private static ModernButton Button(string text, bool primary, int width)
        {
            return new ModernButton
            {
                Text = text,
                Width = width,
                Height = 36,
                Kind = primary ? ModernButtonKind.Primary : ModernButtonKind.Standard,
                BorderRadius = 6,
                NormalColor = primary ? TaiTheme.Accent : TaiTheme.Surface,
                HoverColor = primary ? TaiTheme.AccentHover : TaiTheme.AccentSoft,
                PressColor = primary ? TaiTheme.AccentPressed : TaiTheme.SurfacePressed,
                BorderColor = primary ? TaiTheme.Accent : TaiTheme.BorderStrong,
                ForeColor = primary ? Color.White : TaiTheme.Text,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Margin = new Padding(0, 0, 8, 0)
            };
        }

        // Interactive Filter Pill Button
        private sealed class FilterPillButton : Control
        {
            private readonly string label;
            private readonly int count;
            private readonly Color hue;
            private readonly Color softFill;
            private readonly Color softBorder;
            private bool hovered;
            private bool isActive;

            public bool IsActive
            {
                get { return isActive; }
                set { if (isActive != value) { isActive = value; Invalidate(); } }
            }

            public FilterPillButton(string label, int count, string filterKey, Color hue, Color softFill, Color softBorder)
            {
                this.label = label;
                this.count = count;
                this.hue = hue;
                this.softFill = softFill;
                this.softBorder = softBorder;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
                Cursor = Cursors.Hand;
                Height = 34;
                Width = MeasureWidth(label, count);
                Margin = new Padding(6, 0, 0, 0);
            }

            private static int MeasureWidth(string text, int num)
            {
                using (var f1 = new Font("Segoe UI", 9.5F, FontStyle.Regular))
                using (var f2 = new Font("Segoe UI", 10.5F, FontStyle.Bold))
                {
                    int w1 = TextRenderer.MeasureText(text, f1).Width;
                    int w2 = TextRenderer.MeasureText(num.ToString(), f2).Width;
                    return Math.Max(88, w1 + w2 + 36);
                }
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                hovered = true;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                hovered = false;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(TaiTheme.Surface);
                TaiTheme.Prepare(g);

                Rectangle rect = new Rectangle(1, 1, Width - 3, Height - 3);
                Color fill = isActive ? softFill : hovered ? TaiTheme.Page : Color.FromArgb(249, 250, 252);
                Color border = isActive ? hue : hovered ? softBorder : TaiTheme.Border;

                TaiTheme.FillRound(g, rect, 16, fill, border);
                if (isActive)
                {
                    Rectangle inner = new Rectangle(2, 2, Width - 5, Height - 5);
                    TaiTheme.FillRound(g, inner, 15, Color.Transparent, Color.FromArgb(90, hue));
                }

                int dotSize = 8;
                int dotX = 13;
                int dotY = (Height - dotSize) / 2;
                using (var dotBrush = new SolidBrush(hue))
                    g.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);

                using (var lblFont = new Font("Segoe UI", 9.5F, isActive ? FontStyle.Bold : FontStyle.Regular))
                using (var cntFont = new Font("Segoe UI", 10.5F, FontStyle.Bold))
                {
                    Color lblColor = isActive ? hue : TaiTheme.TextMuted;
                    Color cntColor = isActive ? hue : (count > 0 ? hue : TaiTheme.Text);

                    Size lblSize = TextRenderer.MeasureText(g, label, lblFont, Size.Empty, TextFormatFlags.NoPadding);
                    int textY = (Height - lblSize.Height) / 2;
                    TextRenderer.DrawText(g, label, lblFont, new Point(dotX + dotSize + 7, textY), lblColor,
                        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

                    string cntText = count.ToString();
                    Size cntSize = TextRenderer.MeasureText(g, cntText, cntFont, Size.Empty, TextFormatFlags.NoPadding);
                    int cntY = (Height - cntSize.Height) / 2;
                    TextRenderer.DrawText(g, cntText, cntFont, new Point(Width - cntSize.Width - 13, cntY), cntColor,
                        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }
            }
        }
    }
}
