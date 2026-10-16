using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ADDIN.Commands;

namespace ADDIN.UI
{
    internal sealed partial class AddinSettingsControl
    {
        private BendStandardProfile standardProfile;
        private string savedStandardsSignature;

        private void LoadStandardSelection(BendTableStorageSelection selection)
        {
            standardProfile = selection.StandardProfile;
            savedStandardsSignature = StandardsSignature();
        }

        private string StandardsSignature()
        { return BendTableDatabase.Serializer().Serialize(standardProfile); }

        private bool StandardsChanged()
        { return !string.Equals(savedStandardsSignature, StandardsSignature(), StringComparison.Ordinal); }

        private void StandardModeChanged()
        {
            if (loading) return;
            // Changing modes never discards the remembered private assignments.
            loading = true;
            try
            {
                excelFolderBox.Text = useCustomBendStandards.Checked
                    ? customExcelDirectory ?? standardProfile?.SourceDirectory ?? ""
                    : settings.DefaultSourceDirectory;
            }
            finally { loading = false; }
            RefreshExcelSummary();
            DraftChanged();
        }

        private void ExcelDirectoryChanged()
        {
            if (loading || !useCustomBendStandards.Checked) return;
            customExcelDirectory = excelFolderBox.Text.Trim();
            if (standardProfile != null)
            {
                string normalized = null;
                try { normalized = BendTableStorageSettings.NormalizeDirectory(customExcelDirectory); }
                catch (ArgumentException) { }
                if (!string.Equals(normalized, standardProfile.SourceDirectory, StringComparison.OrdinalIgnoreCase))
                    standardProfile = null;
            }
            DraftChanged();
        }

        private void BrowseExcelFolder()
        {
            if (!useCustomBendStandards.Checked) return;
            using (var browser = new FolderBrowserDialog
            {
                Description = "Chọn thư mục chứa bộ Excel chuẩn riêng",
                ShowNewFolderButton = false
            })
            {
                if (Directory.Exists(excelFolderBox.Text)) browser.SelectedPath = excelFolderBox.Text;
                if (browser.ShowDialog(this) != DialogResult.OK) return;
                excelFolderBox.Text = browser.SelectedPath;
                RefreshExcelSummary();
            }
        }

        private void OpenExcelFolderInExplorer()
        {
            try
            {
                string folder = BendTableStorageSettings.NormalizeDirectory(excelFolderBox.Text);
                if (!useCustomBendStandards.Checked) BundledBendStandards.EnsureExported(settings.DefaultSourceDirectory);
                if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Thư mục không tồn tại: " + folder);
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (Exception ex) { ShowStatus("Không thể mở thư mục: " + ex.Message, true); }
        }

        private void ResetExcelToDefault()
        {
            useCustomBendStandards.Checked = false;
            DraftChanged();
        }

        private void OpenBendStandardFilesDialog()
        {
            if (!useCustomBendStandards.Checked) return;
            BendStandardProfile initial = standardProfile;
            if (initial == null)
            {
                try { initial = BendStandardProfile.FromFolder(excelFolderBox.Text); }
                catch { /* Arbitrary filenames are assigned in the dialog. */ }
            }
            using (var dialog = new BendStandardFilesForm(initial))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                standardProfile = dialog.Selection;
                customExcelDirectory = standardProfile.SourceDirectory;
                loading = true;
                try { excelFolderBox.Text = customExcelDirectory; }
                finally { loading = false; }
                RefreshExcelSummary();
                DraftChanged();
            }
        }

        // This summary lists assigned files only. Successful import is reported
        // separately by the explicit Scan button after every workbook is read.
        internal static bool ScanExcelFolder(string folderPath, BendStandardProfile profile,
            out int materialCount, out int grooveCount, out string detailMessage)
        {
            materialCount = grooveCount = 0;
            try
            {
                BendStandardProfile assigned = profile == null ? BendStandardProfile.FromFolder(folderPath)
                    : BendStandardProfile.Validate(profile, false);
                materialCount = assigned.MaterialFiles.Count;
                grooveCount = assigned.GrooveFiles.Count;
                int missing = assigned.MaterialFiles.Select(f => f.FilePath)
                    .Concat(assigned.GrooveFiles.Select(f => f.FilePath)).Count(p => !File.Exists(p));
                detailMessage = string.Format("Đã gán: {0}/4 nhóm vật liệu | {1} bảng bào{2}",
                    materialCount, grooveCount, missing == 0 ? "" : " | Thiếu " + missing + " file");
                return missing == 0;
            }
            catch (Exception ex)
            {
                detailMessage = "Chưa có bộ chuẩn hợp lệ: " + ex.Message;
                return false;
            }
        }

        private void RefreshExcelSummary()
        {
            try
            {
                BendStandardProfile profile = standardProfile;
                if (!useCustomBendStandards.Checked)
                {
                    BundledBendStandards.EnsureExported(settings.DefaultSourceDirectory);
                    profile = BendStandardProfile.CreateDefault(settings.DefaultSourceDirectory);
                }
                int materials, grooves;
                string message;
                bool ok = ScanExcelFolder(excelFolderBox.Text, profile, out materials, out grooves, out message);
                lblBendSummary.Text = message;
                lblBendSummary.ForeColor = ok ? TaiTheme.TextMuted : TaiTheme.Warning;
            }
            catch (Exception ex)
            {
                lblBendSummary.Text = ex.Message;
                lblBendSummary.ForeColor = TaiTheme.Danger;
            }
        }

        private void ScanAndSyncExcel()
        {
            try
            {
                string folder = BendTableStorageSettings.NormalizeDirectory(excelFolderBox.Text);
                BendStandardProfile profile = null;
                if (useCustomBendStandards.Checked)
                    profile = standardProfile == null ? BendStandardProfile.FromFolder(folder)
                        : BendStandardProfile.Validate(standardProfile, false);
                else BundledBendStandards.EnsureExported(settings.DefaultSourceDirectory);
                string cacheDir = chkCustomCache.Checked ? cacheFolderBox.Text.Trim() : null;
                var db = new BendTableDatabase(folder, settings.GetDatabasePath(cacheDir), profile);
                int imported = db.SynchronizeStandards();
                RefreshExcelSummary();
                ShowStatus("✓ Đã đọc và đồng bộ " + imported + " file Excel vào database.", false, true);
            }
            catch (Exception ex)
            {
                lblBendSummary.Text = "Chưa đồng bộ đủ bộ chuẩn: " + ex.Message;
                lblBendSummary.ForeColor = TaiTheme.Danger;
                ShowStatus("Lỗi đọc Excel: " + ex.Message, true);
            }
        }
    }
}
