using System;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.MirrorV7
{
    public sealed class MirrorWorkingCopyV7
    {
        public string RunId { get; private set; }
        public string WorkingPath { get; private set; }
        public ModelDoc2 Document { get; private set; }
        public int OpenErrors { get; private set; }
        public int OpenWarnings { get; private set; }

        public static MirrorWorkingCopyV7 Create(ISldWorks app, MirrorSourceBaselineV7 baseline)
        {
            if (app == null) throw new ArgumentNullException("app");
            if (baseline == null) throw new ArgumentNullException("baseline");
            string runId = Guid.NewGuid().ToString("N");
            string directory = Path.Combine(Path.GetTempPath(), "MirrorV7", "Phase6A", runId);
            Directory.CreateDirectory(directory);
            string workingPath = Path.Combine(directory,
                Path.GetFileNameWithoutExtension(baseline.SourcePath) + "__V7_" + runId + ".SLDPRT");
            if (string.Equals(workingPath, baseline.SourcePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Working copy cannot overwrite source.");
            File.Copy(baseline.SourcePath, workingPath, false);
            if (!string.Equals(baseline.FileHash, MirrorSourceBaselineV7.ComputeHash(workingPath), StringComparison.Ordinal))
                throw new InvalidOperationException("Copied file does not match source baseline.");
            MirrorV7Diagnostics.Log("[PHASE6A][COPY_CREATED] runId=" + runId + " source=\"" + baseline.SourcePath + "\" copy=\"" + workingPath + "\"");
            int errors = 0, warnings = 0;
            ModelDoc2 copy = app.OpenDoc6(workingPath, (int)swDocumentTypes_e.swDocPART,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent, baseline.ConfigurationName,
                ref errors, ref warnings);
            MirrorV7Diagnostics.Log("[PHASE6A][COPY_OPEN] errors=" + errors + " warnings=" + warnings + " returnedDocument=" + (copy != null));
            if (copy == null || errors != 0) throw new InvalidOperationException("Cannot open working copy. errors=" + errors);
            if (copy.GetType() != (int)swDocumentTypes_e.swDocPART ||
                !string.Equals(Path.GetFullPath(copy.GetPathName()), workingPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Opened document is not the expected working copy.");
            Configuration config = copy.ConfigurationManager.ActiveConfiguration;
            if (config == null || !string.Equals(config.Name, baseline.ConfigurationName, StringComparison.Ordinal))
                throw new InvalidOperationException("Working-copy configuration does not match source.");
            return new MirrorWorkingCopyV7 { RunId = runId, WorkingPath = workingPath, Document = copy, OpenErrors = errors, OpenWarnings = warnings };
        }
    }
}
