using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ADDIN.Commands;
using ADDIN.Helpers;
using ADDIN.UI;

namespace ADDIN
{
    // MARK command, the list that always shows the marks of the active Part, and the button that lights them up on the model.
    public partial class BomTaskPaneControl
    {
        private Button btnMarkFeature, btnShowMarks;
        private ListBox lstMarkInfo;
        private MarkFeatureCommand markFeatureCommand;

        private void CreateMarkFeatureControls()
        {
            btnMarkFeature = new ModernButton { Name = "btnMarkFeature", Text = "MARK (KHẮC DẤU)",
                Font = CreateUiFont(8.5F, FontStyle.Bold), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(254, 249, 195), ForeColor = Color.FromArgb(133, 77, 14) };
            btnMarkFeature.FlatAppearance.BorderColor = Color.FromArgb(253, 224, 71);
            btnShowMarks = new ModernButton { Name = "btnShowMarks", Text = "XEM MARK",
                Font = CreateUiFont(8.5F, FontStyle.Bold), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(254, 252, 232), ForeColor = Color.FromArgb(133, 77, 14), TabStop = false };
            btnShowMarks.FlatAppearance.BorderColor = Color.FromArgb(253, 224, 71);
            lstMarkInfo = new ListBox { Name = "lstMarkInfo", Font = CreateUiFont(8.25F, FontStyle.Regular),
                IntegralHeight = false, SelectionMode = SelectionMode.None, BorderStyle = BorderStyle.FixedSingle,
                ForeColor = Color.FromArgb(113, 63, 18), TabStop = false };
            panelModelCommands.Controls.Add(btnMarkFeature);
            panelModelCommands.Controls.Add(btnShowMarks);
            panelModelCommands.Controls.Add(lstMarkInfo);
            btnMarkFeature.Click += (sender, args) => ShowMarkFeaturePage();
            btnShowMarks.Click += (sender, args) => ShowMarksOnModel();
            UpdateMarkInfo();
        }

        // Returns the height used below `top`.
        private int LayoutMarkFeatureControls(int left, int top, int rowWidth)
        {
            if (btnMarkFeature == null || btnShowMarks == null || lstMarkInfo == null) return 0;
            const int showWidth = 96, gap = 6, infoHeight = 100;
            btnMarkFeature.SetBounds(left, top, Math.Max(80, rowWidth - showWidth - gap), 36);
            btnShowMarks.SetBounds(left + rowWidth - showWidth, top, showWidth, 36);
            lstMarkInfo.SetBounds(left, top + 36 + gap, rowWidth, infoHeight);
            return 36 + gap + infoHeight + gap;
        }

        private void AttachMarkHighlight()
        {
            try
            {
                MarkHighlightManager highlight = MarkHighlightManager.Attach(swApp);
                if (highlight != null)
                {
                    highlight.MarksChanged -= UpdateMarkInfo;
                    highlight.MarksChanged += UpdateMarkInfo;
                }
                UpdateMarkInfo();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[MARK] attach failed: " + ex.Message); }
        }

        private void ShowMarkFeaturePage()
        {
            try
            {
                EnsureNativeModelCommandAvailable();
                if (markFeatureCommand == null) markFeatureCommand = new MarkFeatureCommand(swApp);
                OpenNativeModelCommand(closed => new MarkFeaturePropertyManagerPage(swApp, markFeatureCommand, true, closed,
                    (changed, set) =>
                    {
                        // A rename does not rebuild the Part: read the marks again ourselves.
                        var highlight = MarkHighlightManager.Attach(swApp);
                        if (highlight != null) highlight.RequestRefresh();
                    }));
            }
            catch (Exception ex) { NativeModelCommandError("MARK", ex); }
        }

        private void ShowMarksOnModel()
        {
            try
            {
                EnsureNativeModelCommandAvailable();
                var highlight = MarkHighlightManager.Attach(swApp);
                if (highlight != null) highlight.ShowMarks();
            }
            catch (Exception ex) { NativeModelCommandError("MARK", ex); }
        }

        private void UpdateMarkInfo()
        {
            if (lstMarkInfo == null || lstMarkInfo.IsDisposed) return;
            var highlight = MarkHighlightManager.Current;
            IList<string> lines = highlight == null ? null : highlight.MarkLines;
            lstMarkInfo.BeginUpdate();
            lstMarkInfo.Items.Clear();
            lstMarkInfo.Items.Add(lines == null ? "MARK: mở một Part để xem"
                : lines.Count == 0 ? "MARK: part này chưa có" : "MARK: " + lines.Count + " feature");
            if (lines != null)
                foreach (string line in lines) lstMarkInfo.Items.Add("  " + line);
            lstMarkInfo.EndUpdate();
            lstMarkInfo.BackColor = lines != null && lines.Count > 0 ? Color.FromArgb(254, 249, 195) : Color.FromArgb(248, 250, 252);
        }
    }
}
