using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace ADDIN.Commands
{
    // Independent of Bend Table/Kegaki preferences. A fresh profile automatically
    // exports and remembers the workbook bundled in the add-in.
    internal sealed class BendToolStorageSettings
    {
        private readonly string preferencePath;
        internal BendToolStorageSettings() : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ADDIN", "bend-tool-storage.json")) { }

        internal BendToolStorageSettings(string path) { preferencePath = Path.GetFullPath(path); }

        internal string DefaultExcelPath => Path.Combine(Path.GetDirectoryName(preferencePath),
            "BendTools", "DU_LIEU_DAO_CHAN.xlsx");

        internal string LoadPath() { return LoadPath(CancellationToken.None); }

        internal string LoadPath(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!File.Exists(preferencePath))
            {
                string bundled = BundledBendTools.EnsureExported(DefaultExcelPath, cancellation);
                BendToolExcelCatalog.Load(bundled, cancellation);
                // A concurrent user Save wins over first-run initialization.
                if (StorePath(bundled, cancellation, false)) return bundled;
            }
            if (new FileInfo(preferencePath).Length > 65536)
                throw new InvalidDataException("Cài đặt dao chấn quá lớn.");
            var data = new JavaScriptSerializer().Deserialize<Preference>(File.ReadAllText(preferencePath, Encoding.UTF8));
            if (data == null || data.Version != 1 || string.IsNullOrWhiteSpace(data.ExcelPath))
                throw new InvalidDataException("Không đọc được đường dẫn dao chấn đã lưu.");
            string selected = NormalizePath(data.ExcelPath);
            if (string.Equals(selected, DefaultExcelPath, StringComparison.OrdinalIgnoreCase))
                BundledBendTools.EnsureExported(selected, cancellation);
            return selected;
        }

        internal BendToolCatalogSnapshot SavePath(string path, CancellationToken cancellation)
        {
            string normalized = NormalizePath(path);
            // Validate before touching the remembered path. A failed candidate never replaces it.
            BendToolCatalogSnapshot snapshot = BendToolExcelCatalog.Load(normalized, cancellation);
            StorePath(normalized, cancellation, true);
            return snapshot;
        }

        private bool StorePath(string normalized, CancellationToken cancellation, bool replaceExisting)
        {
            cancellation.ThrowIfCancellationRequested();
            string directory = Path.GetDirectoryName(preferencePath);
            string temporary = preferencePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var hash = SHA256.Create())
            using (var mutex = new Mutex(false, @"Local\ADDIN_BendToolSettings_" +
                BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(preferencePath.ToUpperInvariant()))).Replace("-", "")))
            {
                bool held = false;
                try
                {
                    int waited;
                    try { waited = WaitHandle.WaitAny(new WaitHandle[] { mutex, cancellation.WaitHandle }, 5000); }
                    catch (AbandonedMutexException ex) { if (ex.MutexIndex != 0) throw; waited = 0; }
                    if (waited == 1) cancellation.ThrowIfCancellationRequested();
                    if (waited == WaitHandle.WaitTimeout) throw new IOException("Cài đặt dao đang được lưu; hãy thử lại.");
                    held = true;
                    if (!replaceExisting && File.Exists(preferencePath)) return false;
                    Directory.CreateDirectory(directory);
                    string json = new JavaScriptSerializer().Serialize(new Preference { Version = 1, ExcelPath = normalized });
                    using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                        file.Write(bytes, 0, bytes.Length);
                        file.Flush(true);
                    }
                    cancellation.ThrowIfCancellationRequested();
                    if (File.Exists(preferencePath)) File.Replace(temporary, preferencePath, null);
                    else File.Move(temporary, preferencePath);
                    return true;
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                    if (held) mutex.ReleaseMutex();
                }
            }
        }

        internal static string NormalizePath(string path)
        {
            string value = Environment.ExpandEnvironmentVariables((path ?? "").Trim().Trim('"'));
            bool drive = value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' && (value[2] == '\\' || value[2] == '/');
            bool unc = value.StartsWith(@"\\", StringComparison.Ordinal);
            if ((!drive && !unc) || value.StartsWith(@"\\?\") || value.StartsWith(@"\\.\"))
                throw new ArgumentException("Chọn đường dẫn đầy đủ đến file Excel dao chấn.");
            string normalized = Path.GetFullPath(value);
            if (!string.Equals(Path.GetExtension(normalized), ".xlsx", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("File dao chấn phải có đuôi .xlsx.");
            return normalized;
        }

        private sealed class Preference
        {
            public int Version { get; set; }
            public string ExcelPath { get; set; }
        }
    }
}
