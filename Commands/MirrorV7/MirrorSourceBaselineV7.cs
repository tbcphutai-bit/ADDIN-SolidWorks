using System;
using System.IO;
using System.Security.Cryptography;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.MirrorV7
{
    public sealed class MirrorSourceBaselineV7
    {
        public string SourcePath { get; private set; }
        public string ConfigurationName { get; private set; }
        public string FileHash { get; private set; }
        public int FeatureCount { get; private set; }
        public bool SaveFlag { get; private set; }

        public static MirrorSourceBaselineV7 Capture(ModelDoc2 source)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (source.GetType() != (int)swDocumentTypes_e.swDocPART)
                throw new InvalidOperationException("PHASE6A requires a Part document.");
            string path = source.GetPathName();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new InvalidOperationException("Save the source Part before PHASE6A.");
            bool dirty = source.GetSaveFlag();
            if (dirty) throw new InvalidOperationException("Source Part has unsaved changes. Save it before PHASE6A.");
            Configuration config = source.ConfigurationManager.ActiveConfiguration;
            if (config == null) throw new InvalidOperationException("Cannot read source configuration.");
            return new MirrorSourceBaselineV7
            {
                SourcePath = Path.GetFullPath(path),
                ConfigurationName = config.Name,
                FeatureCount = source.GetFeatureCount(),
                SaveFlag = dirty,
                FileHash = ComputeHash(path)
            };
        }

        public void AssertUnchanged(ModelDoc2 source)
        {
            if (source == null) throw new InvalidOperationException("Source document is unavailable.");
            string currentPath = source.GetPathName();
            Configuration config = source.ConfigurationManager.ActiveConfiguration;
            int featureCountAfter = source.GetFeatureCount();
            bool saveFlagAfter = source.GetSaveFlag();
            string hashAfter = ComputeHash(SourcePath);
            MirrorV7Diagnostics.Log("[PHASE6A][SOURCE_SAFETY] featureCountBefore=" + FeatureCount +
                " featureCountAfter=" + featureCountAfter + " saveFlagBefore=" + SaveFlag +
                " saveFlagAfter=" + saveFlagAfter + " fileHashUnchanged=" +
                string.Equals(FileHash, hashAfter, StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(currentPath) ||
                !string.Equals(SourcePath, Path.GetFullPath(currentPath), StringComparison.OrdinalIgnoreCase) ||
                config == null || !string.Equals(ConfigurationName, config.Name, StringComparison.Ordinal) ||
                FeatureCount != featureCountAfter || SaveFlag != saveFlagAfter ||
                !string.Equals(FileHash, hashAfter, StringComparison.Ordinal))
                throw new InvalidOperationException("PHASE6A SAFETY FAILURE: Source changed.");
        }

        internal static string ComputeHash(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
    }
}
