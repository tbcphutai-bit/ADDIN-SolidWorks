using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public partial class LenhNoteTextBalloon
    {
        private readonly ISldWorks swApp;
        private readonly IWin32Window owner;
        private readonly HistoryTextBox noteComboBox;
        private readonly HistoryTextBox textComboBox;
        private readonly ComboBox balloonPropertyComboBox;
        private readonly string historyDirectory;
        private readonly Dictionary<string, SavedItemsSnapshot> savedItemsCache =
            new Dictionary<string, SavedItemsSnapshot>(StringComparer.OrdinalIgnoreCase);
        private static readonly CompareInfo JapaneseTextComparison = CultureInfo.GetCultureInfo("ja-JP").CompareInfo;
        private List<string> sortedTextSource;
        private List<string> sortedTextItems;

        private sealed class SavedItemsSnapshot
        {
            public string SourcePath;
            public DateTime LastWriteTimeUtc;
            public long Length;
            public List<string> Items;
        }

        private sealed class DisplayDimensionIdentityComparer : IEqualityComparer<DisplayDimension>
        {
            public bool Equals(DisplayDimension left, DisplayDimension right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(DisplayDimension dimension)
            {
                return RuntimeHelpers.GetHashCode(dimension);
            }
        }

        public LenhNoteTextBalloon(
            ISldWorks app,
            IWin32Window dialogOwner,
            HistoryTextBox noteList,
            HistoryTextBox textList,
            ComboBox balloonPropertyList)
            : this(app, dialogOwner, noteList, textList, balloonPropertyList,
                Path.Combine(System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.ApplicationData), "ADDIN"))
        {
        }

        internal LenhNoteTextBalloon(
            ISldWorks app,
            IWin32Window dialogOwner,
            HistoryTextBox noteList,
            HistoryTextBox textList,
            ComboBox balloonPropertyList,
            string savedHistoryDirectory)
        {
            swApp = app;
            owner = dialogOwner;
            noteComboBox = noteList;
            textComboBox = textList;
            balloonPropertyComboBox = balloonPropertyList;
            historyDirectory = savedHistoryDirectory;

            LoadSavedItems(noteComboBox, GetSavedNotePath());
            LoadSavedItems(textComboBox, GetSavedTextPath());
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

            List<DisplayDimension> displayDimensions = GetSelectedDisplayDimensions(model);
            if (displayDimensions.Count == 0)
            {
                MessageBox.Show("Hay chon dimension truoc. Giu Ctrl de chon nhieu dimension.", "Text", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<string> savedTexts = AddSavedItem(textComboBox, GetSavedTextPath(), text);
            bool changed = false;
            try
            {
                foreach (DisplayDimension displayDimension in displayDimensions)
                {
                    string currentSuffix = displayDimension.GetText((int)swDimensionTextParts_e.swDimensionTextSuffix);
                    string newSuffix = BuildDimensionSuffix(currentSuffix, text, savedTexts);
                    if (string.Equals(currentSuffix ?? "", newSuffix, StringComparison.Ordinal))
                        continue;

                    displayDimension.SetText((int)swDimensionTextParts_e.swDimensionTextSuffix, newSuffix);
                    changed = true;
                }
            }
            finally
            {
                // Refresh once for the whole batch, including changes already
                // applied if a later dimension fails. Preserve the selection.
                if (changed)
                    model.GraphicsRedraw2();
            }
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

        private List<DisplayDimension> GetSelectedDisplayDimensions(ModelDoc2 model)
        {
            List<DisplayDimension> dimensions = new List<DisplayDimension>();
            SelectionMgr selMgr = model.SelectionManager as SelectionMgr;
            if (selMgr == null)
                return dimensions;

            HashSet<DisplayDimension> seen = new HashSet<DisplayDimension>(new DisplayDimensionIdentityComparer());
            int count = selMgr.GetSelectedObjectCount2(-1);
            for (int i = 1; i <= count; i++)
            {
                object selectedObject = selMgr.GetSelectedObject6(i, -1);

                DisplayDimension displayDimension = selectedObject as DisplayDimension;
                if (displayDimension == null)
                {
                    Annotation annotation = selectedObject as Annotation;
                    displayDimension = annotation?.GetSpecificAnnotation() as DisplayDimension;
                }
                if (displayDimension != null && seen.Add(displayDimension))
                    dimensions.Add(displayDimension);
            }

            return dimensions;
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

        private string GetCurrentComboText(HistoryTextBox textBox)
        {
            return textBox == null ? "" : textBox.Text.Trim();
        }

        private void LoadSavedItems(HistoryTextBox comboBox, string path)
        {
            if (comboBox == null)
                return;

            List<string> items = ReadSavedItems(path);
            UpdateSavedItemList(comboBox, items);

            if (comboBox.ItemCount > 0)
                comboBox.Text = items[0];

            comboBox.MoveCaretToEnd();
        }

        private void UpdateSavedItemList(HistoryTextBox comboBox, List<string> items)
        {
            if (ReferenceEquals(comboBox, textComboBox))
            {
                // Only sort the displayed list; suffix replacement keeps its
                // existing history order. Cache this view until history changes.
                if (!ReferenceEquals(sortedTextSource, items))
                {
                    sortedTextItems = new List<string>(items);
                    sortedTextItems.Sort(CompareSavedTexts);
                    sortedTextSource = items;
                }
                items = sortedTextItems;
            }

            bool unchanged = comboBox.ItemCount == items.Count;
            for (int i = 0; unchanged && i < items.Count; i++)
                unchanged = string.Equals(comboBox.GetItem(i), items[i], StringComparison.Ordinal);

            if (unchanged)
                return;

            comboBox.ClearItems();
            foreach (string item in items)
                comboBox.AddItem(item);
        }

        private static int CompareSavedTexts(string left, string right)
        {
            int order = JapaneseTextComparison.Compare(left, right,
                CompareOptions.IgnoreCase | CompareOptions.IgnoreKanaType |
                CompareOptions.IgnoreWidth | CompareOptions.StringSort);
            return order != 0 ? order : string.Compare(left, right, StringComparison.Ordinal);
        }

        private List<string> AddSavedItem(HistoryTextBox comboBox, string path, string text)
        {
            List<string> items = ReadSavedItems(path);
            if (comboBox == null || string.IsNullOrWhiteSpace(text))
                return items;

            if (!ContainsText(items, text))
            {
                // Commit a new snapshot only after the file write succeeds.
                items = new List<string>(items);
                items.Add(text);
                WriteSavedItems(path, items);
            }

            UpdateSavedItemList(comboBox, items);
            comboBox.Text = text;
            comboBox.MoveCaretToEnd();
            return items;
        }

        private void DeleteSelectedItem(HistoryTextBox comboBox, string path)
        {
            if (comboBox == null)
                return;

            string text = comboBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return;

            List<string> items = ReadSavedItems(path);
            if (ContainsText(items, text))
            {
                items = new List<string>(items);
                items.RemoveAll(item => string.Equals(item, text, StringComparison.Ordinal));
                WriteSavedItems(path, items);
            }

            UpdateSavedItemList(comboBox, items);
            comboBox.Text = "";
        }

        private List<string> ReadSavedItems(string path)
        {
            FileInfo source = new FileInfo(path);
            bool legacyNote = false;
            if (!source.Exists && string.Equals(path, GetSavedNotePath(), StringComparison.OrdinalIgnoreCase))
            {
                FileInfo oldNote = new FileInfo(GetOldSavedNotePath());
                if (oldNote.Exists)
                {
                    source = oldNote;
                    legacyNote = true;
                }
            }

            DateTime lastWriteTimeUtc = source.Exists ? source.LastWriteTimeUtc : DateTime.MinValue;
            long length = source.Exists ? source.Length : -1;
            SavedItemsSnapshot snapshot;
            if (savedItemsCache.TryGetValue(path, out snapshot) &&
                string.Equals(snapshot.SourcePath, source.FullName, StringComparison.OrdinalIgnoreCase) &&
                snapshot.LastWriteTimeUtc == lastWriteTimeUtc && snapshot.Length == length)
                return snapshot.Items;

            List<string> items = new List<string>();
            if (source.Exists && legacyNote)
            {
                string oldText = File.ReadAllText(source.FullName).Trim();
                if (!string.IsNullOrWhiteSpace(oldText))
                    items.Add(oldText);
            }
            else if (source.Exists)
            {
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string line in File.ReadAllLines(source.FullName))
                {
                    string item = DecodeItem(line);
                    if (!string.IsNullOrWhiteSpace(item) && seen.Add(item))
                        items.Add(item);
                }
            }

            savedItemsCache[path] = new SavedItemsSnapshot
            {
                SourcePath = source.FullName,
                LastWriteTimeUtc = lastWriteTimeUtc,
                Length = length,
                Items = items
            };

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
            FileInfo writtenFile = new FileInfo(path);
            savedItemsCache[path] = new SavedItemsSnapshot
            {
                SourcePath = writtenFile.FullName,
                LastWriteTimeUtc = writtenFile.LastWriteTimeUtc,
                Length = writtenFile.Length,
                Items = items
            };
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
            return Path.Combine(historyDirectory, "component-notes.txt");
        }

        private string GetSavedTextPath()
        {
            return Path.Combine(historyDirectory, "component-texts.txt");
        }

        private string GetOldSavedNotePath()
        {
            return Path.Combine(historyDirectory, "component-note.txt");
        }
    }
}
