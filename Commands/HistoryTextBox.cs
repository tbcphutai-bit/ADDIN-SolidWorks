using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace ADDIN.Commands
{
    [DefaultEvent("TextChanged")]
    public sealed class HistoryTextBox : UserControl
    {
        private readonly TextBox inputTextBox;
        private readonly Button dropDownButton;
        private const int MaximumVisibleHistoryItems = 10;
        private readonly ToolStripDropDown historyMenu;
        private readonly ListBox historyList;
        private readonly ToolStripControlHost historyHost;
        private readonly List<string> historyItems;
        private bool historyMenuDirty = true;

        public HistoryTextBox()
        {
            historyItems = new List<string>();

            inputTextBox = new TextBox();
            inputTextBox.BorderStyle = BorderStyle.None;
            inputTextBox.Dock = DockStyle.Fill;
            inputTextBox.HideSelection = true;
            inputTextBox.Margin = new Padding(0);
            inputTextBox.Multiline = false;
            inputTextBox.ShortcutsEnabled = true;
            inputTextBox.TextChanged += InputTextBox_TextChanged;
            inputTextBox.KeyDown += InputTextBox_KeyDown;
            inputTextBox.MouseDoubleClick += InputTextBox_MouseDoubleClick;

            dropDownButton = new Button();
            dropDownButton.Dock = DockStyle.Right;
            dropDownButton.FlatAppearance.BorderSize = 0;
            dropDownButton.FlatStyle = FlatStyle.Flat;
            dropDownButton.Margin = new Padding(0);
            dropDownButton.Padding = new Padding(0);
            dropDownButton.TabStop = false;
            dropDownButton.Text = "";
            dropDownButton.BackColor = UI.TaiTheme.Surface;
            dropDownButton.FlatAppearance.MouseOverBackColor = UI.TaiTheme.SurfaceHover;
            dropDownButton.FlatAppearance.MouseDownBackColor = UI.TaiTheme.SurfacePressed;
            dropDownButton.Paint += DropDownButton_Paint;
            dropDownButton.Cursor = Cursors.Hand;
            dropDownButton.Width = 20;
            dropDownButton.Click += DropDownButton_Click;

            historyList = new ListBox();
            historyList.BorderStyle = BorderStyle.FixedSingle;
            historyList.IntegralHeight = false;
            historyList.DrawMode = DrawMode.OwnerDrawFixed;
            historyList.HorizontalScrollbar = false;
            historyList.ScrollAlwaysVisible = false;
            historyList.DrawItem += HistoryList_DrawItem;
            historyList.MouseUp += HistoryList_MouseUp;
            historyList.KeyDown += HistoryList_KeyDown;

            historyHost = new ToolStripControlHost(historyList);
            historyHost.AutoSize = false;
            historyHost.Margin = new Padding(0);
            historyHost.Padding = new Padding(0);

            historyMenu = new ToolStripDropDown();
            historyMenu.AutoSize = false;
            historyMenu.Padding = new Padding(0);
            historyMenu.Items.Add(historyHost);
            historyMenu.Closed += HistoryMenu_Closed;

            BackColor = UI.TaiTheme.Surface;
            BorderStyle = BorderStyle.None;
            MinimumSize = new Size(40, 23);
            // The outer pixel of the padding is the border drawn in OnPaint.
            Padding = new Padding(5, 4, 1, 4);
            SetStyle(ControlStyles.ResizeRedraw, true);
            inputTextBox.Enter += (sender, e) => Invalidate();
            inputTextBox.Leave += (sender, e) => Invalidate();
            Size = new Size(180, 23);

            Controls.Add(inputTextBox);
            Controls.Add(dropDownButton);

            ApplyFontToChildren();
        }

        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public override string Text
        {
            get
            {
                return inputTextBox == null ? base.Text : inputTextBox.Text;
            }
            set
            {
                string newValue = value ?? string.Empty;
                if (inputTextBox == null)
                {
                    base.Text = newValue;
                    return;
                }

                if (!string.Equals(inputTextBox.Text, newValue, StringComparison.Ordinal))
                    inputTextBox.Text = newValue;

                if (!string.Equals(base.Text, newValue, StringComparison.Ordinal))
                    base.Text = newValue;
            }
        }

        [Browsable(false)]
        public int ItemCount
        {
            get { return historyItems.Count; }
        }

        public void ClearItems()
        {
            if (historyItems.Count == 0)
                return;

            historyItems.Clear();
            historyMenuDirty = true;
        }

        public void AddItem(string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            historyItems.Add(value);
            historyMenuDirty = true;
        }

        public string GetItem(int index)
        {
            return historyItems[index];
        }

        public void MoveCaretToEnd()
        {
            inputTextBox.SelectionStart = inputTextBox.TextLength;
            inputTextBox.SelectionLength = 0;
        }

        public void FocusInput()
        {
            inputTextBox.Focus();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && historyMenu != null)
                historyMenu.Dispose();

            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Color edge = !Enabled ? UI.TaiTheme.Divider : ContainsFocus ? UI.TaiTheme.Accent : UI.TaiTheme.BorderStrong;
            using (Pen pen = new Pen(edge))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        private void DropDownButton_Paint(object sender, PaintEventArgs e)
        {
            int size = Math.Max(8, Math.Min(dropDownButton.Width, dropDownButton.Height) - 4);
            UI.TaiIcons.Draw(e.Graphics, UI.TaiIcon.ChevronDown,
                new Rectangle((dropDownButton.Width - size) / 2, (dropDownButton.Height - size) / 2, size, size),
                Enabled ? UI.TaiTheme.TextMuted : UI.TaiTheme.TextDisabled);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            ApplyFontToChildren();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            if (inputTextBox != null)
                inputTextBox.Enabled = Enabled;
            if (dropDownButton != null)
                dropDownButton.Enabled = Enabled;
        }

        private void InputTextBox_TextChanged(object sender, EventArgs e)
        {
            if (!string.Equals(base.Text, inputTextBox.Text, StringComparison.Ordinal))
                base.Text = inputTextBox.Text;
        }

        private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Alt && e.KeyCode == Keys.Down)
            {
                ShowHistoryMenu();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void InputTextBox_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            string value = inputTextBox.Text ?? string.Empty;
            if (value.Length == 0)
                return;

            int index = inputTextBox.GetCharIndexFromPosition(e.Location);
            if (index >= value.Length)
                index = value.Length - 1;

            if (!IsWordCharacter(value[index]))
            {
                inputTextBox.Select(index, 1);
                return;
            }

            int start = index;
            while (start > 0 && IsWordCharacter(value[start - 1]))
                start--;

            int end = index + 1;
            while (end < value.Length && IsWordCharacter(value[end]))
                end++;

            inputTextBox.Select(start, end - start);
        }

        private static bool IsWordCharacter(char value)
        {
            return char.IsLetterOrDigit(value) || value == '_';
        }

        private void DropDownButton_Click(object sender, EventArgs e)
        {
            ShowHistoryMenu();
        }

        private void ShowHistoryMenu()
        {
            if (!Enabled || IsDisposed || Disposing)
                return;
            if (historyMenu.Visible)
            {
                historyMenu.Close();
                return;
            }

            historyMenu.Font = Font;
            EnsureHistoryMenuItems();

            if (historyList.Items.Count == 0)
                return;

            UpdateHistoryMenuSize();
            int selected = historyItems.FindIndex(value => string.Equals(value, Text, StringComparison.Ordinal));
            historyList.SelectedIndex = selected >= 0 ? selected : 0;
            historyMenu.Show(this, new Point(0, Height));
            historyList.Focus();
        }

        private void EnsureHistoryMenuItems()
        {
            if (!historyMenuDirty)
                return;

            historyList.BeginUpdate();
            try
            {
                historyList.Items.Clear();
                foreach (string value in historyItems)
                    historyList.Items.Add(value);
            }
            finally
            {
                historyList.EndUpdate();
            }

            historyMenuDirty = false;
        }

        private void UpdateHistoryMenuSize()
        {
            int spacing = Math.Max(4, (int)Math.Round(6.0 * DeviceDpi / 96));
            historyList.ItemHeight = Math.Max(Font.Height + spacing, (int)Math.Round(22.0 * DeviceDpi / 96));
            int rows = Math.Max(1, Math.Min(MaximumVisibleHistoryItems, historyList.Items.Count));
            int width = Math.Max(Width, MinimumSize.Width);
            int height = rows * historyList.ItemHeight + SystemInformation.BorderSize.Height * 2;
            historyList.Size = new Size(width, height);
            historyHost.Size = historyList.Size;
            historyMenu.Size = historyList.Size;
        }

        private void ApplyFontToChildren()
        {
            if (inputTextBox != null)
                inputTextBox.Font = Font;
            if (dropDownButton != null)
                dropDownButton.Font = Font;
            if (historyMenu != null)
                historyMenu.Font = Font;
            if (historyList != null)
                historyList.Font = Font;
        }

        private void HistoryList_DrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index >= 0 && e.Index < historyList.Items.Count)
            {
                var bounds = e.Bounds;
                bounds.X += 4;
                bounds.Width = Math.Max(1, bounds.Width - 8);
                TextRenderer.DrawText(e.Graphics, (string)historyList.Items[e.Index], Font,
                    bounds, e.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            e.DrawFocusRectangle();
        }

        private void HistoryList_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !historyList.ClientRectangle.Contains(e.Location))
                return;

            int index = historyList.IndexFromPoint(e.Location);
            if (index >= 0) AcceptHistoryItem(index);
        }

        private void HistoryList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                AcceptHistoryItem(historyList.SelectedIndex);
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                historyMenu.Close(ToolStripDropDownCloseReason.Keyboard);
                e.Handled = e.SuppressKeyPress = true;
            }
        }

        private void AcceptHistoryItem(int index)
        {
            if (index < 0 || index >= historyList.Items.Count)
                return;
            Text = (string)historyList.Items[index];
            MoveCaretToEnd();
            historyMenu.Close(ToolStripDropDownCloseReason.ItemClicked);
        }

        private void HistoryMenu_Closed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            if (!IsDisposed && !Disposing && Enabled &&
                (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked || e.CloseReason == ToolStripDropDownCloseReason.Keyboard))
                FocusInput();
        }
    }
}
