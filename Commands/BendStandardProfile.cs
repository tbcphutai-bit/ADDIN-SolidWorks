using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ADDIN.Commands
{
    // Files acquire their roles from the user's explicit assignment. Their
    // names are display labels; groove stock remaining is separate metadata.
    internal sealed class BendStandardProfile
    {
        public string SourceDirectory { get; set; }
        public List<BendStandardMaterialFile> MaterialFiles { get; set; } = new List<BendStandardMaterialFile>();
        public List<BendStandardGrooveFile> GrooveFiles { get; set; } = new List<BendStandardGrooveFile>();

        internal static readonly string[] MaterialGroups = { "ST", "AL", "SUS", "CU" };
        internal static readonly string[] MaterialNames = { "BD_Kiku_ST_2020.xlsx", "BD_Kiku_AL_2020.xlsx",
            "BD_Kiku_SUS_2020.xlsx", "BD_Kiku_RBs,Bs,Cu_2020.xlsx" };

        internal static BendStandardProfile CreateDefault(string directory)
        {
            var profile = new BendStandardProfile { SourceDirectory = BendTableStorageSettings.NormalizeDirectory(directory) };
            for (int i = 0; i < MaterialGroups.Length; i++)
                profile.MaterialFiles.Add(new BendStandardMaterialFile
                { Group = MaterialGroups[i], FilePath = Path.Combine(profile.SourceDirectory, MaterialNames[i]) });
            using (Stream stream = typeof(BendStandardProfile).Assembly.GetManifestResourceStream("ADDIN.BendTables.Default.json"))
            {
                if (stream == null) throw new InvalidDataException("Khong tim thay danh muc bang chuan.");
                BendTableDatabaseDocument catalog;
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    catalog = BendTableDatabase.Serializer().Deserialize<BendTableDatabaseDocument>(reader.ReadToEnd());
                foreach (BendTableDatabaseEntry entry in catalog.Tables.Where(t => string.IsNullOrEmpty(Path.GetDirectoryName(t.RelativePath))
                    && t.Data != null && t.Data.Blocks.Any(b => b.IsGroove)))
                {
                    double? remaining = KegakiChecker.ReadCanonicalGrooveRemainingThickness(entry.RelativePath);
                    if (!remaining.HasValue) throw new InvalidDataException("Thieu muc bao trong danh muc chuan.");
                    profile.GrooveFiles.Add(new BendStandardGrooveFile
                    { FilePath = Path.Combine(profile.SourceDirectory, entry.RelativePath), RemainingThicknessMm = remaining.Value });
                }
            }
            return profile;
        }

        internal static BendStandardProfile Validate(BendStandardProfile profile, bool readFiles)
        {
            if (profile == null) return null;
            var result = new BendStandardProfile { SourceDirectory = BendTableStorageSettings.NormalizeDirectory(profile.SourceDirectory) };
            var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BendStandardMaterialFile file in profile.MaterialFiles ?? new List<BendStandardMaterialFile>())
            {
                if (file == null || !MaterialGroups.Contains((file.Group ?? "").ToUpperInvariant()) || !groups.Add(file.Group))
                    throw new InvalidDataException("Nhom vat lieu trong bo chuan bi trung/khong hop le.");
                if (string.IsNullOrWhiteSpace(file.FilePath)) continue;
                string path = NormalizeFile(file.FilePath);
                paths.Add(path);
                if (readFiles) ValidateWorkbook(path, false);
                result.MaterialFiles.Add(new BendStandardMaterialFile { Group = file.Group.ToUpperInvariant(), FilePath = path });
            }
            foreach (BendStandardGrooveFile file in profile.GrooveFiles ?? new List<BendStandardGrooveFile>())
            {
                if (file == null || !BendValueMath.IsFinite(file.RemainingThicknessMm) || file.RemainingThicknessMm <= 0)
                    throw new InvalidDataException("Bang bao can co be day con lai sau bao > 0 mm.");
                string path = NormalizeFile(file.FilePath);
                if (!paths.Add(path)) throw new InvalidDataException("File bang bao dang bi gan trung.");
                if (readFiles) ValidateWorkbook(path, true);
                result.GrooveFiles.Add(new BendStandardGrooveFile { FilePath = path, RemainingThicknessMm = file.RemainingThicknessMm });
            }
            if (paths.Count == 0) throw new InvalidDataException("Hay chon it nhat mot file Excel chuan.");
            result.MaterialFiles = result.MaterialFiles.OrderBy(f => f.Group, StringComparer.OrdinalIgnoreCase).ToList();
            result.GrooveFiles = result.GrooveFiles.OrderBy(f => f.RemainingThicknessMm)
                .ThenBy(f => f.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
            return result;
        }

        // A folder alone can identify the known default roles. Arbitrary names
        // need an explicit assignment; never guess a material from its values.
        internal static BendStandardProfile FromFolder(string directory)
        {
            string root = BendTableStorageSettings.NormalizeDirectory(directory);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Thu muc Excel khong ton tai: " + root);
            BendStandardProfile profile = CreateDefault(root);
            profile.MaterialFiles.RemoveAll(f => !File.Exists(f.FilePath));
            profile.GrooveFiles.RemoveAll(f => !File.Exists(f.FilePath));
            if (profile.MaterialFiles.Count + profile.GrooveFiles.Count == 0)
                throw new InvalidDataException("Chua co file duoc gan nhom. Bam 'Gan chi tiet' de chon file co ten rieng.");
            return Validate(profile, false);
        }

        private static string NormalizeFile(string path)
        {
            // Reuse absolute-path validation; existence is checked only at Save
            // and CHECK, so a disappeared source can still be repaired in Settings.
            string full = BendTableStorageSettings.NormalizeDirectory(path);
            if (!string.Equals(Path.GetExtension(full), ".xlsx", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("File chuan phai la Excel .xlsx: " + full);
            return full;
        }

        private static void ValidateWorkbook(string path, bool groove)
        {
            BendTableWorkbookData workbook = BendTableXlsxImporter.Read(BendTableDatabase.ReadSource(path), path);
            if (!workbook.IsValid) throw new InvalidDataException(Path.GetFileName(path) + ": " + workbook.Error);
            if (workbook.Blocks.Any(b => b.IsGroove != groove))
                throw new InvalidDataException(Path.GetFileName(path) + (groove ? ": chua phai bang bao ranh." : ": hay gan bang bao trong muc Bang bao."));
        }

        internal string ResolveCanonicalName(string canonical)
        {
            for (int i = 0; i < MaterialNames.Length; i++)
                if (string.Equals(canonical, MaterialNames[i], StringComparison.OrdinalIgnoreCase))
                    return MaterialFiles.FirstOrDefault(f => string.Equals(f.Group, MaterialGroups[i], StringComparison.OrdinalIgnoreCase))?.FilePath;
            double? remaining = KegakiChecker.ReadCanonicalGrooveRemainingThickness(canonical);
            if (remaining.HasValue)
            {
                var matches = GrooveFiles.Where(f => Math.Abs(f.RemainingThicknessMm - remaining.Value) < 1e-9).ToList();
                return matches.Count == 1 ? matches[0].FilePath : null;
            }
            return null;
        }

        internal double? RemainingForPath(string path)
        {
            var matches = GrooveFiles.Where(f => string.Equals(f.FilePath, path, StringComparison.OrdinalIgnoreCase)).ToList();
            return matches.Count == 1 ? (double?)matches[0].RemainingThicknessMm : null;
        }
    }

    internal sealed class BendStandardMaterialFile
    {
        public string Group { get; set; }
        public string FilePath { get; set; }
    }
    internal sealed class BendStandardGrooveFile
    {
        public string FilePath { get; set; }
        public double RemainingThicknessMm { get; set; }
    }
    internal sealed class BendTableStorageSelection
    {
        internal string CustomDatabaseDirectory { get; set; }
        internal string DatabasePath { get; set; }
        internal string DefaultSourceDirectory { get; set; }
        internal string CustomSourceDirectory { get; set; }
        internal BendStandardProfile StandardProfile { get; set; }
        internal bool UseCustomStandards { get; set; }
        internal BendStandardProfile EffectiveStandardProfile => !UseCustomStandards ? null
            : StandardProfile ?? BendStandardProfile.FromFolder(CustomSourceDirectory);
        internal string EffectiveSourceDirectory => !UseCustomStandards ? DefaultSourceDirectory
            : StandardProfile?.SourceDirectory ?? CustomSourceDirectory;
        internal string Tolerance { get; set; } = "0.01";
        internal string Scale { get; set; } = "1:1";
        internal string ExportExt { get; set; } = "dxf";
        internal string QtyType { get; set; } = "数量+Qty";
    }
}

