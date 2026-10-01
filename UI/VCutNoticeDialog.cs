using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace ADDIN.UI
{
    // Modernized bilingual notice/confirmation dialog for SolidWorks Add-in
    internal partial class VCutNoticeDialog : Form
    {
        private MessageBoxIcon currentIcon = MessageBoxIcon.Information;
        private Color currentAccent = Color.FromArgb(16, 185, 129);
        private Color currentBadgeBg = Color.FromArgb(236, 253, 245);
        private Color currentBadgeBorder = Color.FromArgb(167, 243, 208);
        private Color currentCardBg = Color.FromArgb(248, 250, 252);
        private Color currentCardBorder = Color.FromArgb(226, 232, 240);

        private VCutNoticeDialog()
        {
            InitializeComponent();
            UIHelper.EnableDoubleBuffer(this);
            UIHelper.EnableDoubleBuffer(panelHeader);
            UIHelper.EnableDoubleBuffer(pnlIcon);
            UIHelper.EnableDoubleBuffer(panelBodyCard);
            UIHelper.EnableDoubleBuffer(panelFooter);

            pnlIcon.Paint += PnlIcon_Paint;
            panelBodyCard.Paint += PanelBodyCard_Paint;
            panelFooter.Paint += PanelFooter_Paint;
            panelHeader.Paint += PanelHeader_Paint;
        }

        internal static DialogResult Inform(IWin32Window owner, string title,
            string message, MessageBoxIcon icon)
        {
            using (var dialog = new VCutNoticeDialog())
            {
                dialog.Configure(title, message, icon, false);
                return dialog.ShowDialog(owner);
            }
        }

        internal static DialogResult Confirm(IWin32Window owner, string title,
            string message)
        {
            using (var dialog = new VCutNoticeDialog())
            {
                dialog.Configure(title, message, MessageBoxIcon.Warning, true);
                return dialog.ShowDialog(owner);
            }
        }

        private void Configure(string title, string message, MessageBoxIcon icon, bool confirm)
        {
            currentIcon = icon;

            // Unicode FormKC normalization: cleans up decomposed accents & half-width katakana (ｴｯｼﾞﾍﾞﾝﾄﾞ -> エッジベンド)
            title = (title ?? "").Normalize(NormalizationForm.FormKC);
            message = (message ?? "").Normalize(NormalizationForm.FormKC);

            Text = title;
            lblTitle.Text = title;
            lblBody.Text = message;

            if (icon == MessageBoxIcon.Error)
            {
                currentAccent = Color.FromArgb(225, 29, 72);          // Rose 600
                currentBadgeBg = Color.FromArgb(255, 241, 242);       // Rose 50
                currentBadgeBorder = Color.FromArgb(254, 205, 211);   // Rose 200
                currentCardBg = Color.FromArgb(255, 248, 248);
                currentCardBorder = Color.FromArgb(254, 205, 211);
                lblEyebrow.Text = "TAI TOOL  ·  LỖI";
                lblEyebrow.ForeColor = currentAccent;
            }
            else if (icon == MessageBoxIcon.Warning)
            {
                currentAccent = Color.FromArgb(217, 119, 6);           // Amber 600
                currentBadgeBg = Color.FromArgb(254, 243, 199);        // Amber 100
                currentBadgeBorder = Color.FromArgb(253, 230, 138);    // Amber 200
                currentCardBg = Color.FromArgb(254, 252, 248);
                currentCardBorder = Color.FromArgb(253, 230, 138);
                lblEyebrow.Text = "TAI TOOL  ·  CẢNH BÁO";
                lblEyebrow.ForeColor = currentAccent;
            }
            else
            {
                currentAccent = Color.FromArgb(5, 150, 105);           // Emerald 600
                currentBadgeBg = Color.FromArgb(236, 253, 245);        // Emerald 50
                currentBadgeBorder = Color.FromArgb(167, 243, 208);    // Emerald 200
                currentCardBg = Color.FromArgb(248, 250, 252);         // Slate 50
                currentCardBorder = Color.FromArgb(226, 232, 240);     // Slate 200
                lblEyebrow.Text = "TAI TOOL  ·  THÔNG BÁO";
                lblEyebrow.ForeColor = currentAccent;
            }

            panelAccent.BackColor = currentAccent;
            panelBodyCard.BackColor = currentCardBg;
            lblBody.BackColor = currentCardBg;

            // Configure buttons
            btnPrimary.NormalColor = currentAccent;
            btnPrimary.HoverColor = ControlPaint.Light(currentAccent, 0.15f);
            btnPrimary.PressColor = ControlPaint.Dark(currentAccent, 0.12f);
            btnPrimary.BorderColor = Color.Transparent;
            btnPrimary.ForeColor = Color.White;
            btnPrimary.Text = confirm ? "Tiếp tục" : "Đóng";
            btnPrimary.DialogResult = confirm ? DialogResult.Yes : DialogResult.OK;

            btnSecondary.Visible = confirm;
            btnSecondary.DialogResult = DialogResult.No;
            btnSecondary.Text = "Hủy";
            btnSecondary.NormalColor = Color.White;
            btnSecondary.BorderColor = Color.FromArgb(203, 213, 225);
            btnSecondary.HoverColor = Color.FromArgb(241, 245, 249);
            btnSecondary.PressColor = Color.FromArgb(226, 232, 240);
            btnSecondary.ForeColor = Color.FromArgb(51, 65, 85);

            AcceptButton = btnPrimary;
            CancelButton = confirm ? (IButtonControl)btnSecondary : btnPrimary;

            // Calculate dynamic sizing to prevent truncation
            AdjustLayout(confirm);

            pnlIcon.Invalidate();
            panelBodyCard.Invalidate();
            panelFooter.Invalidate();
            panelHeader.Invalidate();
        }

        private void AdjustLayout(bool confirm)
        {
            const int formWidth = 500;
            const int cardMarginH = 20;
            int cardWidth = formWidth - cardMarginH * 2; // 460px

            // Set maximum width constraint for word wrapping
            lblBody.MaximumSize = new Size(cardWidth - 20, 0);
            lblBody.AutoSize = true;

            // Measure actual rendered size with Label.GetPreferredSize
            Size pref = lblBody.GetPreferredSize(new Size(cardWidth - 20, 0));
            int contentHeight = Math.Max(pref.Height, lblBody.Height);

            // Generous card height with 16px bottom breathing room so descenders and accents are NEVER cut off
            int cardHeight = Math.Max(90, Math.Min(340, contentHeight + 16));

            panelBodyCard.Location = new Point(cardMarginH, panelHeader.Bottom + 12);
            panelBodyCard.Size = new Size(cardWidth, cardHeight);

            const int footerHeight = 54;
            int totalFormHeight = panelBodyCard.Bottom + 16 + footerHeight;

            this.ClientSize = new Size(formWidth, totalFormHeight);

            // Align buttons neatly to bottom-right
            int btnY = (panelFooter.Height - btnPrimary.Height) / 2;
            btnPrimary.Location = new Point(panelFooter.Width - 20 - btnPrimary.Width, btnY);

            if (confirm)
            {
                btnSecondary.Visible = true;
                btnSecondary.Location = new Point(btnPrimary.Left - 10 - btnSecondary.Width, btnY);
            }
            else
            {
                btnSecondary.Visible = false;
            }
        }

        private void PanelHeader_Paint(object sender, PaintEventArgs e)
        {
            using (var pen = new Pen(Color.FromArgb(226, 232, 240), 1f))
            {
                e.Graphics.DrawLine(pen, 0, panelHeader.Height - 1, panelHeader.Width, panelHeader.Height - 1);
            }
        }

        private void PanelFooter_Paint(object sender, PaintEventArgs e)
        {
            using (var pen = new Pen(Color.FromArgb(226, 232, 240), 1f))
            {
                e.Graphics.DrawLine(pen, 0, 0, panelFooter.Width, 0);
            }
        }

        private void PanelBodyCard_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, panelBodyCard.Width - 1, panelBodyCard.Height - 1);
            using (var path = UIHelper.GetRoundPath(r, 6))
            {
                using (var pen = new Pen(currentCardBorder, 1f))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }
        }

        private void PnlIcon_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(1, 1, pnlIcon.Width - 3, pnlIcon.Height - 3);

            using (var brush = new SolidBrush(currentBadgeBg))
                g.FillEllipse(brush, rect);
            using (var pen = new Pen(currentBadgeBorder, 1.2f))
                g.DrawEllipse(pen, rect);

            float cx = rect.X + rect.Width / 2f;
            float cy = rect.Y + rect.Height / 2f;

            if (currentIcon == MessageBoxIcon.Error)
            {
                using (var pen = new Pen(currentAccent, 2.6f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    float r = 6f;
                    g.DrawLine(pen, cx - r, cy - r, cx + r, cy + r);
                    g.DrawLine(pen, cx + r, cy - r, cx - r, cy + r);
                }
            }
            else if (currentIcon == MessageBoxIcon.Warning)
            {
                using (var pen = new Pen(currentAccent, 2.8f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, cx, cy - 8.5f, cx, cy + 1.5f);
                }
                using (var dotBrush = new SolidBrush(currentAccent))
                {
                    float dotR = 2.1f;
                    g.FillEllipse(dotBrush, cx - dotR, cy + 6f, dotR * 2, dotR * 2);
                }
            }
            else
            {
                using (var pen = new Pen(currentAccent, 2.6f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    g.DrawLines(pen, new PointF[]
                    {
                        new PointF(cx - 7f, cy),
                        new PointF(cx - 2f, cy + 5f),
                        new PointF(cx + 7f, cy - 4f)
                    });
                }
            }
        }
    }
}
