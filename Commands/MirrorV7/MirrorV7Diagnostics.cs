using System;
namespace ADDIN.Commands.MirrorV7
{
    public static class MirrorV7Diagnostics
    {
        public static void Log(string message) { ADDIN.Commands.CreateMirrorPartPackage.LogDebug("[V7] " + (message ?? "")); }
        public static void LogPhase(string phase, string message) { Log("[" + (phase ?? "UNKNOWN") + "] " + (message ?? "")); }
        public static void LogException(string context, Exception ex) { Log("[EXCEPTION] " + (context ?? "") + " :: " + (ex == null ? "<null>" : ex.ToString())); }
    }
}
