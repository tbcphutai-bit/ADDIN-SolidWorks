using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public class LenhNoteTextBalloon
    {
        private readonly ISldWorks swApp;
        private readonly IWin32Window owner;
        private readonly ComboBox noteComboBox;
        private readonly ComboBox textComboBox;
        private readonly ComboBox balloonPropertyComboBox;

        public LenhNoteTextBalloon(
            ISldWorks app,
            IWin32Window dialogOwner,
            ComboBox noteList,
            ComboBox textList,
            ComboBox balloonPropertyList)
        {
            swApp = app;
            owner = dialogOwner;
            noteComboBox = noteList;
            textComboBox = textList;
            balloonPropertyComboBox = balloonPropertyList;

            LoadSavedItems(noteComboBox, GetSavedNotePath());
            LoadSavedItems(textComboBox, GetSavedTextPath());
            ConfigureEditableComboBox(noteComboBox);
            ConfigureEditableComboBox(textComboBox);
            InitializeBalloonPropertyOptions();
        }

        public void InsertNote()
        {
            ModelDoc2 model = swApp?.ActiveDoc as ModelDoc2;
            if (model == null ||
                model.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                MessageBox.Show("Hay mo Drawing truoc.", "Note", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string noteText = GetCurrentComboText(noteComboBox);
            if (string.IsNullOrWhiteSpace(noteText))
            {
                noteText = PromptForText("Note");
                if (string.IsNullOrWhiteSpace(noteText))
                    return;
            }

            AddSavedItem(noteComboBox, GetSavedNotePath(), noteText);

            object noteObject = model.InsertNote(noteText);
            if (noteObject == null)
            {
                MessageBox.Show("Khong tao duoc Note.", "Note", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            model.GraphicsRedraw2();
        }

        public void DeleteSelectedNote()
        {
            DeleteSelectedItem(noteComboBox, GetSavedNotePath());
        }

        public void InsertText()
        {
            ModelDoc2 model = swApp?.ActiveDoc as ModelDoc2;
            if (model == null ||
                model.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                MessageBox.Show("Hay mo Drawing truoc.", "Text", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string text = GetCurrentComboText(textComboBox);
            if (string.IsNullOrWhiteSpace(text))
            {
                text = PromptForText("Text");
                if (string.IsNullOrWhiteSpace(text))
                    return;
            }

            AddSavedItem(textComboBox, GetSavedTextPath(), text);

            DisplayDimension displayDimension = GetSelectedDisplayDimension(model);
            if (displayDimension == null)
            {
                MessageBox.Show("Hay chon 1 dimension truoc.", "Text", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<string> savedTexts = ReadSavedItems(GetSavedTextPath());
            string currentSuffix = displayDimension.GetText((int)swDimensionTextParts_e.swDimensionTextSuffix);
            string newSuffix = BuildDimensionSuffix(currentSuffix, text, savedTexts);
            displayDimension.SetText((int)swDimensionTextParts_e.swDimensionTextSuffix, newSuffix);
            model.GraphicsRedraw2();
        }

        public void DeleteSelectedText()
        {
            DeleteSelectedItem(textComboBox, GetSavedTextPath());
        }

        public void InsertBalloon()
        {
            ModelDoc2 model = swApp?.ActiveDoc as ModelDoc2;
            if (model == null ||
                model.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                MessageBox.Show("Hay mo Drawing truoc.", "Balloon", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string propertyName = GetCurrentComboText(balloonPropertyComboBox);
            if (string.IsNullOrWhiteSpace(propertyName))
                propertyName = "部品番号";

            if (!HasBalloonTargetSelection(model))
            {
                MessageBox.Show("Hay chon canh/mat/diem cua component, khong chon nguyen drawing view.", "Balloon", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string propertyValue = GetSelectedBalloonPropertyValue(model, propertyName);
            if (propertyValue == null)
            {
                MessageBox.Show("Hay chon object cua component truoc.", "Balloon", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(propertyValue))
            {
                MessageBox.Show("Khong co gia tri property: " + propertyName, "Balloon", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int balloonStyle = GetBalloonStyle(propertyName);
            int customPropertyText = (int)swBalloonTextContent_e.swBalloonTextCustomProperties;
            int customText = (int)swBalloonTextContent_e.swBalloonTextCustom;
            int balloonSize = (int)swBalloonFit_e.swBF_Tightest;
            string propertyLink = BuildComponentPropertyLink(propertyName);

            BalloonOptions balloonOptions =
                model.Extension.CreateBalloonOptions() as BalloonOptions;
            if (balloonOptions == null)
            {
                MessageBox.Show("Khong tao duoc Balloon options.", "Balloon", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            balloonOptions.Style = balloonStyle;
            balloonOptions.Size = balloonSize;
            balloonOptions.UpperTextContent = customPropertyText;
            balloonOptions.UpperText = propertyLink;
            balloonOptions.LowerTextContent = customText;
            balloonOptions.LowerText = "";
            balloonOptions.ShowQuantity = false;
            balloonOptions.Layername = "部品表";

            Note balloon = model.Extension.InsertBOMBalloon2(balloonOptions) as Note;

            if (balloon == null)
            {
                MessageBox.Show("Khong tao duoc Balloon.", "Balloon", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            balloon.SetBomBalloonText(
                customPropertyText,
                propertyLink,
                customText,
                "");

            Annotation balloonAnnotation = balloon.GetAnnotation() as Annotation;
            if (balloonAnnotation != null)
            {
                model.ClearSelection2(true);
                balloonAnnotation.Select2(false, 0);
            }

            model.Extension.EditBalloonProperties2(
                balloonStyle,
                balloonSize,
                customPropertyText,
                propertyLink,
                customText,
                "",
                0,
                false,
                0,
                "部品表",
                0.01016);

            model.GraphicsRedraw2();
        }

        private string BuildComponentPropertyLink(string propertyName)
        {
            return "$PRPMODEL:\"" + propertyName + "\"";
        }

        private int GetBalloonStyle(string propertyName)
        {
            if (string.Equals(propertyName, "合番", StringComparison.Ordinal))
                return (int)swBalloonStyle_e.swBS_None;

            return (int)swBalloonStyle_e.swBS_Circular;
        }

        private void InitializeBalloonPropertyOptions()
        {
            if (balloonPropertyComboBox == null)
                return;

            balloonPropertyComboBox.DropDownStyle = ComboBoxStyle.DropDownList;

            AddComboOption(balloonPropertyComboBox, "部品番号");
            AddComboOption(balloonPropertyComboBox, "合番");

            if (string.IsNullOrWhiteSpace(balloonPropertyComboBox.Text))
                balloonPropertyComboBox.Text = "部品番号";
        }

        private void AddComboOption(ComboBox comboBox, string value)
        {
            foreach (object item in comboBox.Items)
            {
                if (string.Equals(item?.ToString(), value, StringComparison.Ordinal))
                    return;
            }

            comboBox.Items.Add(value);
        }

        private bool HasBalloonTargetSelection(ModelDoc2 model)
        {
            SelectionMgr selMgr = model.SelectionManager as SelectionMgr;
            if (selMgr == null)
                return false;

            int count = selMgr.GetSelectedObjectCount2(-1);
            for (int i = 1; i <= count; i++)
            {
                if (GetSelectedComponent(selMgr, i) != null)
                    return true;

                object selectedObject = selMgr.GetSelectedObject6(i, -1);
                if (selectedObject == null)
                    continue;

                if (selectedObject is SolidWorks.Interop.sldworks.View)
                    continue;

                return true;
            }

            return false;
        }

        private string GetSelectedBalloonPropertyValue(ModelDoc2 model, string propertyName)
        {
            SelectionMgr selMgr = model.SelectionManager as SelectionMgr;
            if (selMgr == null)
                return null;

            int count = selMgr.GetSelectedObjectCount2(-1);
            for (int i = 1; i <= count; i++)
            {
                Component2 component = GetSelectedComponent(selMgr, i);
                if (component != null)
                    return GetComponentPropertyValue(component, propertyName);

                SolidWorks.Interop.sldworks.View view = GetSelectedDrawingView(selMgr, i);
                if (view != null)
                    return GetViewReferencedPropertyValue(view, propertyName);
            }

            return null;
        }

        private Component2 GetSelectedComponent(ModelDoc2 model)
        {
            SelectionMgr selMgr = model.SelectionManager as SelectionMgr;
            if (selMgr == null)
                return null;

            int count = selMgr.GetSelectedObjectCount2(-1);
            for (int i = 1; i <= count; i++)
            {
                Component2 component = GetSelectedComponent(selMgr, i);
                if (component != null)
                    return component;
            }

            return null;
        }

        private Component2 GetSelectedComponent(SelectionMgr selMgr, int index)
        {
            Component2 component =
                selMgr.GetSelectedObjectsComponent4(index, -1) as Component2 ??
                selMgr.GetSelectedObjectsComponent3(index, -1) as Component2;
            if (component != null)
                return component;

            DrawingComponent drawingComponent =
                selMgr.GetSelectedObject6(index, -1) as DrawingComponent;
            component = drawingComponent?.Component as Component2;
            if (component != null)
                return component;

            Entity entity = selMgr.GetSelectedObject6(index, -1) as Entity;
            if (entity != null)
            {
                component =
                    entity.GetComponent() as Component2 ??
                    entity.IGetComponent2() as Component2;
                if (component != null)
                    return component;
            }

            return null;
        }

        private SolidWorks.Interop.sldworks.View GetSelectedDrawingView(SelectionMgr selMgr, int index)
        {
            SolidWorks.Interop.sldworks.View view =
                selMgr.GetSelectedObject6(index, -1) as SolidWorks.Interop.sldworks.View;
            if (view != null)
                return view;

            try
            {
                return selMgr.GetSelectedObjectsDrawingView2(index, -1) as SolidWorks.Interop.sldworks.View;
            }
            catch
            {
                return null;
            }
        }

        private string GetComponentPropertyValue(Component2 component, string propertyName)
        {
            string configName = component.ReferencedConfiguration;

            string value = GetPropertyFromManager(
                component.CustomPropertyManager[configName] as CustomPropertyManager,
                propertyName);
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            ModelDoc2 model = component.GetModelDoc2() as ModelDoc2;
            if (model == null)
                return "";

            value = GetPropertyFromManager(
                model.Extension.get_CustomPropertyManager(configName ?? ""),
                propertyName);
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            return GetPropertyFromManager(
                model.Extension.get_CustomPropertyManager(""),
                propertyName);
        }

        private string GetViewReferencedPropertyValue(SolidWorks.Interop.sldworks.View view, string propertyName)
        {
            if (view == null)
                return null;

            ModelDoc2 referencedModel = view.ReferencedDocument as ModelDoc2;
            if (referencedModel == null)
                return "";

            string configName = "";
            try
            {
                configName = view.ReferencedConfiguration;
            }
            catch
            {
                configName = "";
            }

            return GetModelPropertyValue(referencedModel, configName, propertyName);
        }

        private string GetModelPropertyValue(ModelDoc2 model, string configName, string propertyName)
        {
            if (model == null)
                return "";

            string value = GetPropertyFromManager(
                model.Extension.get_CustomPropertyManager(configName ?? ""),
                propertyName);
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            return GetPropertyFromManager(
                model.Extension.get_CustomPropertyManager(""),
                propertyName);
        }

        private string GetPropertyFromManager(CustomPropertyManager propMgr, string propertyName)
        {
            if (propMgr == null)
                return "";

            string valOut;
            string resolvedVal;
            bool wasResolved;
            bool linkToProp;

            propMgr.Get6(propertyName, true, out valOut, out resolvedVal, out wasResolved, out linkToProp);

            return string.IsNullOrWhiteSpace(resolvedVal) ? valOut : resolvedVal;
        }

        private DisplayDimension GetSelectedDisplayDimension(ModelDoc2 model)
        {
            SelectionMgr selMgr = model.SelectionManager as SelectionMgr;
            if (selMgr == null)
                return null;

            int count = selMgr.GetSelectedObjectCount2(-1);
            for (int i = 1; i <= count; i++)
            {
                object selectedObject = selMgr.GetSelectedObject6(i, -1);

                DisplayDimension displayDimension = selectedObject as DisplayDimension;
                if (displayDimension != null)
                    return displayDimension;

                Annotation annotation = selectedObject as Annotation;
                displayDimension = annotation?.GetSpecificAnnotation() as DisplayDimension;
                if (displayDimension != null)
                    return displayDimension;
            }

            return null;
        }

        private string BuildDimensionSuffix(string currentSuffix, string text, List<string> savedTexts)
        {
            string cleanSuffix = currentSuffix ?? "";

            foreach (string savedText in savedTexts)
            {
                if (string.IsNullOrWhiteSpace(savedText))
                    continue;

                cleanSuffix = cleanSuffix.Replace(savedText, "");
            }

            if (cleanSuffix.Contains(text))
                return cleanSuffix;

            return cleanSuffix + text;
        }

        private string PromptForText(string title)
        {
            using (Form form = new Form())
            using (TextBox textBox = new TextBox())
            using (Button okButton = new Button())
            using (Button cancelButton = new Button())
            {
                form.Text = title;
                form.Width = 380;
                form.Height = 145;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MinimizeBox = false;
                form.MaximizeBox = false;

                textBox.Left = 12;
                textBox.Top = 12;
                textBox.Width = 350;
                textBox.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;

                okButton.Text = "OK";
                okButton.Left = 200;
                okButton.Top = 50;
                okButton.Width = 75;
                okButton.DialogResult = DialogResult.OK;

                cancelButton.Text = "Cancel";
                cancelButton.Left = 285;
                cancelButton.Top = 50;
                cancelButton.Width = 75;
                cancelButton.DialogResult = DialogResult.Cancel;

                form.Controls.Add(textBox);
                form.Controls.Add(okButton);
                form.Controls.Add(cancelButton);
                form.AcceptButton = okButton;
                form.CancelButton = cancelButton;

                return form.ShowDialog(owner) == DialogResult.OK
                    ? textBox.Text.Trim()
                    : "";
            }
        }

        private string GetCurrentComboText(ComboBox comboBox)
        {
            return comboBox == null ? "" : comboBox.Text.Trim();
        }

        private void ConfigureEditableComboBox(ComboBox comboBox)
        {
            if (comboBox == null)
                return;

            comboBox.DropDownStyle = ComboBoxStyle.DropDown;
            comboBox.Enter += EditableComboBox_Enter;
            comboBox.MouseUp += EditableComboBox_MouseUp;
            comboBox.Leave += EditableComboBox_Leave;
            comboBox.LostFocus += EditableComboBox_LostFocus;
            comboBox.DropDownClosed += EditableComboBox_DropDownClosed;
            comboBox.SelectionChangeCommitted += EditableComboBox_SelectionChangeCommitted;
            ClearComboTextSelection(comboBox);
        }

        private void EditableComboBox_Enter(object sender, EventArgs e)
        {
            ComboBox comboBox = sender as ComboBox;
            SetComboCaretDeferred(comboBox, (comboBox?.Text ?? "").Length);
        }

        private void EditableComboBox_MouseUp(object sender, MouseEventArgs e)
        {
            ComboBox comboBox = sender as ComboBox;
            if (comboBox == null || e.Button != MouseButtons.Left)
                return;

            // Double click giu nguyen vung chu do Windows vua chon.
            if (e.Clicks > 1)
                return;

            int dropDownButtonLeft = comboBox.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
            if (e.X >= dropDownButtonLeft)
                return;

            SetComboCaretDeferred(comboBox, GetComboCaretIndexAtMouse(comboBox, e.X));
        }

        private void EditableComboBox_Leave(object sender, EventArgs e)
        {
            ClearComboTextSelectionAfterFocusChange(sender as ComboBox);
        }

        private void EditableComboBox_LostFocus(object sender, EventArgs e)
        {
            ClearComboTextSelectionAfterFocusChange(sender as ComboBox);
        }

        private void EditableComboBox_DropDownClosed(object sender, EventArgs e)
        {
            ClearComboTextSelection(sender as ComboBox);
        }

        private void EditableComboBox_SelectionChangeCommitted(object sender, EventArgs e)
        {
            ClearComboTextSelection(sender as ComboBox);
        }

        private void ClearComboTextSelection(ComboBox comboBox)
        {
            if (comboBox == null || comboBox.DropDownStyle == ComboBoxStyle.DropDownList)
                return;

            try
            {
                comboBox.SelectionStart = (comboBox.Text ?? "").Length;
                comboBox.SelectionLength = 0;
            }
            catch
            {
            }
        }

        private void ClearComboTextSelectionAfterFocusChange(ComboBox comboBox)
        {
            ClearComboTextSelection(comboBox);

            if (comboBox == null || comboBox.IsDisposed || !comboBox.IsHandleCreated)
                return;

            try
            {
                comboBox.BeginInvoke((MethodInvoker)delegate
                {
                    if (!comboBox.IsDisposed && !comboBox.Focused)
                        ClearComboTextSelection(comboBox);
                });
            }
            catch
            {
            }
        }

        private void SetComboCaretDeferred(ComboBox comboBox, int caretIndex)
        {
            if (comboBox == null || comboBox.IsDisposed || !comboBox.IsHandleCreated)
                return;

            try
            {
                comboBox.BeginInvoke((MethodInvoker)delegate
                {
                    if (comboBox.IsDisposed)
                        return;

                    string text = comboBox.Text ?? "";
                    int index = Math.Max(0, Math.Min(caretIndex, text.Length));
                    comboBox.SelectionStart = index;
                    comboBox.SelectionLength = 0;
                });
            }
            catch
            {
            }
        }

        private int GetComboCaretIndexAtMouse(ComboBox comboBox, int mouseX)
        {
            string text = comboBox?.Text ?? "";
            if (text.Length == 0)
                return 0;

            int targetX = Math.Max(0, mouseX - 3);
            int previousWidth = 0;
            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

            for (int i = 1; i <= text.Length; i++)
            {
                int currentWidth = TextRenderer.MeasureText(
                    text.Substring(0, i),
                    comboBox.Font,
                    Size.Empty,
                    flags).Width;

                if (targetX < (previousWidth + currentWidth) / 2)
                    return i - 1;

                previousWidth = currentWidth;
            }

            return text.Length;
        }

        private void LoadSavedItems(ComboBox comboBox, string path)
        {
            if (comboBox == null)
                return;

            comboBox.Items.Clear();

            List<string> items = ReadSavedItems(path);
            foreach (string item in items)
                comboBox.Items.Add(item);

            if (comboBox.Items.Count > 0)
                comboBox.Text = comboBox.Items[0].ToString();

            ClearComboTextSelection(comboBox);
        }

        private void AddSavedItem(ComboBox comboBox, string path, string text)
        {
            if (comboBox == null || string.IsNullOrWhiteSpace(text))
                return;

            List<string> items = ReadSavedItems(path);
            if (!ContainsText(items, text))
                items.Add(text);

            WriteSavedItems(path, items);
            LoadSavedItems(comboBox, path);
            comboBox.Text = text;
            ClearComboTextSelection(comboBox);
        }

        private void DeleteSelectedItem(ComboBox comboBox, string path)
        {
            if (comboBox == null)
                return;

            string text = comboBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return;

            List<string> items = ReadSavedItems(path);
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (string.Equals(items[i], text, StringComparison.Ordinal))
                    items.RemoveAt(i);
            }

            WriteSavedItems(path, items);
            LoadSavedItems(comboBox, path);
            if (!ContainsText(items, text))
                comboBox.Text = "";
        }

        private List<string> ReadSavedItems(string path)
        {
            List<string> items = new List<string>();
            if (!File.Exists(path))
            {
                string oldNotePath = GetOldSavedNotePath();
                if (path == GetSavedNotePath() && File.Exists(oldNotePath))
                {
                    string oldText = File.ReadAllText(oldNotePath).Trim();
                    if (!string.IsNullOrWhiteSpace(oldText))
                        items.Add(oldText);
                }

                return items;
            }

            string[] lines = File.ReadAllLines(path);
            foreach (string line in lines)
            {
                string item = DecodeItem(line);
                if (!string.IsNullOrWhiteSpace(item) && !ContainsText(items, item))
                    items.Add(item);
            }

            return items;
        }

        private void WriteSavedItems(string path, List<string> items)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            List<string> lines = new List<string>();
            foreach (string item in items)
            {
                if (!string.IsNullOrWhiteSpace(item))
                    lines.Add(EncodeItem(item));
            }

            File.WriteAllLines(path, lines.ToArray());
        }

        private bool ContainsText(List<string> items, string text)
        {
            foreach (string item in items)
            {
                if (string.Equals(item, text, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private string EncodeItem(string text)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        }

        private string DecodeItem(string text)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(text));
            }
            catch
            {
                return text;
            }
        }

        private string GetSavedNotePath()
        {
            return Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "ADDIN",
                "component-notes.txt");
        }

        private string GetSavedTextPath()
        {
            return Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "ADDIN",
                "component-texts.txt");
        }

        private string GetOldSavedNotePath()
        {
            return Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "ADDIN",
                "component-note.txt");
        }
    }
}
