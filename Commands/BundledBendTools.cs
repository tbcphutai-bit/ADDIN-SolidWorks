using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ADDIN.Commands
{
    internal static class BundledBendTools
    {
        private const string ResourceName = "ADDIN.BendTools.DefaultExcel.xlsx";

        internal static string EnsureExported(string path, CancellationToken cancellation)
        {
            string target = BendToolStorageSettings.NormalizePath(path);
            cancellation.ThrowIfCancellationRequested();
            // Preserve user edits and replacement workbooks at the default path.
            if (File.Exists(target)) return target;
            using (var hash = SHA256.Create())
            using (var mutex = new Mutex(false, @"Local\ADDIN_BendToolsExport_" +
                BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(target.ToUpperInvariant()))).Replace("-", "")))
            {
                bool held = false;
                string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    int waited;
                    try { waited = WaitHandle.WaitAny(new WaitHandle[] { mutex, cancellation.WaitHandle }, 5000); }
                    catch (AbandonedMutexException ex) { if (ex.MutexIndex != 0) throw; waited = 0; }
                    if (waited == 1) cancellation.ThrowIfCancellationRequested();
                    if (waited == WaitHandle.WaitTimeout) throw new IOException("Đang tạo dữ liệu dao chấn mặc định; hãy thử lại.");
                    held = true;
                    if (File.Exists(target)) return target;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (Stream input = typeof(BundledBendTools).Assembly.GetManifestResourceStream(ResourceName))
                    {
                        if (input == null) throw new InvalidDataException("Không tìm thấy dữ liệu dao chấn mặc định trong add-in.");
                        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            input.CopyTo(output);
                            output.Flush(true);
                        }
                    }
                    cancellation.ThrowIfCancellationRequested();
                    File.Move(temporary, target);
                    return target;
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                    if (held) mutex.ReleaseMutex();
                }
            }
        }
    }
}
