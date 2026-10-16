using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;

namespace ADDIN.Commands
{
    internal static class BundledBendStandards
    {
        internal static string DefaultDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ADDIN", "BendTables", "StandardExcel");

        internal static void EnsureExported(string directory)
        {
            string root = BendTableStorageSettings.NormalizeDirectory(directory);
            string marker = Path.Combine(root, ".addin-bundled-standards-v1");
            using (var mutex = new Mutex(false, @"Local\ADDIN_BendExport_" + BendTableDatabase.Hash(
                Encoding.UTF8.GetBytes(root.ToUpperInvariant()))))
            {
                bool held = false;
                try
                {
                    try { held = mutex.WaitOne(5000); } catch (AbandonedMutexException) { held = true; }
                    if (!held) throw new IOException("Bo Excel mac dinh dang duoc xuat; hay thu lai.");
                    // A deleted file remains missing after the first export.
                    if (File.Exists(marker)) return;
                    Directory.CreateDirectory(root);
                    string prefix = root.TrimEnd('\\') + "\\";
                    using (Stream stream = typeof(BundledBendStandards).Assembly.GetManifestResourceStream("ADDIN.BendTables.DefaultExcel.zip"))
                    {
                        if (stream == null) throw new InvalidDataException("Khong tim thay bo Excel mac dinh trong add-in.");
                        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                            foreach (ZipArchiveEntry entry in zip.Entries)
                            {
                                if (entry.Name.Length == 0) continue;
                                string target = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', '\\')));
                                if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                    throw new InvalidDataException("Duong dan trong bo Excel mac dinh khong hop le.");
                                if (File.Exists(target)) continue;
                                Directory.CreateDirectory(Path.GetDirectoryName(target));
                                string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                                try
                                {
                                    using (Stream input = entry.Open())
                                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                                        input.CopyTo(output);
                                    File.Move(temporary, target);
                                }
                                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                            }
                    }
                    File.WriteAllText(marker, "Bundled Excel standards exported.", new UTF8Encoding(false));
                }
                finally { if (held) mutex.ReleaseMutex(); }
            }
        }
    }
}
