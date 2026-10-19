using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace ADDIN.Commands
{
    internal static class BendNativeCaptureDiagnostics
    {
        private static readonly object Gate = new object();
        // Opt-in local diagnostics only. No change to native call order, Min
        // evaluation, exception semantics, timeout, or source CAD.
        internal static void Failure(Exception error, string phase)
            => Append(phase, error?.ToString());
        internal static void Stage(string phase) => Append(phase, null);
        private static void Append(string phase, string detail)
        {
            try
            {
                string root = Environment.GetEnvironmentVariable("ADDIN_BEND_CAPTURE_DIAGNOSTIC_DIRECTORY");
                if (string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root)) return;
                string text = DateTimeOffset.UtcNow.ToString("o") + " phase=" + phase + " thread=" + Thread.CurrentThread.ManagedThreadId +
                    " apartment=" + Thread.CurrentThread.GetApartmentState() + "\r\n" + detail + "\r\n";
                lock (Gate)
                {
                    Directory.CreateDirectory(root); string path = Path.Combine(root, "PRIVATE-native-capture-failures.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 2097152) return;
                    File.AppendAllText(path, text);
                }
            }
            catch (Exception diagnostic) { Debug.WriteLine("Capture diagnostic unavailable: " + diagnostic.GetType().Name); }
        }
    }
}
