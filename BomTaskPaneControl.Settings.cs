using System.Windows.Forms;
using ADDIN.UI;

namespace ADDIN
{
    public partial class BomTaskPaneControl
    {
        private void EnsureSettingsTab()
        {
            if (tabBom.TabPages.ContainsKey("tabSettings")) return;
            var settings = new AddinSettingsControl();
            settings.SetSldWorks(swApp);
            var page = new TabPage("Settings") { Name = "tabSettings",
                BackColor = TaiTheme.Page, UseVisualStyleBackColor = false, Padding = Padding.Empty };
            page.Controls.Add(settings);
            tabBom.TabPages.Add(page);
            tabBom.SelectedIndexChanged += (sender, e) =>
            {
                if (tabBom.SelectedTab == page) settings.ReloadIfUnchanged();
            };
        }
    }
}
