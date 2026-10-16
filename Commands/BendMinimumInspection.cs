using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace ADDIN.Commands
{
    // A CCW must remain alive as long as its temporary SolidWorks callout.
    [ComVisible(true), ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class BendMinimumCalloutHandler : SwCalloutHandler
    {
        public bool OnStringValueChanged(object callout, int row, string text) => true;
    }

    internal sealed class BendMinimumPreview
    {
        internal string Error;
        internal double PositionMm;
        internal SectionProfilePlanner.Point Bend;
        internal readonly List<SectionProfilePlanner.Curve> Curves = new List<SectionProfilePlanner.Curve>();
        internal readonly List<BendMinimumPreviewDatum> Datums = new List<BendMinimumPreviewDatum>();
    }
    internal sealed class BendMinimumPreviewDatum
    {
        internal int Side;
        internal SectionProfilePlanner.Point Start, End;
    }

    // Flush each boundary before entering native code: an AccessViolation can
    // terminate SolidWorks without reaching a managed catch/finally. This is
    // diagnostic only; it must never change API order or swallow native faults.
    internal sealed class BendMinimumNativeTrace
    {
        private const int MaximumBytes = 256 * 1024;
        private static readonly object gate = new object();
        private readonly string filePath, session = Guid.NewGuid().ToString("N").Substring(0, 8);
        internal BendMinimumNativeTrace(string directory = null)
        {
            int pid; using (var process = Process.GetCurrentProcess()) pid = process.Id;
            filePath = Path.Combine(directory ?? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "ADDIN", "Logs"),
                "BendMinimumDetails-" + pid + ".log");
        }
        [Conditional("DEBUG")]
        internal void Mark(string phase, string detail = null)
        {
            try
            {
                string line = DateTimeOffset.Now.ToString("o") + " session=" + session + " thread=" + System.Threading.Thread.CurrentThread.ManagedThreadId +
                    " " + phase + (string.IsNullOrEmpty(detail) ? "" : " | " + detail.Replace("\r", " ").Replace("\n", " "));
                lock (gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                    using (var stream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
                    {
                        if (stream.Length > MaximumBytes) stream.SetLength(0);
                        stream.Seek(0, SeekOrigin.End);
                        using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.WriteLine(line);
                    }
                }
                Debug.WriteLine("[BEND MIN NATIVE] " + line);
            }
            catch (Exception ex) { Debug.WriteLine("[BEND MIN NATIVE] Cannot write diagnostic log: " + ex.Message); }
        }
    }

    // Owned by the modeless details window. Nothing is inserted into the Part.
    // Native work stays on the SolidWorks/UI thread, and is rejected as soon as
    // the captured model/configuration/update stamp is no longer current.
    internal sealed class BendMinimumInspection : IDisposable
    {
        private sealed class Marker
        {
            internal Callout Callout;
            internal BendMinimumCalloutHandler Handler;
            internal int Number, FeatureId;
            internal BendVector Anchor, ViewAnchor;
            internal string Caption;
            internal int? TextColor;
        }
        private readonly ISldWorks app;
        private readonly ModelDoc2 model;
        private readonly ModelDoc2 viewport;
        private readonly Component2 component;
        private readonly bool canLocate;
        private readonly BendMinimumContext context;
        private readonly List<Marker> markers = new List<Marker>();
        private readonly BendMinimumSectionCache cache = new BendMinimumSectionCache();
        private readonly BendMinimumNativeTrace trace = new BendMinimumNativeTrace();
        private bool disposed;
        internal int MarkerCount => markers.Count;
        internal int PreviewSectionCount => cache.KernelSections;
        internal bool CanLocate => canLocate;
        internal string MarkerError { get; private set; }
        internal string MarkerDiagnostic { get; private set; }
        internal BendMinimumInspection(ISldWorks app, ModelDoc2 model, BendMinimumContext context)
            : this(app, model, context, model, null, true) { }
        internal BendMinimumInspection(ISldWorks app, ModelDoc2 model, BendMinimumContext context,
            ModelDoc2 viewport, Component2 component, bool canLocate)
        {
            this.app = app; this.model = model; this.context = context;
            this.viewport = viewport; this.component = component; this.canLocate = canLocate;
            trace.Mark("inspection.created", "assembly=" + typeof(BendMinimumInspection).Assembly.Location + " mvid=" + typeof(BendMinimumInspection).Module.ModuleVersionId);
        }
        private BendVector ToViewport(BendVector point, MathUtility math)
        {
            if (component == null) return point;
            var transform = component.GetTotalTransform(true);
            if (transform == null) throw new InvalidOperationException("Chưa đọc được vị trí component.");
            var native = math.CreatePoint(point.Array) as MathPoint;
            var moved = native.MultiplyTransform(transform) as MathPoint;
            return BendVector.From((double[])moved.ArrayData);
        }
        private bool Ready()
        {
            trace.Mark("context.CheckCurrent.begin");
            bool current = !disposed && context.CheckCurrent();
            trace.Mark("context.CheckCurrent.end", "current=" + current);
            return current;
        }

        internal void Update(IList<VCutPropertySetupCommand.BendRow> rows, int selectedId)
        {
            trace.Mark("markers.Update.begin", "rows=" + rows.Count + " selected=" + selectedId);
            if (!Ready()) { ClearMarkers(); return; }
            if (!canLocate) { MarkerError = "Configuration đọc trên bản sao; xem mặt cắt, chưa gắn nhãn 3D."; return; }
            bool redraw = false;
            if (markers.Count == 0 && MarkerError == null)
            {
                try
                {
                    trace.Mark("app.GetMathUtility.begin");
                    var math = app.GetMathUtility() as MathUtility;
                    trace.Mark("app.GetMathUtility.end");
                    for (int i = 0; i < rows.Count; i++)
                    {
                        BendMinimumFeatureGeometry geometry;
                        if (!context.Geometry.TryGetValue(rows[i].FeatureId, out geometry) || geometry.Body == null) continue;
                        var faces = BendFaces(geometry);
                        if (faces.Count == 0) continue;
                        var face = faces.OrderByDescending(f => f.Radius).First();
                        var ends = face.Edges.SelectMany(e => new[] { geometry.Body.Edges[e].A, geometry.Body.Edges[e].B }).ToList();
                        if (ends.Count == 0) continue;
                        var axis = face.Axis.Unit;
                        double low = ends.Min(p => BendVector.Dot(p, axis)), high = ends.Max(p => BendVector.Dot(p, axis));
                        double along = low + (high - low) * (.18 + .64 * (i + .5) / Math.Max(1, rows.Count));
                        var radial = ends.Select(p => p - face.Point - axis * BendVector.Dot(p - face.Point, axis)).Aggregate(new BendVector(), (a, b) => a + b);
                        if (radial.Length < 1e-9) radial = ends[0] - face.Point - axis * BendVector.Dot(ends[0] - face.Point, axis);
                        var requested = face.Point + axis * (along - BendVector.Dot(face.Point, axis)) + radial.Unit * face.Radius;
                        var native = geometry.Body.NativeFaces[face.Id];
                        trace.Mark("face.GetClosestPointOn.begin", "number=" + (i + 1) + " feature=" + rows[i].FeatureId);
                        var closest = native.GetClosestPointOn(requested.X, requested.Y, requested.Z) as double[];
                        trace.Mark("face.GetClosestPointOn.end");
                        if (closest == null || closest.Length < 3) continue;
                        var anchor = BendVector.From(closest);
                        var marker = new Marker { Number = i + 1, FeatureId = rows[i].FeatureId, Anchor = anchor, ViewAnchor = ToViewport(anchor, math), Handler = new BendMinimumCalloutHandler() };
                        trace.Mark("model.Extension.begin");
                        var extension = viewport.Extension;
                        trace.Mark("model.Extension.end");
                        trace.Mark("extension.CreateCallout.begin");
                        marker.Callout = extension.CreateCallout(1, marker.Handler) as Callout;
                        trace.Mark("extension.CreateCallout.end", "created=" + (marker.Callout != null));
                        if (marker.Callout == null) throw new InvalidOperationException("SolidWorks không tạo được nhãn số nếp chấn.");
                        // Register immediately so any later API failure still cleans up.
                        markers.Add(marker);
                        var callout = marker.Callout;
                        trace.Mark("callout.set_Label2.begin");
                        callout.set_Label2(0, (i + 1).ToString());
                        trace.Mark("callout.set_Label2.end");
                        trace.Mark("callout.SkipColon.begin"); callout.SkipColon = true; trace.Mark("callout.SkipColon.end");
                        trace.Mark("callout.set_IgnoreValue.begin"); callout.set_IgnoreValue(0, true); trace.Mark("callout.set_IgnoreValue.end");
                        trace.Mark("callout.set_ValueInactive.begin"); callout.set_ValueInactive(0, true); trace.Mark("callout.set_ValueInactive.end");
                        trace.Mark("callout.TextBox.begin"); callout.TextBox = false; trace.Mark("callout.TextBox.end");
                        trace.Mark("callout.SetLeader.begin"); callout.SetLeader(true, false); trace.Mark("callout.SetLeader.end");
                        trace.Mark("callout.SetTargetPoint.begin"); var viewAnchor = marker.ViewAnchor; callout.SetTargetPoint(0, viewAnchor.X, viewAnchor.Y, viewAnchor.Z); trace.Mark("callout.SetTargetPoint.end");
                        double offset = Math.Max(.006, rows[i].ModelThicknessMm.GetValueOrDefault(2) * .004);
                        var position = anchor + radial.Unit * offset + axis * (offset * (i % 2 == 0 ? 1 : -1));
                        trace.Mark("math.CreatePoint.begin");
                        var nativePosition = math.CreatePoint(ToViewport(position, math).Array) as MathPoint;
                        trace.Mark("math.CreatePoint.end");
                        trace.Mark("callout.Position.begin"); callout.Position = nativePosition; trace.Mark("callout.Position.end");
                        trace.Mark("callout.Display(true).begin"); callout.Display(true); trace.Mark("callout.Display(true).end"); redraw = true;
                    }
                }
                catch (Exception ex) { trace.Mark("markers.Update.exception", ex.ToString()); MarkerError = "Chưa hiển thị được số trên model: " + ex.Message; MarkerDiagnostic = ex.ToString(); ClearMarkers(); }
            }
            foreach (var marker in markers)
            {
                var row = rows.FirstOrDefault(r => r.FeatureId == marker.FeatureId); if (row == null) continue;
                var status = context.Evaluate(row).Status;
                string caption = marker.Number + (status == BendMinimumStatus.BelowMinimum ? " NG" : status == BendMinimumStatus.InsufficientData || status == BendMinimumStatus.Conditional ? " ?" : "");
                int color = status == BendMinimumStatus.BelowMinimum ? (int)swUserPreferenceIntegerValue_e.swSystemColorsRefTriadX : marker.FeatureId == selectedId ? (int)swUserPreferenceIntegerValue_e.swSystemColorsRefTriadZ : (int)swUserPreferenceIntegerValue_e.swSystemColorsText;
                try {
                    trace.Mark("marker.update.begin", "number=" + marker.Number + " feature=" + marker.FeatureId);
                    if (marker.Caption != caption) { trace.Mark("callout.caption.begin"); marker.Callout.set_Label2(0, caption); trace.Mark("callout.caption.end"); marker.Caption = caption; redraw = true; }
                    // SW 2024 SP5 faults in get_TextColor for these independent
                    // callouts. This session owns their style: compare our last
                    // successful write, without querying the native getter.
                    if (marker.TextColor != color) {
                        trace.Mark("callout.set_TextColor.begin", "number=" + marker.Number + " color=" + color);
                        marker.Callout.set_TextColor(0, color);
                        trace.Mark("callout.set_TextColor.end");
                        marker.TextColor = color; redraw = true;
                    }
                    trace.Mark("marker.update.end"); }
                catch (Exception ex) { trace.Mark("marker.update.exception", ex.ToString()); /* A model closing can already have removed its callouts. */ }
            }
            if (redraw) try { trace.Mark("markers.GraphicsRedraw2.begin"); viewport.GraphicsRedraw2(); trace.Mark("markers.GraphicsRedraw2.end"); } catch (Exception ex) { trace.Mark("markers.GraphicsRedraw2.exception", ex.ToString()); }
            trace.Mark("markers.Update.end");
        }
        private static List<BendPanelFace> BendFaces(BendMinimumFeatureGeometry geometry)
        {
            var faces = geometry.Body.Faces.Where(f => f.Kind == 2 && f.Owner == geometry.FeatureId).ToList();
            // Do not mark an arbitrary cylinder when a feature has several axes.
            if (faces.Count == 0) return faces;
            var first = faces[0];
            if (faces.Any(f => BendVector.Cross(f.Axis, first.Axis).Length > 1e-8 || BendVector.Cross(f.Point - first.Point, first.Axis).Length > 1e-8)) faces.Clear();
            return faces;
        }
        // -2: bend skins, -1: worst measured side, 0/1: explicitly chosen side.
        internal bool Select(VCutPropertySetupCommand.BendRow row, int side)
        {
            trace.Mark("selection.begin", "feature=" + row?.FeatureId + " side=" + side);
            if (!canLocate || !Ready() || row?.MinimumContext != context) return false;
            BendMinimumFeatureGeometry geometry;
            if (!context.Geometry.TryGetValue(row.FeatureId, out geometry) || geometry.Body == null) return false;
            var check = context.Evaluate(row);
            var measure = side >= 0 && side < check.Sides.Count ? check.Sides[side].Measure : side == -1 ? check.Datum : null;
            var ids = measure == null ? BendFaces(geometry).Select(f => f.Id) : measure.Faces;
            try
            {
                trace.Mark("selection.ClearSelection2.begin"); viewport.ClearSelection2(true); trace.Mark("selection.ClearSelection2.end"); bool selected = false;
                foreach (int id in ids.Distinct()) { Face2 face; if (geometry.Body.NativeFaces.TryGetValue(id, out face)) { trace.Mark("face.Select4.begin", "face=" + id); var entity = component == null ? face as Entity : component.GetCorrespondingEntity(face) as Entity; if (entity != null) selected |= entity.Select4(true, null); trace.Mark("face.Select4.end"); } }
                trace.Mark("selection.GraphicsRedraw2.begin"); viewport.GraphicsRedraw2(); trace.Mark("selection.GraphicsRedraw2.end"); return selected;
            }
            catch (Exception ex) { trace.Mark("selection.exception", ex.ToString()); return false; }
        }
        internal BendMinimumPreview Preview(VCutPropertySetupCommand.BendRow row)
        {
            trace.Mark("preview.begin", "feature=" + row?.FeatureId);
            var result = new BendMinimumPreview();
            if (!Ready() || row?.MinimumContext != context) { result.Error = "Đang chờ model sau rebuild. Chưa hiển thị mặt cắt cũ."; return result; }
            BendMinimumFeatureGeometry geometry;
            if (!context.Geometry.TryGetValue(row.FeatureId, out geometry) || geometry.Body?.NativeBody == null || geometry.Axis.Length < .9)
            { result.Error = "Chưa xác định được trục của nếp chấn để lấy mặt cắt thực tế."; return result; }
            var measure = geometry.Measures.FirstOrDefault(m => m.Error == null && (m.End - m.Start).Length > 1e-9);
            if (measure == null) { result.Error = geometry.Error ?? "Chưa xác định được mốc đo phủ bì của nếp chấn."; return result; }
            var axis = geometry.Axis;
            // A common frame for parallel bends keeps the section upright when
            // the user changes rows; dimensions do not rotate the whole part.
            var reference = Math.Abs(axis.Y) < .9 ? new BendVector(0, 1, 0) : new BendVector(1, 0, 0);
            var x = (reference - axis * BendVector.Dot(reference, axis)).Unit; var y = BendVector.Cross(axis, x).Unit;
            double position = (geometry.Min + geometry.Max) / 2;
            var origin = geometry.Root + axis * position;
            var entry = cache.Find(geometry.Body, axis, position);
            if (entry == null)
            {
                entry = new BendMinimumSectionCache.Entry { Body = geometry.Body, Axis = axis, Position = position, Origin = origin, X = x, Y = y };
                BendMinimumSolidReader.ReadPreviewBoundary(app, geometry.Body.NativeBody, entry, trace);
                cache.Add(entry);
            }
            if (entry.Error != null) { result.Error = entry.Error; return result; }
            result.Curves.AddRange(BendMinimumSectionCache.InFrame(entry, origin, x, y));
            var radii = BendFaces(geometry).Select(f => f.Radius).ToList();
            if (!result.Curves.Any(c => c.IsArc && c.Center.Length < 1e-7 && radii.Any(r => Math.Abs(r - (c.A - c.Center).Length) < 1e-7)))
            { result.Error = "Mặt cắt giữa nếp không đi qua đúng cung chấn đang chọn."; result.Curves.Clear(); return result; }
            result.PositionMm = (position - geometry.Min) * 1000;
            result.Bend = new SectionProfilePlanner.Point(0, 0);
            Func<BendVector, SectionProfilePlanner.Point> project = p => new SectionProfilePlanner.Point(BendVector.Dot(p - origin, x), BendVector.Dot(p - origin, y));
            for (int i = 0; i < geometry.Measures.Count; i++)
            {
                var m = geometry.Measures[i]; if (m.Error != null) continue;
                // A and B are virtual support-plane extensions. Confirm both
                // support planes intersect this real section before overlaying.
                var direction = (m.End - m.Start).Unit;
                var ends = entry.Curves.SelectMany(c => new[] { c.A, c.B }).Select(p => entry.Origin + entry.X * p.X + entry.Y * p.Y).ToList();
                bool low = ends.Any(p => Math.Abs(BendVector.Dot(p - m.Start, direction)) < 1e-7);
                bool high = ends.Any(p => Math.Abs(BendVector.Dot(p - m.End, direction)) < 1e-7);
                if (!low || !high) { result.Error = "Mặt cắt giữa nếp không chứa đủ hai mốc phủ bì. Chưa vẽ kích thước để tránh chỉ sai vị trí."; result.Curves.Clear(); result.Datums.Clear(); return result; }
                result.Datums.Add(new BendMinimumPreviewDatum { Side = i, Start = project(m.Start), End = project(m.End) });
            }
            trace.Mark("preview.end", "curves=" + result.Curves.Count);
            return result;
        }
        private void ClearMarkers()
        {
            trace.Mark("markers.Clear.begin", "count=" + markers.Count);
            foreach (var marker in markers) { try { trace.Mark("callout.Display(false).begin", "number=" + marker.Number); marker.Callout.Display(false); trace.Mark("callout.Display(false).end"); } catch (Exception ex) { trace.Mark("callout.Display(false).exception", ex.ToString()); }
                if (Marshal.IsComObject(marker.Callout)) try { trace.Mark("callout.ReleaseComObject.begin", "number=" + marker.Number); Marshal.ReleaseComObject(marker.Callout); trace.Mark("callout.ReleaseComObject.end"); } catch (InvalidComObjectException ex) { trace.Mark("callout.ReleaseComObject.exception", ex.ToString()); } }
            markers.Clear();
            trace.Mark("markers.Clear.end");
        }
        public void Dispose() { if (disposed) return; trace.Mark("inspection.Dispose.begin"); ClearMarkers(); cache.Clear(); disposed = true; try { trace.Mark("dispose.GraphicsRedraw2.begin"); viewport.GraphicsRedraw2(); trace.Mark("dispose.GraphicsRedraw2.end"); } catch (Exception ex) { trace.Mark("dispose.GraphicsRedraw2.exception", ex.ToString()); } trace.Mark("inspection.Dispose.end"); }
    }

    internal static partial class BendMinimumSolidReader
    {
        // Visualization only. The result and outside dimensions still come from
        // the captured panel graph; no section classification runs here.
        internal static void ReadPreviewBoundary(ISldWorks app, Body2 body, BendMinimumSectionCache.Entry entry, BendMinimumNativeTrace trace = null)
        {
            Body2 solid = null, sheet = null; Surface plane = null; object[] sections = null;
            try
            {
                trace?.Mark("section.GetModeler.begin");
                var modeler = app.GetModeler() as Modeler;
                trace?.Mark("section.GetModeler.end");
                trace?.Mark("section.CreatePlanarSurface2.begin");
                plane = modeler.CreatePlanarSurface2(entry.Origin.Array, entry.Axis.Array, entry.X.Array) as Surface;
                trace?.Mark("section.CreatePlanarSurface2.end");
                trace?.Mark("section.GetBodyBox.begin");
                var box = body.GetBodyBox() as double[];
                trace?.Mark("section.GetBodyBox.end");
                if (box == null || plane == null) throw new InvalidOperationException("Không tạo được mặt phẳng cắt tạm.");
                double size = Math.Max(.01, new BendVector(box[3] - box[0], box[4] - box[1], box[5] - box[2]).Length * 2);
                trace?.Mark("section.CreateSheetFromSurface.begin"); sheet = modeler.CreateSheetFromSurface(plane, new[] { -size, size, -size, size }) as Body2; trace?.Mark("section.CreateSheetFromSurface.end");
                trace?.Mark("section.Body.Copy.begin"); solid = body.Copy() as Body2; trace?.Mark("section.Body.Copy.end");
                if (sheet == null || solid == null) throw new InvalidOperationException("Không tạo được bản sao body để đọc mặt cắt.");
                trace?.Mark("section.Operations2.begin");
                int error; sections = sheet.Operations2((int)swBodyOperationType_e.SWBODYINTERSECT, solid, out error) as object[];
                trace?.Mark("section.Operations2.end", "error=" + error + " bodies=" + sections?.Length);
                if (error != 0 || sections == null || sections.Length == 0) throw new InvalidOperationException("Không đọc được mặt cắt thực tế (kernel " + error + ").");
                foreach (var item in sections)
                {
                    trace?.Mark("section.GetEdges.begin"); var edges = (item as Body2)?.GetEdges() as object[] ?? new object[0]; trace?.Mark("section.GetEdges.end", "edges=" + edges.Length);
                    foreach (var edge in edges)
                    {
                        trace?.Mark("section.ReadCurve.begin", "curve=" + entry.Curves.Count);
                        var curve = ReadCurve(edge as Edge, entry.Origin, entry.X, entry.Y);
                        trace?.Mark("section.ReadCurve.end");
                        if (curve == null) throw new InvalidOperationException("Mặt cắt có spline/ellipse chưa hỗ trợ hiển thị.");
                        entry.Curves.Add(curve);
                    }
                }
                if (entry.Curves.Count == 0) throw new InvalidOperationException("Mặt cắt không có đường biên.");
            }
            catch (Exception ex) { trace?.Mark("section.exception", ex.ToString()); entry.Error = ex.Message; entry.Curves.Clear(); }
            finally {
                if (sections != null) foreach (var item in sections) { trace?.Mark("section.result.ReleaseTemporary.begin"); ReleaseTemporary(item); trace?.Mark("section.result.ReleaseTemporary.end"); }
                trace?.Mark("section.solid.ReleaseTemporary.begin"); ReleaseTemporary(solid); trace?.Mark("section.solid.ReleaseTemporary.end");
                trace?.Mark("section.sheet.ReleaseTemporary.begin"); ReleaseTemporary(sheet); trace?.Mark("section.sheet.ReleaseTemporary.end");
                trace?.Mark("section.plane.ReleaseTemporary.begin"); ReleaseTemporary(plane); trace?.Mark("section.plane.ReleaseTemporary.end");
            }
        }
    }
}
