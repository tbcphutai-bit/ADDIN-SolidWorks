using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    internal sealed class BendCheckItem : IDisposable
    {
        internal string Key, Name, Path, Configuration, Error, Material;
        internal double? ThicknessMm;
        internal int Quantity, Stamp;
        internal bool Done, Current = true, AssemblyBodyModified;
        internal readonly List<Component2> Instances = new List<Component2>();
        internal List<VCutPropertySetupCommand.BendRow> Rows = new List<VCutPropertySetupCommand.BendRow>();
        internal ModelDoc2 Model;
        internal CheckPartWorkingCopy Copy;
        internal BendMinimumContext Context => Rows.Select(r => r.MinimumContext).FirstOrDefault(c => c != null);
        internal BendMinimumSummary Summary => BendMinimumSummary.Create(Rows);
        internal string MinimumText => !Done ? "Chờ" : !Current ? "Cần quét lại" : Error != null ? "Chưa xác minh" :
            Summary.Status == BendMinimumStatus.MeetsMinimum ? "Đạt" : Summary.Status == BendMinimumStatus.BelowMinimum ? "NG" : "Chưa xác minh";
        internal static string GroupKey(string path, string configuration, string instance, bool modified)
        {
            // Configuration is part of the identity. An instance with assembly
            // cuts cannot inherit the result of the unmodified source Part.
            return (path ?? "").ToUpperInvariant() + "\n" + (configuration ?? "").ToUpperInvariant() +
                (modified || string.IsNullOrEmpty(path) ? "\n@" + instance : "");
        }
        internal void Invalidate() { Current = false; Context?.Invalidate(); }
        public void Dispose() { Current = false; Copy?.Dispose(); Copy = null; Model = null; }
    }

    // All SolidWorks calls in this class run on the task pane's STA. Only the
    // Excel provider runs on a worker. Geometry is captured once per file/config.
    internal sealed class BendCheckSession : IDisposable
    {
        private readonly ISldWorks app;
        internal readonly ModelDoc2 Host;
        internal readonly bool IsAssembly;
        internal readonly List<BendCheckItem> Items = new List<BendCheckItem>();
        private readonly HashSet<string> selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<List<VCutPropertySetupCommand.BendRow>> sharedRows;
        private readonly string hostConfiguration;
        private int hostStamp;
        private bool disposed;
        internal int SuppressedCount { get; private set; }
        internal bool HasSelection => selected.Count > 0;

        internal BendCheckSession(ISldWorks app, ModelDoc2 host, Func<List<VCutPropertySetupCommand.BendRow>> sharedRows)
        {
            this.app = app; Host = host; this.sharedRows = sharedRows;
            IsAssembly = host.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY;
            if (!IsAssembly && host.GetType() != (int)swDocumentTypes_e.swDocPART)
                throw new InvalidOperationException("Mở Part hoặc Assembly.");
            hostConfiguration = ConfigurationOf(host); CaptureSelection();
        }
        internal static string ConfigurationOf(ModelDoc2 model) => model.ConfigurationManager.ActiveConfiguration.Name;
        internal bool IsHostActive
        {
            get { try { return !disposed && app.IsSame(app.ActiveDoc, Host) == (int)swObjectEquality.swObjectSame && ConfigurationOf(Host) == hostConfiguration; } catch { return false; } }
        }
        internal bool IsEditing
        {
            get
            {
                try
                {
                    if (Host.GetActiveSketch2() != null || Host.Extension.NeedsRebuild2 != 0) return true;
                    var target = IsAssembly ? ((AssemblyDoc)Host).GetEditTarget() as ModelDoc2 : null;
                    return target != null && (target.GetActiveSketch2() != null || target.Extension.NeedsRebuild2 != 0);
                }
                catch { return true; }
            }
        }
        internal void CaptureSelection()
        {
            selected.Clear(); if (!IsAssembly) return;
            var manager = (SelectionMgr)Host.SelectionManager;
            for (int i = 1; i <= manager.GetSelectedObjectCount2(-1); i++)
            {
                var component = manager.GetSelectedObjectsComponent4(i, -1) as Component2;
                if (component != null) selected.Add(component.Name2);
            }
        }
        internal void BuildInventory(bool selectedOnly)
        {
            foreach (var item in Items) item.Dispose(); Items.Clear(); SuppressedCount = 0;
            if (!IsHostActive) throw new InvalidOperationException("Quay lại đúng model và configuration đã mở Check chấn.");
            if (IsEditing) throw new InvalidOperationException("Thoát sketch và rebuild trước khi kiểm tra.");
            if (!IsAssembly)
            {
                string path = Host.GetPathName();
                Items.Add(new BendCheckItem { Key = BendCheckItem.GroupKey(path, hostConfiguration, Host.GetTitle(), false),
                    Path = path, Name = Host.GetTitle(), Configuration = hostConfiguration, Quantity = 1, Model = Host });
            }
            else
            {
                var assembly = (AssemblyDoc)Host;
                assembly.ResolveAllLightWeightComponents(false);
                var all = (assembly.GetComponents(false) as object[] ?? new object[0]).OfType<Component2>().ToList();
                // Preserve the captured scope across Refresh and face highlighting.
                var roots = selectedOnly ? all.Where(c => selected.Contains(c.Name2)).ToList() :
                    (assembly.GetComponents(true) as object[] ?? new object[0]).OfType<Component2>().ToList();
                if (selectedOnly && roots.Count == 0) throw new InvalidOperationException("Component đã chọn không còn trong configuration này. Chọn lại phạm vi.");
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var component in roots) Visit(component, visited);
            }
            Items.Sort((a, b) => { int n = StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name); return n != 0 ? n : StringComparer.Ordinal.Compare(a.Configuration, b.Configuration); });
            hostStamp = Host.GetUpdateStamp();
        }
        private void Visit(Component2 component, HashSet<string> visited)
        {
            string name = component.Name2;
            if (!visited.Add(name)) return;
            if (component.IsSuppressed()) { SuppressedCount++; return; }
            var children = component.GetChildren() as object[];
            var model = component.GetModelDoc2() as ModelDoc2;
            string path = component.GetPathName();
            bool assembly = model != null ? model.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY :
                string.Equals(System.IO.Path.GetExtension(path), ".SLDASM", StringComparison.OrdinalIgnoreCase);
            if (assembly || children != null && children.Length > 0)
            { if (children != null) foreach (var child in children.OfType<Component2>()) Visit(child, visited); return; }
            string config = component.ReferencedConfiguration;
            bool modified = false; string error = null;
            if (model == null) error = "Chưa tải được component.";
            else
            {
                try
                {
                    object info; component.GetBodies3((int)swBodyType_e.swSolidBody, out info);
                    var values = info as Array;
                    if (values == null) error = "Chưa đọc được body trong Assembly.";
                    else modified = values.Cast<object>().Any(v => Convert.ToInt32(v) == (int)swBodyInfo_e.swUserBody_e);
                }
                catch (Exception ex) { error = "Chưa đối chiếu body Assembly: " + ex.Message; }
            }
            string key = BendCheckItem.GroupKey(path, config, name, modified || error != null);
            var item = Items.FirstOrDefault(i => i.Key == key);
            if (item == null)
            {
                string title = string.IsNullOrEmpty(path) ? name : System.IO.Path.GetFileNameWithoutExtension(path);
                item = new BendCheckItem { Key = key, Path = path, Name = title, Configuration = config,
                    Model = model, AssemblyBodyModified = modified, Error = modified ? "Body có sửa trong Assembly; cần kiểm tra hình học instance." : error };
                Items.Add(item);
            }
            item.Instances.Add(component); item.Quantity++;
        }
        internal void Capture(BendCheckItem item, BendToolCatalogReadResult source)
        {
            if (item.Done) return;
            try
            {
                if (!IsHostActive || IsEditing) throw new InvalidOperationException("Model đang thay đổi. Quét lại sau rebuild.");
                if (item.Error != null) return;
                var model = item.Model;
                if (model == null || !string.Equals(ConfigurationOf(model), item.Configuration, StringComparison.OrdinalIgnoreCase))
                {
                    if (model != null && model.GetSaveFlag())
                    { item.Error = "Configuration khác có thay đổi chưa lưu. Lưu Part rồi Quét lại."; return; }
                    item.Copy = CheckPartWorkingCopy.Open(app, item.Path, item.Configuration);
                    model = item.Copy.Model; item.Model = model;
                    item.Copy.ValidateSource();
                }
                if (!string.Equals(ConfigurationOf(model), item.Configuration, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Configuration không khớp component.");
                if (model.GetActiveSketch2() != null || model.Extension.NeedsRebuild2 != 0)
                    throw new InvalidOperationException("Part cần thoát sketch và rebuild.");
                int stamp = model.GetUpdateStamp(); item.Stamp = stamp;
                var shared = !IsAssembly ? sharedRows?.Invoke() : null;
                var context = shared?.Select(r => r.MinimumContext).FirstOrDefault(c => c != null);
                if (context != null && context.CheckCurrent()) item.Rows = shared;
                else
                {
                    item.Rows = VCutPropertySetupCommand.CaptureForCheck(app, model, item.Configuration,
                        () => item.Current && IsHostActive && model.GetActiveSketch2() == null && model.Extension.NeedsRebuild2 == 0 && model.GetUpdateStamp() == stamp && ConfigurationOf(model) == item.Configuration);
                    context = item.Context;
                }
                item.Material = item.Rows.FirstOrDefault()?.Material;
                item.ThicknessMm = item.Rows.FirstOrDefault()?.ModelThicknessMm;
                if (context != null)
                {
                    context.SetSource(source);
                    if (IsAssembly)
                    {
                        var component = item.Instances.FirstOrDefault();
                        context.InspectionFactory = () => new BendMinimumInspection(app, model, context, Host, component, item.Copy == null);
                    }
                }
                if (model.GetUpdateStamp() != stamp || !IsHostActive)
                { item.Invalidate(); throw new InvalidOperationException("Model đổi trong khi đọc. Quét lại."); }
                if (item.Rows.Count == 0) item.Error = "Không có nếp chấn được hỗ trợ.";
            }
            catch (Exception ex) { item.Error = ex.Message; item.Context?.Invalidate(); }
            finally { item.Done = true; }
        }
        internal bool CheckHostStamp()
        {
            try { return IsHostActive && !IsEditing && Host.GetUpdateStamp() == hostStamp; } catch { return false; }
        }
        internal bool CheckItem(BendCheckItem item)
        {
            if (!item.Done || item.Model == null || item.Context == null) return true;
            if (!item.Current) return false;
            try
            {
                bool current = item.Model.GetActiveSketch2() == null && item.Model.Extension.NeedsRebuild2 == 0 &&
                    item.Model.GetUpdateStamp() == item.Stamp && ConfigurationOf(item.Model) == item.Configuration;
                if (item.Copy != null) item.Copy.ValidateSource();
                if (!current) item.Invalidate(); return current;
            }
            catch { item.Invalidate(); return false; }
        }
        internal void Invalidate() { foreach (var item in Items) item.Invalidate(); }
        internal void SelectRepresentative(BendCheckItem item)
        {
            if (!IsAssembly || !IsHostActive || item == null) return;
            try { Host.ClearSelection2(true); item.Instances.FirstOrDefault()?.Select4(false, null, false); Host.GraphicsRedraw2(); } catch { }
        }
        public void Dispose() { if (disposed) return; disposed = true; foreach (var item in Items) item.Dispose(); }
    }

    internal sealed partial class VCutPropertySetupCommand
    {
        internal static List<BendRow> CaptureForCheck(ISldWorks app, ModelDoc2 part, string config, Func<bool> current)
        {
            double thickness; string table;
            var features = CollectActiveBends(part, out table, out thickness);
            if (features.Count == 0) return new List<BendRow>();
            var tags = new VCutFeatureTagStore(app, part); string material = ReadMaterial(part, config);
            var rows = features.Select(f => CreateRow(f, table, thickness, tags, material)).ToList();
            var timer = Stopwatch.StartNew();
            foreach (var row in rows)
                try { var id = part.Extension.GetPersistReference3(row.Feature) as byte[]; row.PersistentId = id == null ? "" : Convert.ToBase64String(id); } catch { }
            var bodies = BendPanelGeometryReader.Read(app, part, config, rows);
            var context = new BendMinimumContext(BendMinimumFeatureGraph.Build(bodies, rows), current,
                timer.Elapsed.TotalSeconds, bodies.Sum(b => b.Faces.Count), bodies.Sum(b => b.Edges.Count));
            context.InspectionFactory = () => new BendMinimumInspection(app, part, context);
            foreach (var row in rows) row.MinimumContext = context;
            context.CheckCurrent(); return rows;
        }
    }
}
