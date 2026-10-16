using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace ADDIN.Commands
{
    // The preference stays in the user profile even when the cache moves.
    // A new checker reads it once, keeping a running CHECK's location stable.
    internal sealed class BendTableStorageSettings
    {
        internal const string DatabaseFileName = "database.json";
        private readonly string preferencesPath;
        internal string DefaultDirectory { get; private set; }
        internal string DefaultSourceDirectory { get; private set; }

        internal BendTableStorageSettings() : this(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ADDIN", "bend-table-storage.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ADDIN", "BendTables")) { }

        internal BendTableStorageSettings(string preferenceFile, string defaultDirectory)
            : this(preferenceFile, defaultDirectory, BundledBendStandards.DefaultDirectory) { }

        internal BendTableStorageSettings(string preferenceFile, string defaultDirectory, string defaultSourceDirectory)
        {
            preferencesPath = Path.GetFullPath(preferenceFile);
            DefaultDirectory = NormalizeDirectory(defaultDirectory);
            DefaultSourceDirectory = NormalizeDirectory(defaultSourceDirectory);
        }

        internal static string GetConfiguredDatabasePath()
        {
            var settings = new BendTableStorageSettings();
            return settings.GetDatabasePath(settings.LoadCustomDirectory());
        }

        internal string GetDatabasePath(string customDirectory)
        {
            return Path.Combine(customDirectory == null
                ? DefaultDirectory : NormalizeDirectory(customDirectory), DatabaseFileName);
        }

        internal string LoadCustomDirectory() { return LoadSelection().CustomDatabaseDirectory; }

        internal BendTableStorageSelection LoadSelection()
        {
            try
            {
                BendTableStoragePreference data = null;
                if (File.Exists(preferencesPath))
                {
                    if (new FileInfo(preferencesPath).Length > 65536)
                        throw new InvalidDataException("File Settings qua lon.");
                    data = Serializer().Deserialize<BendTableStoragePreference>(File.ReadAllText(preferencesPath, Encoding.UTF8));
                    if (data == null || (data.Version != 1 && data.Version != 2 && data.Version != 3))
                        throw new InvalidDataException("Dinh dang Settings khong hop le.");
                }
                string custom = data?.DatabaseDirectory == null ? null : NormalizeDirectory(data.DatabaseDirectory);
                string customSource = data?.CustomSourceDirectory == null ? null : NormalizeDirectory(data.CustomSourceDirectory);
                BendStandardProfile profile = BendStandardProfile.Validate(data?.StandardProfile, false);
                // Explicit file assignments own their source location. Migrate
                // version 3 preferences whose displayed folder disagreed with it.
                if (profile != null) customSource = profile.SourceDirectory;
                bool useCustom = data != null && data.UseCustomStandards;
                if (useCustom && profile == null && string.IsNullOrWhiteSpace(customSource))
                    throw new InvalidDataException("Chua chon file cho bo chuan rieng.");
                return new BendTableStorageSelection {
                    CustomDatabaseDirectory = custom,
                    DatabasePath = GetDatabasePath(custom),
                    DefaultSourceDirectory = DefaultSourceDirectory,
                    CustomSourceDirectory = customSource,
                    StandardProfile = profile,
                    UseCustomStandards = useCustom,
                    Tolerance = string.IsNullOrWhiteSpace(data?.Tolerance) ? "0.01" : data.Tolerance,
                    Scale = string.IsNullOrWhiteSpace(data?.Scale) ? "1:1" : data.Scale,
                    ExportExt = string.IsNullOrWhiteSpace(data?.ExportExt) ? "dxf" : data.ExportExt,
                    QtyType = string.IsNullOrWhiteSpace(data?.QtyType) ? "数量+Qty" : data.QtyType
                };
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Khong doc duoc Settings Bend Table. Mo tab Settings va luu lai lua chon. " + ex.Message, ex);
            }
        }

        internal BendTableStorageSelection SaveSelection(BendStandardProfile profile, string customDirectory)
        { return SaveSelection(profile, customDirectory, profile != null); }

        internal BendTableStorageSelection SaveSelection(BendStandardProfile profile, string customDirectory, bool useCustom)
        { return SaveSelection(profile, customDirectory, useCustom, null, "0.01", "1:1", "dxf", "数量+Qty"); }

        internal BendTableStorageSelection SaveSelection(
            BendStandardProfile profile,
            string customDatabaseDirectory,
            bool useCustomStandards,
            string customSourceDirectory,
            string tolerance,
            string scale,
            string exportExt,
            string qtyType)
        {
            if (useCustomStandards && profile == null && string.IsNullOrWhiteSpace(customSourceDirectory))
                throw new InvalidDataException("Hay chon bo Excel hoac thu muc rieng truoc khi luu.");
            string source = string.IsNullOrWhiteSpace(customSourceDirectory) ? null : NormalizeDirectory(customSourceDirectory);
            BendStandardProfile valid = BendStandardProfile.Validate(profile, useCustomStandards);
            if (valid != null)
            {
                if (source != null && !string.Equals(source, valid.SourceDirectory, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Thu muc Excel khac voi bo file da gan. Hay chon lai file cho thu muc moi.");
                source = valid.SourceDirectory;
            }
            if (useCustomStandards)
            {
                if (!Directory.Exists(source)) throw new DirectoryNotFoundException("Thu muc Excel khong ton tai: " + source);
                if (valid == null) valid = BendStandardProfile.Validate(BendStandardProfile.FromFolder(source), true);
            }
            string custom = SavePreference(customDatabaseDirectory, valid, useCustomStandards,
                source, tolerance, scale, exportExt, qtyType);
            return new BendTableStorageSelection {
                CustomDatabaseDirectory = custom,
                DatabasePath = GetDatabasePath(custom),
                DefaultSourceDirectory = DefaultSourceDirectory,
                CustomSourceDirectory = source,
                StandardProfile = valid,
                UseCustomStandards = useCustomStandards,
                Tolerance = tolerance ?? "0.01",
                Scale = scale ?? "1:1",
                ExportExt = exportExt ?? "dxf",
                QtyType = qtyType ?? "数量+Qty"
            };
        }

        internal string Save(string customDirectory)
        {
            BendTableStorageSelection selection;
            try { selection = LoadSelection(); } catch (InvalidDataException) { selection = new BendTableStorageSelection(); }
            return SavePreference(customDirectory, selection.StandardProfile, selection.UseCustomStandards,
                selection.CustomSourceDirectory, selection.Tolerance, selection.Scale, selection.ExportExt, selection.QtyType);
        }

        private string SavePreference(string customDirectory, BendStandardProfile profile, bool useCustom,
            string customSourceDirectory, string tolerance, string scale, string exportExt, string qtyType)
        {
            string custom = customDirectory == null ? null : NormalizeDirectory(customDirectory);
            string directory = custom ?? DefaultDirectory;
            // Check the chosen folder before changing the remembered location.
            if (File.Exists(directory)) throw new ArgumentException("Hay chon thu muc, khong chon mot file.");
            Directory.CreateDirectory(directory);
            string probe = Path.Combine(directory, ".addin-bend-write-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 1, FileOptions.DeleteOnClose)) { }

            string temporary = preferencesPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var mutex = new Mutex(false, MutexName()))
            {
                bool held = false;
                try
                {
                    try { held = mutex.WaitOne(5000); }
                    catch (AbandonedMutexException) { held = true; }
                    if (!held) throw new IOException("Settings dang duoc luu boi phien khac; hay thu lai.");
                    Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath));
                    var preference = new BendTableStoragePreference {
                        Version = 3,
                        DatabaseDirectory = custom,
                        StandardProfile = profile,
                        UseCustomStandards = useCustom,
                        CustomSourceDirectory = customSourceDirectory,
                        Tolerance = tolerance ?? "0.01",
                        Scale = scale ?? "1:1",
                        ExportExt = exportExt ?? "dxf",
                        QtyType = qtyType ?? "数量+Qty"
                    };
                    File.WriteAllText(temporary, Serializer().Serialize(preference), new UTF8Encoding(false));
                    if (File.Exists(preferencesPath)) File.Replace(temporary, preferencesPath, null);
                    else File.Move(temporary, preferencesPath);
                }
                finally
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); }
                    finally { if (held) mutex.ReleaseMutex(); }
                }
            }
            return custom;
        }

        internal static string NormalizeDirectory(string directory)
        {
            string value = Environment.ExpandEnvironmentVariables((directory ?? "").Trim());
            if (value.Length == 0) throw new ArgumentException("Hay chon thu muc luu database.");
            value = value.Replace('/', '\\');
            bool drive = value.Length >= 3 && char.IsLetter(value[0])
                && value[1] == ':' && value[2] == '\\';
            bool network = value.StartsWith(@"\\", StringComparison.Ordinal)
                && !value.StartsWith(@"\\?\", StringComparison.Ordinal)
                && !value.StartsWith(@"\\.\", StringComparison.Ordinal);
            if (!drive && !network)
                throw new ArgumentException("Hay nhap duong dan thu muc day du, vi du D:\\BendTableDatabase.");
            string fullPath = Path.GetFullPath(value);
            if (network)
            {
                string[] pieces = fullPath.TrimStart('\\').Split('\\');
                if (pieces.Length < 2 || pieces[0].Length == 0 || pieces[1].Length == 0)
                    throw new ArgumentException("Duong dan mang can co ten may va thu muc chia se.");
            }
            string root = Path.GetPathRoot(fullPath);
            return fullPath.Length > root.Length ? fullPath.TrimEnd('\\') : fullPath;
        }

        private string MutexName()
        {
            using (var hash = SHA256.Create())
                return @"Local\ADDIN_BendTableStorage_" + BitConverter.ToString(
                    hash.ComputeHash(Encoding.UTF8.GetBytes(preferencesPath.ToUpperInvariant()))).Replace("-", "");
        }

        private static JavaScriptSerializer Serializer()
        { return new JavaScriptSerializer { MaxJsonLength = 65536, RecursionLimit = 10 }; }
    }

    internal sealed class BendTableStoragePreference
    {
        public int Version { get; set; }
        public string DatabaseDirectory { get; set; }
        public string CustomSourceDirectory { get; set; }
        public BendStandardProfile StandardProfile { get; set; }
        public bool UseCustomStandards { get; set; }
        public string Tolerance { get; set; }
        public string Scale { get; set; }
        public string ExportExt { get; set; }
        public string QtyType { get; set; }
    }
}
