using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace ADDIN.Commands
{
    // Excel is authoritative. JSON is a persistent import cache, never an override
    // for a changed, missing or unreadable source. Each CHECK gets one consistent
    // snapshot per table; the next CHECK hashes the source again, including copies
    // that preserve the original file's length and timestamp.
    internal sealed class BendTableDatabase
    {
        internal const string SourceDirectory =
            @"C:\sw共有ライブラリ\テーブル\菊川ベンドテーブル_170905";
        private const int SchemaVersion = 1;
        private static readonly object StoreGate = new object();
        private readonly string root;
        private readonly string databasePath;
        private readonly string cacheIdentity;
        private readonly BendStandardProfile standards;
        private readonly bool customStandards;
        internal string SourceDirectoryPath => root;
        private readonly Dictionary<string, BendTableWorkbookData> session =
            new Dictionary<string, BendTableWorkbookData>(StringComparer.OrdinalIgnoreCase);
        private BendTableDatabaseDocument document;
        private bool needsSave;

        internal IEnumerable<string> GetGrooveStandardPaths()
        { return standards.GrooveFiles.Select(f => f.FilePath).ToArray(); }

        internal double? GetGrooveRemainingThickness(string path)
        { return standards.RemainingForPath(path); }

        internal string ResolveStandardPath(string canonicalName)
        {
            string path = standards.ResolveCanonicalName(canonicalName);
            if (path != null || customStandards) return path;
            return Path.Combine(root, Path.GetFileName(canonicalName));
        }

        internal BendTableDatabase() : this(new BendTableStorageSettings().LoadSelection()) { }

        internal BendTableDatabase(BendTableStorageSelection selection)
            : this(selection.EffectiveSourceDirectory,
                selection.DatabasePath, selection.EffectiveStandardProfile)
        {
            if (!selection.UseCustomStandards) BundledBendStandards.EnsureExported(root);
        }

        internal BendTableDatabase(string sourceDirectory, string storePath)
            : this(sourceDirectory, storePath, null) { }

        internal BendTableDatabase(string sourceDirectory, string storePath, BendStandardProfile profile)
        {
            root = BendTableStorageSettings.NormalizeDirectory(sourceDirectory);
            databasePath = Path.GetFullPath(storePath);
            customStandards = profile != null;
            standards = profile == null ? BendStandardProfile.CreateDefault(root) : BendStandardProfile.Validate(profile, false);
            cacheIdentity = profile == null ? root : root + "|profile=" + Hash(Encoding.UTF8.GetBytes(Serializer().Serialize(standards)));
        }

        internal int SynchronizeStandards()
        {
            var failures = new List<string>();
            var files = standards.MaterialFiles.Select(f => new { f.FilePath, Groove = false })
                .Concat(standards.GrooveFiles.Select(f => new { f.FilePath, Groove = true }))
                .GroupBy(f => f.FilePath, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            foreach (var file in files)
            {
                BendTableWorkbookData data = Read(file.FilePath);
                if (!data.IsValid) failures.Add(Path.GetFileName(file.FilePath) + ": " + data.Error);
                else if (data.Blocks.Any(b => b.IsGroove != file.Groove))
                    failures.Add(Path.GetFileName(file.FilePath) + ": Loai bang khong khop voi nhom da gan.");
            }
            if (failures.Count > 0)
                throw new InvalidDataException("Chua dong bo du bo chuan (" + failures.Count + " file loi). "
                    + string.Join("; ", failures));
            if (files.Count == 0) throw new InvalidDataException("Chua chon file Excel chuan.");
            return files.Count;
        }

        internal BendTableWorkbookData Read(string path)
        {
            string normalized;
            try { normalized = Path.GetFullPath(path); }
            catch { return Failure(path, "Duong dan Excel chuan khong hop le."); }
            BendTableWorkbookData cached;
            if (session.TryGetValue(normalized, out cached)) return cached;
            try
            {
                string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                bool assigned = standards.MaterialFiles.Any(f => string.Equals(f.FilePath, normalized, StringComparison.OrdinalIgnoreCase))
                    || standards.GrooveFiles.Any(f => string.Equals(f.FilePath, normalized, StringComparison.OrdinalIgnoreCase));
                if (customStandards ? !assigned : !normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Excel chuan nam ngoai thu muc Bend Table.");
                string relative = normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? normalized.Substring(prefix.Length) : normalized;
                byte[] bytes = ReadSource(normalized);
                string hash = Hash(bytes);
                lock (StoreGate)
                {
                    EnsureLoaded();
                    BendTableDatabaseEntry entry = document.Tables.FirstOrDefault(t =>
                        string.Equals(t.RelativePath, relative, StringComparison.OrdinalIgnoreCase));
                    bool unchanged = entry != null && entry.SourceSha256 == hash && ValidEntry(entry);
                    if (unchanged)
                    {
                        cached = ToWorkbook(entry.Data, normalized);
                    }
                    else
                    {
                        cached = BendTableXlsxImporter.Read(bytes, normalized);
                        if (!cached.IsValid)
                        {
                            session[normalized] = cached;
                            return cached;
                        }
                        entry = new BendTableDatabaseEntry
                        {
                            RelativePath = relative,
                            SourceSha256 = hash,
                            ImportedUtc = DateTime.UtcNow.ToString("o"),
                            Data = FromWorkbook(cached)
                        };
                        entry.DataSha256 = Hash(Encoding.UTF8.GetBytes(Serializer().Serialize(entry.Data)));
                        document.Tables.RemoveAll(t => string.Equals(t.RelativePath, relative, StringComparison.OrdinalIgnoreCase));
                        document.Tables.Add(entry);
                        needsSave = true;
                        Debug.WriteLine("[CHECK KEGAKI] Cap nhat database tu Excel: " + normalized);
                    }
                    if (needsSave) Save(entry);
                }
            }
            catch (Exception ex)
            {
                cached = Failure(normalized, "Database Bend Table: " + ex.Message
                    + " Khong dung thong so cu cho lan CHECK nay.");
            }
            session[normalized] = cached;
            return cached;
        }

        private void EnsureLoaded()
        {
            if (document != null) return;
            document = TryLoad(databasePath);
            if (document != null) return;
            // A damaged cache is reconstructed from the verified embedded snapshot
            // and the current Excel source; no model or source workbook is changed.
            using (Stream stream = typeof(BendTableDatabase).Assembly
                .GetManifestResourceStream("ADDIN.BendTables.Default.json"))
            {
                if (stream != null)
                {
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                        document = Deserialize(reader.ReadToEnd());
                    if (document != null && !string.Equals(document.SourceRoot, cacheIdentity, StringComparison.OrdinalIgnoreCase))
                        document = null;
                }
            }
            if (document == null) document = NewDocument(cacheIdentity);
            needsSave = true;
        }

        private BendTableDatabaseDocument TryLoad(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var info = new FileInfo(path);
                if (info.Length > 16000000) return null;
                BendTableDatabaseDocument data = Deserialize(File.ReadAllText(path, Encoding.UTF8));
                return data != null && string.Equals(data.SourceRoot, cacheIdentity, StringComparison.OrdinalIgnoreCase) ? data : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK KEGAKI] Nhap lai database cache: " + ex.Message);
                return null;
            }
        }

        private static BendTableDatabaseDocument Deserialize(string text)
        {
            BendTableDatabaseDocument data = Serializer().Deserialize<BendTableDatabaseDocument>(text);
            if (data == null || data.Version != SchemaVersion || data.Tables == null) return null;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BendTableDatabaseEntry entry in data.Tables)
                if (entry == null || string.IsNullOrWhiteSpace(entry.RelativePath) || !paths.Add(entry.RelativePath)) return null;
            return data;
        }

        private void Save(BendTableDatabaseEntry updated)
        {
            // Merge fresh disk contents when another checker/process imported a
            // different table. A named mutex also protects separate SW sessions.
            string mutexName = @"Local\ADDIN_BendTables_" + Hash(Encoding.UTF8.GetBytes(databasePath.ToUpperInvariant()));
            using (var mutex = new System.Threading.Mutex(false, mutexName))
            {
                bool held = false;
                try
                {
                    try { held = mutex.WaitOne(5000); }
                    catch (System.Threading.AbandonedMutexException) { held = true; }
                    if (!held) throw new IOException("Database dang duoc cap nhat boi phien CHECK khac.");
                    BendTableDatabaseDocument latest = TryLoad(databasePath);
                    if (latest != null)
                    {
                        latest.Tables.RemoveAll(t => string.Equals(t.RelativePath, updated.RelativePath, StringComparison.OrdinalIgnoreCase));
                        latest.Tables.Add(updated);
                        document = latest;
                    }
                    document.UpdatedUtc = DateTime.UtcNow.ToString("o");
                    string directory = Path.GetDirectoryName(databasePath);
                    Directory.CreateDirectory(directory);
                    string temporary = databasePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        byte[] json = Encoding.UTF8.GetBytes(Serializer().Serialize(document));
                        using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        { file.Write(json, 0, json.Length); file.Flush(true); }
                        if (File.Exists(databasePath)) File.Replace(temporary, databasePath, databasePath + ".bak");
                        else File.Move(temporary, databasePath);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    needsSave = false;
                }
                finally { if (held) mutex.ReleaseMutex(); }
            }
        }

        private static bool ValidEntry(BendTableDatabaseEntry entry)
        {
            try
            {
                if (entry.Data == null || entry.DataSha256 != Hash(Encoding.UTF8.GetBytes(Serializer().Serialize(entry.Data)))) return false;
                BendTableXlsxImporter.Validate(ToWorkbook(entry.Data, entry.RelativePath));
                return true;
            }
            catch { return false; }
        }

        internal static byte[] ReadSource(string path)
        {
            // Do not read halfway through a file overwrite. Release the handle
            // before XML parsing so the library is not kept locked by the add-in.
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (file.Length > 10000000) throw new InvalidDataException("Excel Bend Table qua lon.");
                using (var memory = new MemoryStream()) { file.CopyTo(memory); return memory.ToArray(); }
            }
        }

        internal static string Hash(byte[] bytes)
        {
            using (SHA256 algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        internal static JavaScriptSerializer Serializer()
        { return new JavaScriptSerializer { MaxJsonLength = 16000000, RecursionLimit = 100 }; }

        internal static BendTableDatabaseDocument NewDocument(string sourceRoot)
        { return new BendTableDatabaseDocument { Version = SchemaVersion, SourceRoot = sourceRoot, Tables = new List<BendTableDatabaseEntry>() }; }

        internal static BendTableDatabaseData FromWorkbook(BendTableWorkbookData data)
        {
            return new BendTableDatabaseData
            {
                IsMillimeters = data.IsMillimeters, ValueKind = (int)data.ValueKind, MaterialName = data.MaterialName,
                Blocks = data.Blocks.Select(b => new BendTableDatabaseBlock
                {
                    ThicknessMm = b.ThicknessMm, HasCoefficient = b.HasCoefficient,
                    CoefficientMm = b.CoefficientMm, IsGroove = b.IsGroove,
                    RadiiMm = b.RadiiMm.ToList(),
                    Angles = b.Angles.Select(a => new BendTableDatabaseAngle
                    { AngleDeg = a.AngleDeg, ValuesMm = a.ValuesMm.ToList() }).ToList()
                }).ToList()
            };
        }

        private static BendTableWorkbookData ToWorkbook(BendTableDatabaseData data, string path)
        {
            var result = new BendTableWorkbookData
            { FilePath = path, IsMillimeters = data.IsMillimeters, ValueKind = (BendTableValueKind)data.ValueKind, MaterialName = data.MaterialName };
            foreach (BendTableDatabaseBlock saved in data.Blocks)
            {
                var block = new BendTableBlockData
                { ThicknessMm = saved.ThicknessMm, HasCoefficient = saved.HasCoefficient, CoefficientMm = saved.CoefficientMm, IsGroove = saved.IsGroove };
                block.RadiiMm.AddRange(saved.RadiiMm);
                for (int i = 0; i < saved.RadiiMm.Count; i++) block.SourceColumns.Add(i + 2);
                foreach (BendTableDatabaseAngle savedAngle in saved.Angles)
                {
                    var angle = new BendTableAngleData { AngleDeg = savedAngle.AngleDeg };
                    angle.ValuesMm.AddRange(savedAngle.ValuesMm);
                    block.Angles.Add(angle);
                }
                result.Blocks.Add(block);
            }
            return result;
        }

        private static BendTableWorkbookData Failure(string path, string error)
        { return new BendTableWorkbookData { FilePath = path, Error = error }; }
    }

    internal sealed class BendTableDatabaseDocument
    {
        public int Version { get; set; }
        public string SourceRoot { get; set; }
        public string UpdatedUtc { get; set; }
        public List<BendTableDatabaseEntry> Tables { get; set; }
    }
    internal sealed class BendTableDatabaseEntry
    {
        public string RelativePath { get; set; }
        public string SourceSha256 { get; set; }
        public string DataSha256 { get; set; }
        public string ImportedUtc { get; set; }
        public BendTableDatabaseData Data { get; set; }
    }
    internal sealed class BendTableDatabaseData
    {
        public bool IsMillimeters { get; set; }
        public int ValueKind { get; set; }
        public string MaterialName { get; set; }
        public List<BendTableDatabaseBlock> Blocks { get; set; }
    }
    internal sealed class BendTableDatabaseBlock
    {
        public double ThicknessMm { get; set; }
        public bool HasCoefficient { get; set; }
        public double CoefficientMm { get; set; }
        public bool IsGroove { get; set; }
        public List<double> RadiiMm { get; set; }
        public List<BendTableDatabaseAngle> Angles { get; set; }
    }
    internal sealed class BendTableDatabaseAngle
    {
        public double AngleDeg { get; set; }
        public List<double?> ValuesMm { get; set; }
    }
}
