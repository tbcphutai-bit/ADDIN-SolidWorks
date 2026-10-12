using System;
using System.Drawing;
using System.Windows.Forms;
using ADDIN.Commands;
using ADDIN.UI;

namespace ADDIN
{
    // Layout of the two pages built from command tiles: Drawing > Drawing BOM
    // and Model > Macro. Colours come from TaiTheme; what a command is called
    // and which icon it carries is set once in BomTaskPaneTheme.
    public partial class BomTaskPaneControl
    {
        private const string StatusConnected = "Đã kết nối SOLIDWORKS", StatusWaiting = "Đang chờ kết nối SOLIDWORKS…";
        private bool drawingCommandLayoutInProgress, modelMacroLayoutInProgress;
        private Button btnToggleCommands;
        private bool bomCommandsCollapsed;
        private ModernCard cardMacroCommands, cardInspectionMonitor;
        private ModernButton btnClearInspection;
        private RichTextBox modelInspectionReport;
        private Control disabledTileHintOwner, disabledTileHintTarget;

        private int CommandPixels(int value)
        {
            return Math.Max(1, (int)Math.Ceiling(value * Math.Max(96, DeviceDpi) / 96.0));
        }

        private static void SetCommandBounds(Control control, int x, int y, int width, int height)
        {
            if (control == null) return;
            control.Dock = DockStyle.None;
            control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            if (control is Button) control.AutoSize = false;
            Rectangle bounds = new Rectangle(x, y, Math.Max(1, width), Math.Max(1, height));
            if (control.Bounds != bounds) control.Bounds = bounds;
        }

        private static void MoveControlToCard(Control control, Control card)
        {
            if (control == null || card == null || control.Parent == card) return;
            control.Parent?.Controls.Remove(control);
            card.Controls.Add(control);
        }

        // Tiles in two columns under the card title, all the same width: a last
        // tile without a partner stays in the left column. Returns the height
        // the card needs.
        private int LayoutTiles(Button[] tiles, int cardWidth, int tileHeight)
        {
            int pad = CommandPixels(TaiTheme.CardPadding), gap = CommandPixels(6), top = CommandPixels(TaiTheme.CardHeader);
            int inner = Math.Max(1, cardWidth - pad * 2);
            int columns = inner >= CommandPixels(360) ? 2 : 1;
            int width = (inner - gap * (columns - 1)) / columns;
            int index = 0;
            foreach (Button tile in tiles)
            {
                if (tile == null) continue;
                int column = index % columns, row = index / columns;
                int left = pad + column * (width + gap);
                SetCommandBounds(tile, left, top + row * (tileHeight + gap),
                    column == columns - 1 ? pad + inner - left : width, tileHeight);
                index++;
            }
            int rows = (index + columns - 1) / columns;
            return top + rows * tileHeight + Math.Max(0, rows - 1) * gap + pad;
        }

        // A disabled button gets no mouse messages, so its card explains why it is off.
        private void WatchDisabledTiles(Control card)
        {
            card.MouseMove += (sender, e) =>
            {
                if (bomCommandToolTip == null) return;
                Control target = null;
                foreach (Control child in card.Controls)
                    if (child is Button && child.Visible && !child.Enabled && child.Bounds.Contains(e.Location)) { target = child; break; }
                if (ReferenceEquals(target, disabledTileHintTarget)) return;
                HideDisabledTileHint();
                if (target == null) return;
                string text = GetBomCommandToolTipText(target);
                if (string.IsNullOrEmpty(text)) text = bomCommandToolTip.GetToolTip(target);
                if (string.IsNullOrEmpty(text)) return;
                disabledTileHintOwner = card; disabledTileHintTarget = target;
                bomCommandToolTip.Show(text, card, target.Left, target.Bottom + 2, 8000);
            };
            card.MouseLeave += (sender, e) => HideDisabledTileHint();
        }

        private void HideDisabledTileHint()
        {
            if (disabledTileHintOwner != null && bomCommandToolTip != null && !disabledTileHintOwner.IsDisposed)
                bomCommandToolTip.Hide(disabledTileHintOwner);
            disabledTileHintOwner = disabledTileHintTarget = null;
        }

        private void EnsureDrawingBomCards()
        {
            if (cardSheetMetal == null)
            {
                cardSheetMetal = new ModernCard { Name = "cardSheetMetal" };
                tabDrawingBom.Controls.Add(cardSheetMetal);
            }
            if (cardAssemblyDrawing == null)
            {
                cardAssemblyDrawing = new ModernCard { Name = "cardAssemblyDrawing" };
                tabDrawingBom.Controls.Add(cardAssemblyDrawing);
            }
            if (lblBomCount == null)
            {
                lblBomCount = new Label { Name = "lblBomCount", AutoSize = false, TextAlign = ContentAlignment.MiddleRight };
                tabDrawingBom.Controls.Add(lblBomCount);
            }
            foreach (Button tile in new Button[] { btnCheckDfTk, btnCheckAll, btnCheckRound, btnCheckSamePart, btnCheckDxf, btnStandaloneCurveMacro, btnExportBomDxf })
                MoveControlToCard(tile, cardSheetMetal);
            foreach (Button tile in new Button[] { button2, btnOpenAssem, btnCheckBalloon, btnCheckDrawingBom })
                MoveControlToCard(tile, cardAssemblyDrawing);
            if (btnToggleCommands != null) return;

            cardSheetMetal.Text = "Kiểm tra chi tiết";
            cardAssemblyDrawing.Text = "Cụm lắp ráp & bản vẽ";
            WatchDisabledTiles(cardSheetMetal);
            WatchDisabledTiles(cardAssemblyDrawing);

            btnToggleCommands = new ModernButton { Name = "btnToggleCommands", Kind = ModernButtonKind.Subtle, Icon = TaiIcon.ChevronUp, Text = "Thu gọn", TabStop = false };
            btnToggleCommands.Click += (s, e) =>
            {
                bomCommandsCollapsed = !bomCommandsCollapsed;
                btnToggleCommands.Text = bomCommandsCollapsed ? "Hiện lệnh" : "Thu gọn";
                ((ModernButton)btnToggleCommands).Icon = bomCommandsCollapsed ? TaiIcon.ChevronDown : TaiIcon.ChevronUp;
                LayoutDrawingBomCommands();
            };
            tabDrawingBom.Controls.Add(btnToggleCommands);

            if (lblTitle != null) lblTitle.Visible = false;
            if (lblBomBadge != null) lblBomBadge.Visible = false;
            lblStatus.AutoSize = false;
            lblStatus.AutoEllipsis = true;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            chkSelectAll.Text = "Chọn tất cả";
            GridStyler.SetEmptyState(dgvModelBom, "Chưa có BOM", "Chọn bảng BOM trong bản vẽ rồi bấm Cập nhật.");
            if (btnCheckDxf != null) btnCheckDxf.Visible = true;
        }

        // Each card says which BOM its commands need, and whether that BOM is loaded.
        private void UpdateBomCardBadges()
        {
            BomCommandContext context = actions == null || dgvModelBom == null || dgvModelBom.Rows.Count == 0
                ? BomCommandContext.None : actions.LoadedBomContext;
            cardSheetMetal.BadgeText = context == BomCommandContext.Detail ? "BOM chi tiết đã nạp" : "Cần BOM chi tiết";
            cardSheetMetal.BadgeTone = context == BomCommandContext.Detail ? ModernButtonTone.Success : ModernButtonTone.Neutral;
            cardAssemblyDrawing.BadgeText = context == BomCommandContext.Unit ? "BOM UNIT đã nạp" : "Cần BOM UNIT";
            cardAssemblyDrawing.BadgeTone = context == BomCommandContext.Unit ? ModernButtonTone.Success : ModernButtonTone.Neutral;
        }

        private void LayoutDrawingBomCommands()
        {
            if (tabDrawingBom == null || dgvModelBom == null || drawingCommandLayoutInProgress) return;
            if (tabDrawingBom.ClientSize.Width <= 0 || tabDrawingBom.ClientSize.Height <= 0) return;
            drawingCommandLayoutInProgress = true;
            tabDrawingBom.SuspendLayout();
            try
            {
                Point previousScroll = tabDrawingBom.AutoScrollPosition;
                int viewportWidth = tabDrawingBom.ClientSize.Width +
                    (tabDrawingBom.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0);
                int viewportHeight = tabDrawingBom.ClientSize.Height;
                tabDrawingBom.AutoScroll = false;
                tabDrawingBom.AutoScrollMinSize = Size.Empty;
                tabDrawingBom.AutoScrollPosition = Point.Empty;
                int requiredHeight = LayoutDrawingBomViewport(viewportWidth, viewportHeight);
                bool scroll = requiredHeight > viewportHeight;
                if (scroll) requiredHeight = LayoutDrawingBomViewport(
                    Math.Max(1, viewportWidth - SystemInformation.VerticalScrollBarWidth), viewportHeight);
                tabDrawingBom.AutoScroll = scroll;
                tabDrawingBom.AutoScrollMinSize = scroll ? new Size(0, requiredHeight) : Size.Empty;
                if (scroll) tabDrawingBom.AutoScrollPosition = new Point(0, Math.Max(0, -previousScroll.Y));
                actions?.AutoFitBomGrid();
            }
            finally
            {
                tabDrawingBom.ResumeLayout(true);
                drawingCommandLayoutInProgress = false;
            }
        }

        private int LayoutDrawingBomViewport(int viewportWidth, int viewportHeight)
        {
            EnsureDrawingBomCards();
            UpdateBomCardBadges();

            int margin = CommandPixels(8), gap = CommandPixels(8);
            int width = Math.Max(1, viewportWidth - margin * 2);
            bool exporting = IsBomDxfExportPaneOpen;

            // 1. Status line and the switch that folds the command cards away.
            if (string.IsNullOrWhiteSpace(lblStatus.Text) || lblStatus.Text == StatusConnected || lblStatus.Text == StatusWaiting ||
                lblStatus.Text.StartsWith("●", StringComparison.Ordinal) || lblStatus.Text.StartsWith("○", StringComparison.Ordinal))
                lblStatus.Text = swApp != null ? StatusConnected : StatusWaiting;
            int row = CommandPixels(24), toggleWidth = exporting ? 0 : CommandPixels(92);
            int top = CommandPixels(6);
            btnToggleCommands.Visible = !exporting;
            SetCommandBounds(btnToggleCommands, viewportWidth - margin - toggleWidth, top, toggleWidth, row);
            SetCommandBounds(lblStatus, margin + CommandPixels(2), top, Math.Max(1, width - toggleWidth - CommandPixels(6)), row);
            int next = top + row + CommandPixels(6);

            // 2. Command cards. Two-line tiles when the pane is tall enough to
            //    leave the BOM grid its room, one-line tiles otherwise.
            bool showCommands = !bomCommandsCollapsed && !exporting;
            cardSheetMetal.Visible = cardAssemblyDrawing.Visible = showCommands;
            if (showCommands)
            {
                int tileHeight = CommandPixels(viewportHeight >= CommandPixels(700) ? 46 : 30);
                int height = LayoutTiles(new Button[] { btnCheckDfTk, btnCheckAll, btnCheckRound, btnCheckSamePart,
                    btnCheckDxf, btnStandaloneCurveMacro, btnExportBomDxf }, width, tileHeight);
                SetCommandBounds(cardSheetMetal, margin, next, width, height);
                next += height + gap;
                height = LayoutTiles(new Button[] { button2, btnOpenAssem, btnCheckBalloon, btnCheckDrawingBom }, width, tileHeight);
                SetCommandBounds(cardAssemblyDrawing, margin, next, width, height);
                next += height + gap;
            }

            // 3. Select-all row, progress line, then the grid (or the DXF export pane).
            chkSelectAll.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            chkSelectAll.Visible = !exporting;
            chkSelectAll.SetBounds(margin + CommandPixels(2), next, CommandPixels(150), CommandPixels(22));
            int rowCount = dgvModelBom.Rows.Count;
            lblBomCount.Text = rowCount > 0 ? rowCount + " chi tiết" : "";
            lblBomCount.Visible = !exporting;
            SetCommandBounds(lblBomCount, viewportWidth - margin - CommandPixels(110), next, CommandPixels(108), CommandPixels(22));
            int progressTop = next + CommandPixels(24);
            if (progressCheck != null) SetCommandBounds(progressCheck, margin, progressTop, width, CommandPixels(3));
            int gridTop = exporting ? next : progressTop + CommandPixels(7);

            // 4. Footer: load, clear, stop.
            Button[] footer = { btnLoadBom, btnClearBom, button1 };
            int footerHeight = CommandPixels(32), footerGap = CommandPixels(6);
            int footerWidth = Math.Max(1, (width - footerGap * 2) / 3);
            int minimumContent = CommandPixels(exporting ? 600 : 120);
            int requiredHeight = gridTop + minimumContent + gap + footerHeight + margin;
            int footerTop = Math.Max(gridTop + minimumContent + gap, viewportHeight - margin - footerHeight);

            SetCommandBounds(dgvModelBom, margin, gridTop, width, footerTop - gridTop - gap);
            dgvModelBom.Visible = !exporting;
            if (exporting)
            {
                SetCommandBounds(bomDxfExportPane, margin, gridTop, width, footerTop - gridTop - gap);
                bomDxfExportPane.BringToFront();
            }
            for (int n = 0; n < footer.Length; n++)
            {
                int left = margin + n * (footerWidth + footerGap);
                SetCommandBounds(footer[n], left, footerTop, n == footer.Length - 1 ? margin + width - left : footerWidth, footerHeight);
            }
            return requiredHeight;
        }

        private void LayoutModelMacroCommands()
        {
            if (tabModelMacroPage == null || modelMacroLayoutInProgress || tabModelMacroPage.ClientSize.Width <= 0) return;
            modelMacroLayoutInProgress = true;
            tabModelMacroPage.SuspendLayout();
            try
            {
                Point previousScroll = tabModelMacroPage.AutoScrollPosition;
                int viewportWidth = tabModelMacroPage.ClientSize.Width +
                    (tabModelMacroPage.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0);
                int viewportHeight = tabModelMacroPage.ClientSize.Height;
                tabModelMacroPage.AutoScroll = false;
                tabModelMacroPage.AutoScrollMinSize = Size.Empty;
                tabModelMacroPage.AutoScrollPosition = Point.Empty;
                int requiredHeight = LayoutModelMacroViewport(viewportWidth, viewportHeight);
                bool scroll = requiredHeight > viewportHeight;
                if (scroll) requiredHeight = LayoutModelMacroViewport(
                    Math.Max(1, viewportWidth - SystemInformation.VerticalScrollBarWidth), viewportHeight);
                tabModelMacroPage.AutoScroll = scroll;
                tabModelMacroPage.AutoScrollMinSize = scroll ? new Size(0, requiredHeight) : Size.Empty;
                if (scroll) tabModelMacroPage.AutoScrollPosition = new Point(0, Math.Max(0, -previousScroll.Y));
            }
            finally
            {
                tabModelMacroPage.ResumeLayout(true);
                modelMacroLayoutInProgress = false;
            }
        }

        private const string InspectionPlaceholder = "Chưa có kết quả.\r\nChạy một lệnh ở trên, báo cáo sẽ hiện tại đây.";

        private void EnsureModelMacroControls()
        {
            if (tabModelMacroPage == null || cardMacroCommands != null) return;

            cardMacroCommands = new ModernCard { Name = "cardMacroCommands", Text = "Lệnh macro" };
            tabModelMacroPage.Controls.Add(cardMacroCommands);
            foreach (Button tile in new Button[] { btnCheckAssemblyHole, btnMirrorPart, btnEdgeToEqualSpline,
                btnRepair3DSpline, btnVCutPropertySetup, btnCheckDxfMacro })
                MoveControlToCard(tile, cardMacroCommands);
            WatchDisabledTiles(cardMacroCommands);

            cardInspectionMonitor = new ModernCard { Name = "cardInspectionMonitor", Text = "Kết quả kiểm tra" };
            tabModelMacroPage.Controls.Add(cardInspectionMonitor);
            btnClearInspection = new ModernButton { Name = "btnClearInspection", Kind = ModernButtonKind.Subtle, Icon = TaiIcon.Eraser, Text = "Xóa", TabStop = false };
            btnClearInspection.Click += (s, e) => ClearModelInspectionReport();
            cardInspectionMonitor.Controls.Add(btnClearInspection);
            modelInspectionReport = new RichTextBox
            {
                Name = "ModelInspectionReport", ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = TaiTheme.Surface,
                ForeColor = TaiTheme.Text, Font = TaiTheme.Body, WordWrap = true, ScrollBars = RichTextBoxScrollBars.Vertical,
                DetectUrls = false, Visible = false
            };
            cardInspectionMonitor.Controls.Add(modelInspectionReport);
            if (lblCheckAssemblyHoleResult != null)
            {
                lblCheckAssemblyHoleResult.BorderStyle = BorderStyle.None;
                MoveControlToCard(lblCheckAssemblyHoleResult, cardInspectionMonitor);
                lblCheckAssemblyHoleResult.Text = InspectionPlaceholder;
                lblCheckAssemblyHoleResult.TextAlign = ContentAlignment.MiddleCenter;
            }
            // The vCut table covers this page while it is open.
            if (vCutBendAssignmentControl != null) vCutBendAssignmentControl.BringToFront();
        }

        private void ShowModelInspectionReport(string text)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) { BeginInvoke((Action)(() => ShowModelInspectionReport(text))); return; }
            EnsureModelMacroControls();
            modelInspectionReport.Text = text ?? string.Empty;
            modelInspectionReport.Bounds = lblCheckAssemblyHoleResult.Bounds;
            lblCheckAssemblyHoleResult.Visible = false; modelInspectionReport.Visible = true;
            modelInspectionReport.BringToFront();
        }

        private void ClearModelInspectionReport()
        {
            if (modelInspectionReport != null) { modelInspectionReport.Clear(); modelInspectionReport.Visible = false; }
            if (lblCheckAssemblyHoleResult != null) lblCheckAssemblyHoleResult.Visible = true;
        }

        private int LayoutModelMacroViewport(int viewportWidth, int viewportHeight)
        {
            EnsureCheckDxfButtons();
            EnsureModelMacroControls();

            int margin = CommandPixels(8), gap = CommandPixels(8);
            int width = Math.Max(1, viewportWidth - margin * 2);
            int top = CommandPixels(8);

            int height = LayoutTiles(new Button[] { btnCheckAssemblyHole, btnMirrorPart, btnEdgeToEqualSpline,
                btnRepair3DSpline, btnVCutPropertySetup, btnCheckDxfMacro }, width, CommandPixels(44));
            SetCommandBounds(cardMacroCommands, margin, top, width, height);

            int monitorTop = top + height + gap;
            int monitorHeight = Math.Max(CommandPixels(160), viewportHeight - monitorTop - margin);
            SetCommandBounds(cardInspectionMonitor, margin, monitorTop, width, monitorHeight);
            int pad = CommandPixels(TaiTheme.CardPadding), header = CommandPixels(TaiTheme.CardHeader), clearWidth = CommandPixels(58);
            SetCommandBounds(btnClearInspection, width - pad - clearWidth + CommandPixels(4), CommandPixels(4), clearWidth, CommandPixels(24));
            Rectangle body = new Rectangle(pad, header, Math.Max(1, width - pad * 2), Math.Max(1, monitorHeight - header - pad));
            SetCommandBounds(lblCheckAssemblyHoleResult, body.X, body.Y, body.Width, body.Height);
            SetCommandBounds(modelInspectionReport, body.X, body.Y, body.Width, body.Height);
            return monitorTop + monitorHeight + margin;
        }
    }
}
