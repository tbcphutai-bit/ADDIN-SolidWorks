using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    // CHECK may regenerate this disposable document, never the user's part.
    // A filesystem copy deliberately excludes unsaved edits; reject dirty sources.
    internal sealed class CheckPartWorkingCopy : IDisposable
    {
        private static readonly Dictionary<string, string> ownedPaths =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ICheckPartCopyHost host;
        private ICheckPartCopyDocument document;
        private string directory;
        private string workingPath;
        private string sourceHash;
        private readonly Dictionary<string, string> sourceHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> copyPaths = new List<string>();
        private bool disposed;

        private CheckPartWorkingCopy(ICheckPartCopyHost host, string sourcePath)
        { this.host = host; SourcePath = Path.GetFullPath(sourcePath); }

        public string SourcePath { get; private set; }
        public ModelDoc2 Model { get { return document == null ? null : document.Model; } }

        public static CheckPartWorkingCopy Open(ISldWorks app, string sourcePath, string configuration)
        { return Open(new SolidWorksCheckPartCopyHost(app), sourcePath, configuration); }

        internal static CheckPartWorkingCopy Open(ICheckPartCopyHost host, string sourcePath, string configuration)
        {
            var copy = new CheckPartWorkingCopy(host, sourcePath);
            try
            {
                if (!string.Equals(Path.GetExtension(copy.SourcePath), ".SLDPRT", StringComparison.OrdinalIgnoreCase)
                    || !File.Exists(copy.SourcePath))
                    throw new InvalidOperationException("Part chua duoc luu thanh file SLDPRT; chua the kiem tra tren ban sao.");
                ICheckPartCopyDocument original = host.FindOpen(copy.SourcePath);
                if (original != null && original.Dirty)
                    throw new InvalidOperationException("Part co thay doi chua luu; CHECK chua kiem tra de tranh dung du lieu cu tren dia.");
                if (string.IsNullOrWhiteSpace(configuration) && original != null)
                    configuration = original.Configuration;

                // Copy the dependency graph before opening any model. In-context
                // assemblies and derived parts must also refer only to our copies.
                bool hasModelReferences = false;
                Array dependencies = host.Dependencies(copy.SourcePath) as Array;
                if (dependencies != null) foreach (object item in dependencies)
                {
                    string dependency = Convert.ToString(item);
                    string extension = Path.GetExtension(dependency);
                    if ((string.Equals(extension, ".SLDPRT", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(extension, ".SLDASM", StringComparison.OrdinalIgnoreCase))
                        && !SamePath(dependency, copy.SourcePath))
                        hasModelReferences = true;
                }

                string id = Guid.NewGuid().ToString("N");
                copy.directory = Path.Combine(Path.GetTempPath(), "ADDIN_Check", id);
                Directory.CreateDirectory(copy.directory);
                copy.workingPath = Path.Combine(copy.directory, "CHECK_" + id + ".SLDPRT");
                File.Copy(copy.SourcePath, copy.workingPath, false);
                copy.copyPaths.Add(copy.workingPath);
                copy.sourceHash = HashFile(copy.workingPath);
                if (!string.Equals(copy.sourceHash, HashFile(copy.SourcePath), StringComparison.Ordinal))
                    throw new InvalidOperationException("File part thay doi trong khi tao ban sao; can kiem tra lai.");
                copy.sourceHashes.Add(copy.SourcePath, copy.sourceHash);
                if (hasModelReferences) copy.PrepareReferences();
                File.SetAttributes(copy.workingPath, File.GetAttributes(copy.workingPath) | FileAttributes.ReadOnly);

                bool oldVisibility = host.PartVisibility;
                int oldReferenceMode = host.ExternalReferenceLoadMode;
                try
                {
                    host.ExternalReferenceLoadMode = (int)swLoadExternalReferences_e.swLoadExternalReferences_None;
                    if (host.ExternalReferenceLoadMode != (int)swLoadExternalReferences_e.swLoadExternalReferences_None)
                        throw new InvalidOperationException("Khong tat duoc nap tham chieu ngoai khi mo ban sao.");
                    host.PartVisibility = false;
                    copy.document = host.Open(copy.workingPath, configuration ?? "");
                    if (copy.document == null)
                        throw new InvalidOperationException("Khong mo duoc ban sao tam cua part.");
                    if (!SamePath(copy.document.Path, copy.workingPath) || SamePath(copy.document.Path, copy.SourcePath))
                        throw new InvalidOperationException("SOLIDWORKS khong tra ve dung ban sao tam; dung kiem tra.");
                    if (!copy.document.ReadOnly)
                        throw new InvalidOperationException("Ban sao tam khong duoc mo read-only; dung kiem tra.");
                    if (!string.IsNullOrWhiteSpace(configuration)
                        && !string.Equals(copy.document.Configuration, configuration, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Khong mo duoc configuration yeu cau tren ban sao: " + configuration);
                    lock (ownedPaths) ownedPaths.Add(copy.workingPath, copy.SourcePath);
                }
                finally
                {
                    try { host.PartVisibility = oldVisibility; }
                    finally { host.ExternalReferenceLoadMode = oldReferenceMode; }
                }
                copy.ValidateSource();
                Debug.WriteLine("[CHECK COPY] Source=" + copy.SourcePath + ", copy=" + copy.workingPath);
                return copy;
            }
            catch { copy.Dispose(); throw; }
        }

        public void ValidateSource()
        {
            ICheckPartCopyDocument original = host.FindOpen(SourcePath);
            if (original != null && original.Dirty)
                throw new InvalidOperationException("Part goc co thay doi chua luu trong luc CHECK; ket qua chua duoc xac nhan.");
            if (!string.Equals(sourceHash, HashFile(SourcePath), StringComparison.Ordinal))
                throw new InvalidOperationException("File part goc thay doi trong luc CHECK; can kiem tra lai.");
            foreach (KeyValuePair<string, string> entry in sourceHashes)
            {
                if (!string.Equals(entry.Value, HashFile(entry.Key), StringComparison.Ordinal))
                    throw new InvalidOperationException("Model tham chieu goc thay doi trong luc CHECK: " + entry.Key);
                ICheckPartCopyDocument reference = host.FindOpen(entry.Key);
                if (reference != null && reference.Dirty)
                    throw new InvalidOperationException("Model tham chieu goc co thay doi chua luu: " + entry.Key);
            }
        }

        private sealed class ReferenceNode
        {
            public string Source, Copy;
            public readonly Dictionary<string, ReferenceNode> References = new Dictionary<string, ReferenceNode>(StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsModelPath(string path)
        {
            string extension = Path.GetExtension(path);
            return string.Equals(extension, ".SLDPRT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".SLDASM", StringComparison.OrdinalIgnoreCase);
        }

        private void PrepareReferences()
        {
            var nodes = new Dictionary<string, ReferenceNode>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<ReferenceNode>();
            var root = new ReferenceNode { Source = SourcePath, Copy = workingPath };
            nodes.Add(SourcePath, root); pending.Enqueue(root);
            while (pending.Count > 0)
            {
                ReferenceNode node = pending.Dequeue();
                Array values = host.Dependencies(node.Source) as Array;
                if (values == null) continue;
                // GetDocumentDependencies2 returns filename/full-path pairs.
                for (int i = 1; i < values.Length; i += 2)
                {
                    string saved = Convert.ToString(values.GetValue(i + values.GetLowerBound(0)));
                    if (!IsModelPath(saved)) continue;
                    string resolved = File.Exists(saved) ? Path.GetFullPath(saved) : null;
                    if (resolved == null)
                    {
                        string local = Path.Combine(Path.GetDirectoryName(node.Source), Path.GetFileName(saved));
                        string besideRoot = Path.Combine(Path.GetDirectoryName(SourcePath), Path.GetFileName(saved));
                        if (File.Exists(local)) resolved = Path.GetFullPath(local);
                        else if (File.Exists(besideRoot)) resolved = Path.GetFullPath(besideRoot);
                        else resolved = host.FindLibraryReference(Path.GetFileName(saved));
                    }
                    // Preserve an already missing reference as missing, under a
                    // unique sandbox name. Never let SW search the original name.
                    if (resolved == null)
                    {
                        string missingKey = "MISSING:" + saved;
                        ReferenceNode missing;
                        if (!nodes.TryGetValue(missingKey, out missing))
                        {
                            missing = new ReferenceNode { Copy = Path.Combine(directory,
                                "MISSING_" + Guid.NewGuid().ToString("N") + Path.GetExtension(saved)) };
                            nodes.Add(missingKey, missing); copyPaths.Add(missing.Copy);
                        }
                        node.References[saved] = missing;
                        Debug.WriteLine("[CHECK COPY] Preserved missing reference=" + saved);
                        continue;
                    }
                    ReferenceNode target;
                    if (!nodes.TryGetValue(resolved, out target))
                    {
                        if (nodes.Count >= 512) throw new InvalidOperationException("Qua nhieu model tham chieu de kiem tra part doc lap.");
                        ICheckPartCopyDocument opened = host.FindOpen(resolved);
                        if (opened != null && opened.Dirty)
                            throw new InvalidOperationException("Model tham chieu co thay doi chua luu: " + resolved);
                        target = new ReferenceNode { Source = resolved,
                            Copy = Path.Combine(directory, "REF_" + Guid.NewGuid().ToString("N") + Path.GetExtension(resolved)) };
                        File.Copy(target.Source, target.Copy, false); copyPaths.Add(target.Copy);
                        string hash = HashFile(target.Copy);
                        if (!string.Equals(hash, HashFile(target.Source), StringComparison.Ordinal))
                            throw new InvalidOperationException("Model tham chieu thay doi khi tao ban sao: " + target.Source);
                        sourceHashes.Add(target.Source, hash);
                        nodes.Add(resolved, target); pending.Enqueue(target);
                    }
                    node.References[saved] = target;
                }
            }
            foreach (ReferenceNode node in nodes.Values)
            {
                if (node.Source == null) continue;
                foreach (KeyValuePair<string, ReferenceNode> reference in node.References)
                    if (!host.ReplaceReference(node.Copy, reference.Key, reference.Value.Copy))
                        throw new InvalidOperationException("Khong doi duoc tham chieu trong ban sao: " + reference.Key);
                Array values = host.Dependencies(node.Copy) as Array;
                if (values != null) for (int i = 1; i < values.Length; i += 2)
                {
                    string path = Convert.ToString(values.GetValue(i + values.GetLowerBound(0)));
                    if (!IsModelPath(path)) continue;
                    bool owned = false;
                    foreach (string ownPath in copyPaths) if (SamePath(path, ownPath)) { owned = true; break; }
                    if (!owned) throw new InvalidOperationException("Ban sao van tham chieu model goc: " + path);
                }
                File.SetAttributes(node.Copy, File.GetAttributes(node.Copy) | FileAttributes.ReadOnly);
            }
            Debug.WriteLine("[CHECK COPY] Isolated model graph=" + nodes.Count);
        }

        internal static void RequireWorkingCopy(ModelDoc2 model)
        {
            string path = model == null ? "" : model.GetPathName();
            if (string.IsNullOrWhiteSpace(path) || !IsOwnedPath(path))
                throw new InvalidOperationException("Chi duoc doi configuration/Flat-Pattern tren ban sao tam cua CHECK.");
        }

        internal static string GetSourcePath(ModelDoc2 model)
        {
            string path = model == null ? "" : model.GetPathName();
            string source;
            lock (ownedPaths)
                return !string.IsNullOrWhiteSpace(path) && ownedPaths.TryGetValue(Path.GetFullPath(path), out source)
                    ? source : path;
        }

        internal static bool IsOwnedPath(string path)
        { lock (ownedPaths) return ownedPaths.ContainsKey(Path.GetFullPath(path)); }

        private static bool SamePath(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
        }

        private static string HashFile(string path)
        {
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (!string.IsNullOrWhiteSpace(workingPath))
                lock (ownedPaths) ownedPaths.Remove(workingPath);
            try
            {
                // A failed OpenDoc can return another document. Never close it.
                foreach (string copyPath in copyPaths)
                {
                    ICheckPartCopyDocument openedCopy = host.FindOpen(copyPath);
                    if (openedCopy != null && SamePath(openedCopy.Path, copyPath))
                    {
                        string title = openedCopy.Title;
                        if (!string.IsNullOrWhiteSpace(title)) host.Close(title);
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine("[CHECK COPY] Close: " + ex.Message); }
            try
            {
                // Delete only our exact file, and only after its document is closed.
                foreach (string copyPath in copyPaths)
                {
                    if (!SamePath(copyPath, SourcePath) && SamePath(Path.GetDirectoryName(copyPath), directory)
                        && host.FindOpen(copyPath) == null && File.Exists(copyPath))
                    {
                        File.SetAttributes(copyPath, File.GetAttributes(copyPath) & ~FileAttributes.ReadOnly);
                        File.Delete(copyPath);
                    }
                }
                if (!string.IsNullOrWhiteSpace(directory)) Directory.Delete(directory, false);
            }
            catch (Exception ex) { Debug.WriteLine("[CHECK COPY] Cleanup: " + ex.Message); }
        }
    }

    // Small boundary used to verify failure/cleanup paths without a live SW session.
    internal interface ICheckPartCopyDocument
    {
        string Path { get; }
        string Title { get; }
        string Configuration { get; }
        bool Dirty { get; }
        bool ReadOnly { get; }
        ModelDoc2 Model { get; }
    }

    internal interface ICheckPartCopyHost
    {
        bool PartVisibility { get; set; }
        int ExternalReferenceLoadMode { get; set; }
        ICheckPartCopyDocument FindOpen(string path);
        ICheckPartCopyDocument Open(string path, string configuration);
        object Dependencies(string path);
        bool ReplaceReference(string copyPath, string oldPath, string newCopyPath);
        string FindLibraryReference(string fileName);
        void Close(string title);
    }

    internal sealed class SolidWorksCheckPartCopyHost : ICheckPartCopyHost
    {
        private readonly ISldWorks app;
        public SolidWorksCheckPartCopyHost(ISldWorks app) { this.app = app; }
        public int ExternalReferenceLoadMode
        {
            get { return app.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swLoadExternalReferences); }
            set
            {
                if (!app.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swLoadExternalReferences, value))
                    throw new InvalidOperationException("Khong doi duoc che do nap tham chieu ngoai.");
            }
        }
        public bool PartVisibility
        {
            get { return app.GetDocumentVisible((int)swDocumentTypes_e.swDocPART); }
            set { app.DocumentVisible(value, (int)swDocumentTypes_e.swDocPART); }
        }
        public ICheckPartCopyDocument FindOpen(string path)
        {
            ModelDoc2 model = app.GetOpenDocumentByName(path) as ModelDoc2;
            return model == null ? null : new SolidWorksCheckPartCopyDocument(model);
        }
        public ICheckPartCopyDocument Open(string path, string configuration)
        {
            int errors = 0, warnings = 0;
            ModelDoc2 model = app.OpenDoc6(path, (int)swDocumentTypes_e.swDocPART,
                (int)(swOpenDocOptions_e.swOpenDocOptions_Silent | swOpenDocOptions_e.swOpenDocOptions_ReadOnly),
                configuration, ref errors, ref warnings) as ModelDoc2;
            Debug.WriteLine("[CHECK COPY] Open errors=" + errors + ", warnings=" + warnings);
            if (errors != 0)
                throw new InvalidOperationException("Khong mo day du ban sao part. errors=" + errors + ", warnings=" + warnings);
            return model == null ? null : new SolidWorksCheckPartCopyDocument(model);
        }
        public object Dependencies(string path)
        { return app.GetDocumentDependencies2(path, false, false, false); }
        public bool ReplaceReference(string copyPath, string oldPath, string newCopyPath)
        { return app.ReplaceReferencedDocument(copyPath, oldPath, newCopyPath); }
        public string FindLibraryReference(string fileName)
        {
            var roots = new List<string>();
            roots.Add(@"C:\sw共有ライブラリ\共有ライブラリ\無加工品");
            string configured = app.GetSearchFolders((int)swSearchFolderTypes_e.swDocumentType);
            if (!string.IsNullOrWhiteSpace(configured)) roots.AddRange(configured.Split(';'));
            // Purchased hardware has its canonical catalog under 無加工品;
            // assembly packages can contain different models with the same name.
            string found = null;
            foreach (string root in roots)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                foreach (string candidate in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (!string.Equals(System.IO.Path.GetFileName(candidate), fileName, StringComparison.OrdinalIgnoreCase)) continue;
                    string full = System.IO.Path.GetFullPath(candidate);
                    if (found != null && !string.Equals(found, full, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Co nhieu model cung ten trong thu vien; chua the chon tham chieu: " + fileName);
                    found = full;
                }
                if (found != null) return found;
            }
            return found;
        }
        public void Close(string title) { app.QuitDoc(title); }
    }

    internal sealed class SolidWorksCheckPartCopyDocument : ICheckPartCopyDocument
    {
        public SolidWorksCheckPartCopyDocument(ModelDoc2 model) { Model = model; }
        public ModelDoc2 Model { get; private set; }
        public string Path { get { return Model.GetPathName(); } }
        public string Title { get { return Model.GetTitle(); } }
        public string Configuration { get { return Model.ConfigurationManager.ActiveConfiguration.Name; } }
        public bool Dirty
        {
            get
            {
                if (Model.GetSaveFlag()) return true;
                // GetSaveFlag can hide dirty state for a hidden read-only model.
                return Model.IsOpenedReadOnly() && Model.ConfigurationManager.ActiveConfiguration.IsDirty();
            }
        }
        public bool ReadOnly { get { return Model.IsOpenedReadOnly(); } }
    }
}
