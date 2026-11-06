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
        private ModernButton btnToggleSheetMetal, btnToggleAssemblyDrawing;
        private bool bomSheetMetalCollapsed, bomAssemblyDrawingCollapsed;
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
            int pad = CommandPixels(TaiTheme.CardPadding), gap = CommandPixels(6), top = CommandPixels(TaiTheme.CardHeader) + CommandPixels(6);
            int inner = Math.Max(1, cardWidth - pad * 2);
            int columns = inner >= CommandPixels(200) ? 2 : 1;
            int width = (inner - gap * (columns - 1)) / columns;
            var activeTiles = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(tiles, t => t != null));
            int index = 0;
            for (int i = 0; i < activeTiles.Length; i++)
            {
                Button tile = activeTiles[i];
                int column = index % columns, row = index / columns;
                int left = pad + column * (width + gap);
                bool lastOdd = columns == 2 && column == 0 && i == activeTiles.Length - 1;
                int tileW = lastOdd ? inner : (column == columns - 1 ? pad + inner - left : width);
                SetCommandBounds(tile, left, top + row * (tileHeight + gap), tileW, tileHeight);
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
            if (btnStandaloneCurveMacro != null)
            {
                btnStandaloneCurveMacro.Visible = false;
                cardSheetMetal?.Controls.Remove(btnStandaloneCurveMacro);
                tabDrawingBom?.Controls.Remove(btnStandaloneCurveMacro);
            }
            if (btnDxfSource != null)
            {
                btnDxfSource.Visible = false;
                cardSheetMetal?.Controls.Remove(btnDxfSource);
                tabDrawingBom?.Controls.Remove(btnDxfSource);
            }
            foreach (Button tile in new Button[] { btnCheckDfTk, btnCheckAll, btnCheckRound, btnCheckSamePart, btnCheckDxf, btnExportBomDxf })
                MoveControlToCard(tile, cardSheetMetal);
            foreach (Button tile in new Button[] { button2, btnOpenAssem, btnCheckBalloon, btnCheckDrawingBom })
                MoveControlToCard(tile, cardAssemblyDrawing);
            if (btnToggleSheetMetal != null && btnToggleAssemblyDrawing != null) return;

            cardSheetMetal.Text = "Kiểm tra chi tiết";
            cardAssemblyDrawing.Text = "Cụm lắp ráp & bản vẽ";
            WatchDisabledTiles(cardSheetMetal);
            WatchDisabledTiles(cardAssemblyDrawing);

            if (btnToggleSheetMetal == null)
            {
                btnToggleSheetMetal = new ModernButton
                {
                    Name = "btnToggleSheetMetal",
                    Kind = ModernButtonKind.Subtle,
                    Icon = TaiIcon.ChevronUp,
                    Text = "Thu gọn",
                    TabStop = false
                };
                btnToggleSheetMetal.Click += (s, e) =>
                {
                    bomSheetMetalCollapsed = !bomSheetMetalCollapsed;
                    LayoutDrawingBomCommands();
                };
                cardSheetMetal.Controls.Add(btnToggleSheetMetal);
                cardSheetMetal.MouseDown += (s, e) =>
                {
                    if (e.Button == MouseButtons.Left && e.Y <= CommandPixels(TaiTheme.CardHeader))
                    {
                        if (cardSheetMetal.GetChildAtPoint(e.Location) == null)
                        {
                            bomSheetMetalCollapsed = !bomSheetMetalCollapsed;
                            LayoutDrawingBomCommands();
                        }
                    }
                };
            }

            if (btnToggleAssemblyDrawing == null)
            {
                btnToggleAssemblyDrawing = new ModernButton
                {
                    Name = "btnToggleAssemblyDrawing",
                    Kind = ModernButtonKind.Subtle,
                    Icon = TaiIcon.ChevronUp,
                    Text = "Thu gọn",
                    TabStop = false
                };
                btnToggleAssemblyDrawing.Click += (s, e) =>
                {
                    bomAssemblyDrawingCollapsed = !bomAssemblyDrawingCollapsed;
                    LayoutDrawingBomCommands();
                };
                cardAssemblyDrawing.Controls.Add(btnToggleAssemblyDrawing);
                cardAssemblyDrawing.MouseDown += (s, e) =>
                {
                    if (e.Button == MouseButtons.Left && e.Y <= CommandPixels(TaiTheme.CardHeader))
                    {
                        if (cardAssemblyDrawing.GetChildAtPoint(e.Location) == null)
                        {
                            bomAssemblyDrawingCollapsed = !bomAssemblyDrawingCollapsed;
                            LayoutDrawingBomCommands();
                        }
                    }
                };
            }

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
                tabDrawingBom.AutoScroll = false;
                tabDrawingBom.AutoScrollMinSize = Size.Empty;
                tabDrawingBom.AutoScrollPosition = Point.Empty;
                int viewportWidth = tabDrawingBom.ClientSize.Width;
                int viewportHeight = tabDrawingBom.ClientSize.Height;
                LayoutDrawingBomViewport(viewportWidth, viewportHeight);
                actions?.AutoFitBomGrid();
                dgvModelBom.Invalidate();
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

            // 1. Status line (Full width now that toggles are on each card)
            if (string.IsNullOrWhiteSpace(lblStatus.Text) || lblStatus.Text == StatusConnected || lblStatus.Text == StatusWaiting ||
                lblStatus.Text.StartsWith("●", StringComparison.Ordinal) || lblStatus.Text.StartsWith("○", StringComparison.Ordinal))
                lblStatus.Text = swApp != null ? StatusConnected : StatusWaiting;
            int row = CommandPixels(24);
            int top = CommandPixels(6);
            SetCommandBounds(lblStatus, margin + CommandPixels(2), top, Math.Max(1, width - CommandPixels(4)), row);
            int next = top + row + CommandPixels(6);

            // 2. Command cards. Two cards collapsible independently!
            cardSheetMetal.Visible = cardAssemblyDrawing.Visible = !exporting;
            if (!exporting)
            {
                int tileHeight = CommandPixels(32);
                int headerHeight = CommandPixels(TaiTheme.CardHeader);

                // Card 1: Sheet metal (Kiểm tra chi tiết)
                if (btnStandaloneCurveMacro != null) btnStandaloneCurveMacro.Visible = false;
                if (btnDxfSource != null) btnDxfSource.Visible = false;
                Button[] sheetMetalTiles = { btnCheckDfTk, btnCheckAll, btnCheckRound, btnCheckSamePart,
                    btnCheckDxf, btnExportBomDxf };
                foreach (Button tile in sheetMetalTiles)
                    if (tile != null) tile.Visible = !bomSheetMetalCollapsed;
                int height1 = bomSheetMetalCollapsed
                    ? headerHeight + CommandPixels(2)
                    : LayoutTiles(sheetMetalTiles, width, tileHeight);
                SetCommandBounds(cardSheetMetal, margin, next, width, height1);
                LayoutCardHeaderToggle(cardSheetMetal, btnToggleSheetMetal, bomSheetMetalCollapsed, width);
                next += height1 + gap;

                // Card 2: Assembly & Drawing (Cụm lắp ráp & bản vẽ)
                Button[] assemblyTiles = { button2, btnOpenAssem, btnCheckBalloon, btnCheckDrawingBom };
                foreach (Button tile in assemblyTiles)
                    if (tile != null) tile.Visible = !bomAssemblyDrawingCollapsed;
                int height2 = bomAssemblyDrawingCollapsed
                    ? headerHeight + CommandPixels(2)
                    : LayoutTiles(assemblyTiles, width, tileHeight);
                SetCommandBounds(cardAssemblyDrawing, margin, next, width, height2);
                LayoutCardHeaderToggle(cardAssemblyDrawing, btnToggleAssemblyDrawing, bomAssemblyDrawingCollapsed, width);
                next += height2 + gap;
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

            // 4. Footer: load, clear, stop. Pinned at the bottom of the viewport!
            Button[] footer = { btnLoadBom, btnClearBom, button1 };
            int footerHeight = CommandPixels(32), footerGap = CommandPixels(6);
            int footerWidth = Math.Max(1, (width - footerGap * 2) / 3);
            int footerTop = Math.Max(gridTop + CommandPixels(40), viewportHeight - margin - footerHeight);
            int gridHeight = Math.Max(CommandPixels(40), footerTop - gridTop - gap);

            SetCommandBounds(dgvModelBom, margin, gridTop, width, gridHeight);
            dgvModelBom.Visible = !exporting;
            if (exporting)
            {
                SetCommandBounds(bomDxfExportPane, margin, gridTop, width, gridHeight);
                bomDxfExportPane.BringToFront();
            }
            for (int n = 0; n < footer.Length; n++)
            {
                int left = margin + n * (footerWidth + footerGap);
                SetCommandBounds(footer[n], left, footerTop, n == footer.Length - 1 ? margin + width - left : footerWidth, footerHeight);
            }
            return viewportHeight;
        }

        private void LayoutCardHeaderToggle(ModernCard card, ModernButton toggle, bool collapsed, int cardWidth)
        {
            if (card == null || toggle == null) return;
            int pad = CommandPixels(TaiTheme.CardPadding);
            bool narrow = cardWidth < CommandPixels(320);
            int toggleWidth = CommandPixels(narrow ? 30 : 84);
            toggle.Text = narrow ? string.Empty : (collapsed ? "Hiện lệnh" : "Thu gọn");
            toggle.Icon = collapsed ? TaiIcon.ChevronDown : TaiIcon.ChevronUp;
            toggle.Visible = true;
            SetCommandBounds(toggle, cardWidth - pad - toggleWidth, CommandPixels(5), toggleWidth, CommandPixels(24));
            toggle.BringToFront();
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
        private ModernButton btnToggleMacroCommands;
        private bool macroCommandsCollapsed;
        private Control checkHoleResultsView;

        internal void SetModelMacroCommandsCollapsed(bool collapsed)
        {
            macroCommandsCollapsed = collapsed;
            if (btnToggleMacroCommands != null)
            {
                btnToggleMacroCommands.Text = macroCommandsCollapsed ? "Hiện lệnh" : "Thu gọn";
                btnToggleMacroCommands.Icon = macroCommandsCollapsed ? TaiIcon.ChevronDown : TaiIcon.ChevronUp;
            }
            LayoutModelMacroCommands();
        }

        private void EnsureModelMacroControls()
        {
            EnsureBendMinimumButton();
            EnsureAutoFrameJointButton();
            if (tabModelMacroPage == null || cardMacroCommands != null) return;

            cardMacroCommands = new ModernCard { Name = "cardMacroCommands", Text = "Lệnh macro" };
            tabModelMacroPage.Controls.Add(cardMacroCommands);
            foreach (Button tile in new Button[] { btnCheckAssemblyHole, btnMirrorPart, btnEdgeToEqualSpline,
                btnRepair3DSpline, btnVCutPropertySetup, btnCheckDxfMacro, btnCheckBendMinimum,
                btnAutoFrameJoint })
                MoveControlToCard(tile, cardMacroCommands);
            btnToggleMacroCommands = new ModernButton
            {
                Name = "btnToggleMacroCommands",
                Kind = ModernButtonKind.Subtle,
                Icon = TaiIcon.ChevronUp,
                Text = "Thu gọn",
                TabStop = false
            };
            btnToggleMacroCommands.Click += (s, e) => SetModelMacroCommandsCollapsed(!macroCommandsCollapsed);
            cardMacroCommands.Controls.Add(btnToggleMacroCommands);
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
            if (vCutBendAssignmentControl != null) vCutBendAssignmentControl.BringToFront();
        }

        private void ShowModelInspectionReport(string text)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) { BeginInvoke((Action)(() => ShowModelInspectionReport(text))); return; }
            EnsureModelMacroControls();
            if (checkHoleResultsView != null && !checkHoleResultsView.IsDisposed) checkHoleResultsView.Visible = false;
            if (vCutBendAssignmentControl != null && vCutBendAssignmentControl.Visible)
            {
                vCutBendAssignmentControl.Visible = false;
                LayoutModelMacroCommands();
            }
            modelInspectionReport.Text = text ?? string.Empty;
            modelInspectionReport.Bounds = lblCheckAssemblyHoleResult.Bounds;
            lblCheckAssemblyHoleResult.Visible = false; modelInspectionReport.Visible = true;
            modelInspectionReport.BringToFront();
        }

        private void ShowCheckHoleResultsView(Control view)
        {
            if (IsDisposed || Disposing || view == null || view.IsDisposed) return;
            if (InvokeRequired) { BeginInvoke((Action)(() => ShowCheckHoleResultsView(view))); return; }
            EnsureModelMacroControls();
            checkHoleResultsView = view;
            cardInspectionMonitor.Controls.Add(view);
            view.Bounds = lblCheckAssemblyHoleResult.Bounds;
            lblCheckAssemblyHoleResult.Visible = false;
            modelInspectionReport.Visible = false;
            view.Show(); view.BringToFront();
        }

        private void ShowCheckHoleInspectionReport(string text)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) { BeginInvoke((Action)(() => ShowCheckHoleInspectionReport(text))); return; }
            ShowModelInspectionReport(text);
            if (checkHoleResultsView == null || checkHoleResultsView.IsDisposed) return;
            modelInspectionReport.Visible = false;
            checkHoleResultsView.Visible = true;
            checkHoleResultsView.BringToFront();
        }

        private void ClearModelInspectionReport()
        {
            if (checkHoleResultsView != null && !checkHoleResultsView.IsDisposed) checkHoleResultsView.Visible = false;
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
            int pad = CommandPixels(TaiTheme.CardPadding), header = CommandPixels(TaiTheme.CardHeader);
            bool vCutOpen = vCutBendAssignmentControl != null && vCutBendAssignmentControl.Visible;

            Button[] macroTiles = { btnCheckAssemblyHole, btnMirrorPart, btnEdgeToEqualSpline,
                btnRepair3DSpline, btnVCutPropertySetup, btnCheckDxfMacro, btnCheckBendMinimum,
                btnAutoFrameJoint };
            foreach (Button tile in macroTiles)
                if (tile != null) tile.Visible = !macroCommandsCollapsed;

            int toggleWidth = CommandPixels(108);
            SetCommandBounds(btnToggleMacroCommands, width - pad - toggleWidth, CommandPixels(5), toggleWidth, CommandPixels(24));

            int height = macroCommandsCollapsed
                ? header + CommandPixels(2)
                : LayoutTiles(macroTiles, width, CommandPixels(44));
            SetCommandBounds(cardMacroCommands, margin, top, width, height);

            int monitorTop = top + height + gap;
            int minWorkspaceHeight = CommandPixels(vCutOpen ? 280 : 160);
            int monitorHeight = Math.Max(minWorkspaceHeight, viewportHeight - monitorTop - margin);

            cardInspectionMonitor.Visible = !vCutOpen;
            if (vCutOpen)
            {
                SetCommandBounds(vCutBendAssignmentControl, margin, monitorTop, width, monitorHeight);
                vCutBendAssignmentControl.BringToFront();
            }
            else
            {
                SetCommandBounds(cardInspectionMonitor, margin, monitorTop, width, monitorHeight);
                int clearWidth = CommandPixels(64);
                SetCommandBounds(btnClearInspection, width - pad - clearWidth, CommandPixels(5), clearWidth, CommandPixels(24));
                Rectangle body = new Rectangle(pad, header + CommandPixels(6), Math.Max(1, width - pad * 2), Math.Max(1, monitorHeight - header - pad - CommandPixels(6)));
                SetCommandBounds(lblCheckAssemblyHoleResult, body.X, body.Y, body.Width, body.Height);
                SetCommandBounds(modelInspectionReport, body.X, body.Y, body.Width, body.Height);
                SetCommandBounds(checkHoleResultsView, body.X, body.Y, body.Width, body.Height);
            }
            return monitorTop + monitorHeight + margin;
        }
    }
}
