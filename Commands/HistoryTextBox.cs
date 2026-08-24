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
        private readonly ContextMenuStrip historyMenu;
        private readonly List<string> historyItems;

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
            dropDownButton.Text = "\u25BC";
            dropDownButton.Width = 22;
            dropDownButton.Click += DropDownButton_Click;

            historyMenu = new ContextMenuStrip();
            historyMenu.ShowImageMargin = false;

            BackColor = SystemColors.Window;
            BorderStyle = BorderStyle.FixedSingle;
            MinimumSize = new Size(40, 23);
            Padding = new Padding(3, 3, 0, 2);
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
            historyItems.Clear();
        }

        public void AddItem(string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            historyItems.Add(value);
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
            historyMenu.Font = Font;
            historyMenu.Items.Clear();
            foreach (string value in historyItems)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(value);
                item.Tag = value;
                item.Click += HistoryItem_Click;
                historyMenu.Items.Add(item);
            }

            if (historyMenu.Items.Count == 0)
                return;

            historyMenu.Show(this, new Point(0, Height));
        }

        private void ApplyFontToChildren()
        {
            if (inputTextBox != null)
                inputTextBox.Font = Font;
            if (dropDownButton != null)
                dropDownButton.Font = Font;
            if (historyMenu != null)
                historyMenu.Font = Font;
        }

        private void HistoryItem_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem item = sender as ToolStripMenuItem;
            if (item == null)
                return;

            Text = item.Tag as string ?? item.Text;
            FocusInput();
            MoveCaretToEnd();
        }
    }
}
