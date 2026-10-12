using System;
using System.Drawing;
using System.Windows.Forms;

namespace ADDIN.UI
{
    // The task pane's look for the small dialogs a command opens before it runs.
    // The dialog keeps its Designer layout; only colours, fonts and the roles
    // of its buttons come from here.
    public static class TaiDialog
    {
        // primary is the button that runs the command. Pass Danger when it deletes.
        public static void Apply(Form form, Button primary, ModernButtonKind primaryKind = ModernButtonKind.Primary)
        {
            if (form == null) return;
            form.BackColor = TaiTheme.Page;
            foreach (Control control in form.Controls) Style(control, primary, primaryKind, false);
        }

        private static void Style(Control control, Button primary, ModernButtonKind primaryKind, bool inHeader)
        {
            // A plain panel docked to the top is the dialog's heading band.
            if (control.GetType() == typeof(Panel) && control.Dock == DockStyle.Top)
            {
                control.BackColor = TaiTheme.Surface;
                control.Paint += PaintHeadingRule;
                foreach (Control child in control.Controls) Style(child, primary, primaryKind, true);
                return;
            }

            Label label = control as Label;
            ModernButton button = control as ModernButton;
            if (label != null)
            {
                bool strong = label.Font.Bold;
                bool heading = inHeader ? strong : label.Font.SizeInPoints >= 11F;
                label.BackColor = Color.Transparent;
                label.Font = heading ? TaiTheme.Title : inHeader ? TaiTheme.Small : strong ? TaiTheme.BodyStrong : TaiTheme.Body;
                label.ForeColor = heading || (strong && !inHeader) ? TaiTheme.Text : TaiTheme.TextMuted;
            }
            else if (control is CheckBox || control is RadioButton)
            {
                control.Font = control.Font.Bold ? TaiTheme.BodyStrong : TaiTheme.Body;
                control.ForeColor = TaiTheme.Text;
                control.BackColor = Color.Transparent;
            }
            else if (button != null)
            {
                if (ReferenceEquals(button, primary)) button.Kind = primaryKind;
                else if (button.DialogResult == DialogResult.Cancel) button.Kind = ModernButtonKind.Standard;
            }
            else if (control is GroupBox) TaiTheme.StyleGroupAsCard((GroupBox)control);
            else if (control is NumericUpDown) control.Font = TaiTheme.Body;
            else if (control is TextBoxBase && !(control is RichTextBox))
            {
                control.Font = TaiTheme.Body;
                TaiInputFrame.Attach((TextBoxBase)control);
            }
            else if (control is ComboBox)
            {
                control.Font = TaiTheme.Body;
                TaiComboFrame.Attach((ComboBox)control);
            }
            // An up-down control is one box to the user: its inner text box keeps the system frame.
            if (control is UpDownBase) return;
            foreach (Control child in control.Controls) Style(child, primary, primaryKind, inHeader);
        }

        private static void PaintHeadingRule(object sender, PaintEventArgs e)
        {
            Control band = (Control)sender;
            using (Pen pen = new Pen(TaiTheme.Border)) e.Graphics.DrawLine(pen, 0, band.Height - 1, band.Width, band.Height - 1);
        }
    }
}
