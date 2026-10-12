using System;
using System.Drawing;
using System.Windows.Forms;
using ADDIN.UI;

namespace ADDIN
{
    // TaiTheme applied to the task pane. One table names every command, its
    // icon and the line that says what it does; one pass gives the remaining
    // controls the theme's colours and fonts; and the two pages laid out here
    // (Model > Props, Model > Edit) get the same margins as the others.
    public partial class BomTaskPaneControl
    {
        private Rectangle modelPropsCard;

        private void ApplyTaiTheme()
        {
            BackColor = TaiTheme.Page;
            foreach (TabPage page in new[] { tabDrawing, tabModel, tabDrawingBom, tabComponentDrawing,
                tabModelPropsPage, tabModelEditPage, tabModelMacroPage })
            {
                if (page == null) continue;
                page.UseVisualStyleBackColor = false;
                page.BackColor = TaiTheme.Page;
                page.Padding = Padding.Empty;
            }
            // The legacy layout of Model > Edit keeps 6 px inside its panel; 2 px here makes the 8 px of the other pages.
            if (tabModelEditPage != null) tabModelEditPage.Padding = new Padding(2, 0, 2, 0);

            // Drawing > Drawing BOM
            Command(btnCheckDfTk, TaiIcon.BendFlat, "DF/TK", "Uốn & khai triển");
            Command(btnCheckAll, TaiIcon.Scribe, "Kegaki", "Mặt chấn & vạch dấu");
            Command(btnCheckRound, TaiIcon.HoleRadius, "Round", "Lỗ tròn & bo cung R");
            Command(btnCheckSamePart, TaiIcon.SameParts, "Same", "Chi tiết đồng dạng");
            Command(btnCheckDxf, TaiIcon.Compare, "DXF ↔ Drawing", "So DXF với bản vẽ");
            Command(btnStandaloneCurveMacro, TaiIcon.Curve, "Check đường", "Macro Drawing / DXF");
            Command(btnExportBomDxf, TaiIcon.Export, "Xuất DXF", "Flat view + Check DXF");
            Command(button2, TaiIcon.SortList, "Unit", "Sắp xếp số hiệu cụm");
            Command(btnOpenAssem, TaiIcon.Assembly, "Assem", "Mở cụm lắp ráp 3D");
            Command(btnCheckBalloon, TaiIcon.Balloon, "Balloon", "Kiểm tra số Balloon");
            Command(btnCheckDrawingBom, TaiIcon.SheetCheck, "Drawing", "Kiểm tra bản vẽ 2D");
            Act(btnLoadBom, ModernButtonKind.Primary, TaiIcon.Refresh, "Cập nhật");
            Act(btnClearBom, ModernButtonKind.Danger, TaiIcon.Trash, "Xóa bảng");
            Act(button1, ModernButtonKind.Standard, TaiIcon.Stop, "Dừng");

            // Drawing > Component Drawing. The second line says what to select
            // first: with that selected, one click runs the command.
            Command(btnGetWL, TaiIcon.Measure, "Lấy W, L", "");
            Act(btnHorizontalAlignment, ModernButtonKind.Standard, TaiIcon.AlignHorizontal, "Canh ngang");
            Act(btnRotateCw, ModernButtonKind.Standard, TaiIcon.RotateCw, "+90°");
            Act(btnRotateCcw, ModernButtonKind.Standard, TaiIcon.RotateCcw, "−90°");
            Act(btnNote, ModernButtonKind.Standard, TaiIcon.Note, null);
            Act(btnText, ModernButtonKind.Standard, TaiIcon.TextT, null);
            Act(btnInsertBalloon, ModernButtonKind.Standard, TaiIcon.Balloon, null);
            Act(btnDeleteNote, ModernButtonKind.Subtle, TaiIcon.Trash, "", ModernButtonTone.Danger);
            Act(btnDeleteText, ModernButtonKind.Subtle, TaiIcon.Trash, "", ModernButtonTone.Danger);
            Command(dimvang, TaiIcon.Eraser, "Xóa DIM vàng", "DIM, Balloon, Text", ModernButtonTone.Danger);
            Command(btnDimMatCat, TaiIcon.DimSection, "DIM mặt cắt", "Chọn cạnh trước");
            Command(btnSplineToArcs, TaiIcon.ArcRadius, "Spline → cung R", "Chọn spline trước");
            Command(btnDimKegaki, TaiIcon.DimScribe, "DIM Kegaki", "Chọn View trước");
            Command(btnDimKichThuocLo, TaiIcon.DimHole, "DIM lỗ", "Chọn View trước");
            Command(btnFixScale, TaiIcon.FitView, "Fix tỉ lệ View", "Chọn View trước");
            Command(btnRepairDim, TaiIcon.Wrench, "Repair DIM", "DIM mất tham chiếu");
            Command(btnVCutSection, TaiIcon.Section, "Section 断面図", "Chọn đường cắt");

            // Model > Props
            Act(btnModelApplyProps, ModernButtonKind.Primary, TaiIcon.Check, "Apply");
            Act(btnModelResetProps, ModernButtonKind.Standard, TaiIcon.Eraser, "Clear");
            Act(btnModelUpdateProps, ModernButtonKind.Standard, TaiIcon.Refresh, "Reload");

            // Model > Edit
            Command(btnMakeHole, TaiIcon.HoleMake, "Make hole", "");
            Command(btnRepairHole, TaiIcon.HoleRepair, "Repair hole", "");
            Command(btnPaintHoleSummary, TaiIcon.HoleCount, "Đếm hole", "");
            Act(btnMakeHoleUpdate, ModernButtonKind.Primary, TaiIcon.Refresh, null);
            Act(btnMakeHoleReset, ModernButtonKind.Standard, TaiIcon.Undo, null);
            Act(btnMakeHolePattern, ModernButtonKind.Standard, TaiIcon.Pattern, null);
            Act(btnMakeHoleAccept, ModernButtonKind.Success, TaiIcon.Check, null);
            Act(btnDeleteMakeHoleSize, ModernButtonKind.Danger, TaiIcon.None, null);
            Act(btnManageHoleTypes, ModernButtonKind.Standard, TaiIcon.More, "");
            Act(btnChangeHoleName, ModernButtonKind.Standard, TaiIcon.Tag, "Change name hole");
            Act(btnMarkFeature, ModernButtonKind.Warning, TaiIcon.Mark, "MARK (khắc dấu)");
            Act(btnShowMarks, ModernButtonKind.Standard, TaiIcon.None, "Xem MARK");
            if (lstMarkInfo != null) { lstMarkInfo.Font = TaiTheme.Small; TaiInputFrame.Attach(lstMarkInfo); }
            if (lblHoleType != null) lblHoleType.Text = "Tên lỗ (Hole type)";

            // Model > Macro
            Command(btnCheckAssemblyHole, TaiIcon.Concentric, "Check hole", "Đồng tâm Assembly");
            Command(btnMirrorPart, TaiIcon.Mirror, "Mirror part", "Đối xứng Part");
            Command(btnEdgeToEqualSpline, TaiIcon.Spline, "Auto spline", "Dựng spline từ cạnh");
            Command(btnRepair3DSpline, TaiIcon.SplineSmooth, "Repair 3D", "Mịn spline 3D");
            Command(btnVCutPropertySetup, TaiIcon.VCut, "Set bend line", "曲げ線 & V-Cut");
            Command(btnCheckDxfMacro, TaiIcon.FileSearch, "Check DXF file", "Kiểm tra file DXF 2D");

            // The few large buttons carry their icon above the name.
            foreach (Button stacked in new Button[] { btnGetWL, btnMakeHole, btnRepairHole, btnPaintHoleSummary })
                if (stacked != null) stacked.TextImageRelation = TextImageRelation.ImageAboveText;

            if (grpComponentSize != null) grpComponentSize.Text = "View size";
            if (grpComponentBom != null) grpComponentBom.Text = "Text & Balloon";
            if (groupBox3 != null) groupBox3.Text = "Macro tools";

            ThemeControls(this);
            ThemeStatusControls();

            TaiTheme.StyleGroupAsCard(grpMakeHoleOptions);
            if (panelModelProps != null)
            {
                UIHelper.EnableDoubleBuffer(panelModelProps);
                panelModelProps.Paint += (sender, e) =>
                {
                    Rectangle card = modelPropsCard;
                    card.Offset(panelModelProps.AutoScrollPosition);
                    TaiTheme.PaintCard(e.Graphics, panelModelProps, card, "Thuộc tính chi tiết");
                };
                panelModelProps.Resize += (sender, e) => LayoutModelPropsPage();
                LayoutModelPropsPage();
            }
        }

        // A command the user picks from a card: icon, name and one line of help.
        private static void Command(Button button, TaiIcon icon, string title, string detail,
            ModernButtonTone tone = ModernButtonTone.Accent)
        {
            ModernButton tile = button as ModernButton;
            if (tile == null) return;
            tile.Kind = ModernButtonKind.Tile; tile.Icon = icon; tile.Tone = tone;
            tile.Image = null; tile.Text = title; tile.Subtitle = detail;
        }

        // A plain action button. A null text keeps the caption the code already maintains.
        private static void Act(Button button, ModernButtonKind kind, TaiIcon icon, string text,
            ModernButtonTone tone = ModernButtonTone.Accent)
        {
            ModernButton action = button as ModernButton;
            if (action == null) return;
            action.Kind = kind; action.Icon = icon; action.Tone = tone; action.Image = null;
            if (text != null) action.Text = text;
        }

        // Colours and fonts for everything that is not a ModernButton or a card.
        // Hosted controls with a look of their own are left alone.
        private void ThemeControls(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                Commands.HistoryTextBox history = control as Commands.HistoryTextBox;
                if (history != null) { history.Font = TaiTheme.Body; history.MinimumSize = new Size(40, 30); history.BorderStyle = BorderStyle.None; }    // it draws the theme border itself
                if (history != null || control is DataGridView || control is UI.VCutBendAssignmentControl ||
                    control is UI.BomDxfExportPane || control is System.Windows.Forms.Integration.ElementHost)
                    continue;
                Label label = control as Label;
                TextBoxBase text = control as TextBoxBase;
                ComboBox combo = control as ComboBox;
                if (label != null)
                {
                    label.BackColor = Color.Transparent;
                    label.ForeColor = TaiTheme.TextMuted;
                    label.Font = TaiTheme.Body;
                }
                else if (control is CheckBox)
                {
                    control.BackColor = Color.Transparent;
                    control.ForeColor = TaiTheme.Text;
                    control.Font = TaiTheme.Body;
                }
                else if (text != null && !(text is RichTextBox))
                {
                    text.Font = TaiTheme.Body;
                    text.BackColor = TaiTheme.Surface;
                    text.ForeColor = TaiTheme.Text;
                    TaiInputFrame.Attach(text);
                }
                else if (combo != null)
                {
                    combo.FlatStyle = FlatStyle.Standard;
                    combo.Font = TaiTheme.Body;
                    combo.ForeColor = TaiTheme.Text;
                    TaiComboFrame.Attach(combo);
                }
                else if (control.GetType() == typeof(Panel) || control is ModernFieldBox)
                {
                    if (!(control is ModernFieldBox)) control.BackColor = TaiTheme.Page;
                }
                ThemeControls(control);
            }
        }

        private void ThemeStatusControls()
        {
            if (lblStatus != null) { lblStatus.Font = TaiTheme.Small; lblStatus.ForeColor = TaiTheme.TextMuted; }
            if (lblBomCount != null) { lblBomCount.Font = TaiTheme.Small; lblBomCount.ForeColor = TaiTheme.TextFaint; }
            if (lblHoleType != null) { lblHoleType.Font = TaiTheme.SmallStrong; lblHoleType.AutoSize = false; }
            if (lblCheckAssemblyHoleResult != null) lblCheckAssemblyHoleResult.ForeColor = TaiTheme.TextFaint;
            foreach (TextBox value in new[] { txtModelName, txtModelMaterial, txtModelThickness, txtModelGoban, txtModelQty, txtModelFinish })
                if (value != null) value.Font = TaiTheme.Value;
            if (pnlMakeHoleDiagram != null) pnlMakeHoleDiagram.BackColor = TaiTheme.SurfaceHover;
        }


        // Model > Props: the three actions in one row, the six properties in a card.
        private void LayoutModelPropsPage()
        {
            if (panelModelProps == null || panelModelProps.ClientSize.Width <= 0 || btnModelApplyProps == null) return;
            int margin = CommandPixels(8), gap = CommandPixels(6);
            int width = Math.Max(CommandPixels(200), panelModelProps.ClientSize.Width - margin * 2);
            Point scroll = panelModelProps.AutoScrollPosition;
            int left = margin + scroll.X, top = CommandPixels(8) + scroll.Y;

            Button[] actions = { btnModelApplyProps, btnModelResetProps, btnModelUpdateProps };
            int actionWidth = (width - gap * 2) / 3, actionHeight = CommandPixels(32);
            for (int n = 0; n < actions.Length; n++)
            {
                int x = left + n * (actionWidth + gap);
                SetCommandBounds(actions[n], x, top, n == actions.Length - 1 ? left + width - x : actionWidth, actionHeight);
            }

            Label[] labels = { lblModelName, lblModelMaterial, lblModelThickness, lblModelGoban, lblModelQty, lblModelFinish };
            TextBox[] values = { txtModelName, txtModelMaterial, txtModelThickness, txtModelGoban, txtModelQty, txtModelFinish };
            int cardTop = top + actionHeight + CommandPixels(8);
            int pad = CommandPixels(TaiTheme.CardPadding), labelWidth = CommandPixels(66), rowHeight = CommandPixels(40);
            int y = cardTop + CommandPixels(TaiTheme.CardHeader);
            for (int n = 0; n < labels.Length; n++)
            {
                if (labels[n] == null || values[n] == null) continue;
                labels[n].AutoSize = false;
                labels[n].TextAlign = ContentAlignment.MiddleLeft;
                SetCommandBounds(labels[n], left + pad, y, labelWidth, rowHeight - CommandPixels(4));
                int inputHeight = values[n].Height;
                SetCommandBounds(values[n], left + pad + labelWidth, y + Math.Max(0, (rowHeight - CommandPixels(4) - inputHeight) / 2),
                    Math.Max(CommandPixels(60), width - pad * 2 - labelWidth), inputHeight);
                y += rowHeight;
            }
            modelPropsCard = new Rectangle(margin, cardTop - scroll.Y, width, y + CommandPixels(4) - cardTop);
            panelModelProps.AutoScrollMinSize = new Size(0, modelPropsCard.Bottom + CommandPixels(8));
            panelModelProps.Invalidate();
        }

        // Model > Edit: three command tiles across the width, then the Make hole
        // actions and the hole-name tools on one left edge.
        private void LayoutModelEditPage()
        {
            if (panelModelCommands == null || grpMakeHoleOptions == null) return;
            const int margin = 6, gap = 8;
            int width = Math.Max(228, panelModelCommands.ClientSize.Width - margin * 2);
            Button[] tiles = { btnMakeHole, btnRepairHole, btnPaintHoleSummary };
            int tileWidth = (width - gap * 2) / 3, tileHeight = 80, top = 8;
            for (int n = 0; n < tiles.Length; n++)
            {
                if (tiles[n] == null) continue;
                int x = margin + n * (tileWidth + gap);
                tiles[n].SetBounds(x, top, n == tiles.Length - 1 ? margin + width - x : tileWidth, tileHeight);
            }

            int next = top + tileHeight + 10;
            if (makeHoleActionsOnly)
            {
                LayoutMakeHoleActions();
                grpMakeHoleOptions.Location = new Point(margin, next);
                next += grpMakeHoleOptions.Height + 12;
            }
            lblHoleType.SetBounds(margin + 2, next, width - 4, 22);
            cboHoleType.SetBounds(margin, next + 26, Math.Max(100, width - 38), 30);
            btnManageHoleTypes.SetBounds(margin + width - 32, next + 26, 32, 30);
            btnChangeHoleName.SetBounds(margin, next + 64, width, 36);
            int markRow = LayoutMarkFeatureControls(margin, next + 108, width);
            if (!makeHoleActionsOnly) grpMakeHoleOptions.Location = new Point(margin, next + 116 + markRow);

            // The tile of the command whose PropertyManager is open stays lit.
            ModernButton make = btnMakeHole as ModernButton, repair = btnRepairHole as ModernButton;
            if (make != null) make.Selected = nativeMakeHolePage != null && nativeMakeHolePage.IsOpen;
            if (repair != null) repair.Selected = repairHolePanelMode || nativeModelCommandPage is RepairHolePropertyManagerPage;
        }
    }
}
