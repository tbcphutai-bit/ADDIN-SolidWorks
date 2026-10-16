using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using ADDIN.Commands.DrawingDxfExporting;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Helpers
{
    // Keeps the Task Pane told about the features named MARK of the active Part: one line per feature,
    // read again after every rebuild, rename, undo and configuration change. On request it lights the
    // marks up on the model with SOLIDWORKS' own selection colour, which goes away with the next click.
    // It draws nothing on the model and saves nothing.
    internal sealed class MarkHighlightManager
    {
        private const int MaximumMarks = 400;
        private static MarkHighlightManager current;

        private readonly ISldWorks app;
        private readonly System.Windows.Forms.Control dispatcher;
        private DSldWorksEvents_Event appEvents;
        private DPartDocEvents_Event partEvents;
        private ModelDoc2 tracked;
        private string[] markLines;
        private bool refreshPending, detached;

        internal static MarkHighlightManager Current { get { return current; } }

        // One line per feature named MARK of the active Part, in tree order; null when no Part is active.
        internal IList<string> MarkLines { get { return markLines; } }
        internal event Action MarksChanged;

        internal static MarkHighlightManager Attach(ISldWorks application)
        {
            if (application == null) return current;
            if (current != null && !current.detached && ReferenceEquals(current.app, application)) return current;
            if (current != null) current.Detach();
            current = new MarkHighlightManager(application);
            return current;
        }

        private MarkHighlightManager(ISldWorks application)
        {
            app = application;
            // Events only request a refresh; the work runs after SOLIDWORKS returns to its message loop.
            dispatcher = new System.Windows.Forms.Control();
            IntPtr handle = dispatcher.Handle;
            appEvents = app as DSldWorksEvents_Event;
            if (appEvents != null) appEvents.ActiveDocChangeNotify += OnActiveDocumentChanged;
            RequestRefresh();
        }

        internal void Detach()
        {
            if (detached) return;
            detached = true;
            try { if (appEvents != null) appEvents.ActiveDocChangeNotify -= OnActiveDocumentChanged; }
            catch (COMException) { } catch (InvalidComObjectException) { }
            appEvents = null;
            Unhook();
            tracked = null;
            if (!dispatcher.IsDisposed) dispatcher.Dispose();
            if (ReferenceEquals(current, this)) current = null;
        }

        internal void RequestRefresh()
        {
            if (detached || refreshPending || dispatcher.IsDisposed || !dispatcher.IsHandleCreated) return;
            refreshPending = true;
            dispatcher.BeginInvoke((Action)RefreshNow);
        }

        // Selects the edges of every mark (and segments of every 2D sketch mark), so SOLIDWORKS shows them
        // in its selection colour until the next click on the model. Returns the number of marks.
        internal int ShowMarks()
        {
            if (detached) return 0;
            ModelDoc2 model = app.ActiveDoc as ModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocPART) return 0;
            List<List<IFace2>> marks = Marks(model);
            var edges = new List<DispatchWrapper>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (List<IFace2> mark in marks)
                foreach (IFace2 face in mark)
                    foreach (IEdge edge in (face.GetEdges() as object[] ?? new object[0]).OfType<IEdge>())
                    {
                        // Two walls of one cut share an edge: pass it once.
                        string key = EdgeKey(edge);
                        if (key.Length > 0 && seen.Add(key)) edges.Add(new DispatchWrapper(edge));
                    }
            int sketchMarks = CollectSketchMarks(model, edges);
            model.ClearSelection2(true);
            if (edges.Count > 0) model.Extension.MultiSelect2(edges.ToArray(), false, null);
            model.GraphicsRedraw2();
            return marks.Count + sketchMarks;
        }

        private static int CollectSketchMarks(ModelDoc2 model, List<DispatchWrapper> targets)
        {
            int count = 0;
            var recognizer = new LaserMarkFeatureRecognizer();
            for (var feature = model.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
            {
                LaserMarkFeatureRecognizer.Resolution mark;
                if (!recognizer.TryResolve(feature, out mark)) continue;
                if (mark.SourceKind != LaserMarkFeatureKind.Sketch2D) continue;
                if (!string.Equals(mark.SourceName, feature.Name ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if (feature.IsSuppressed()) continue;
                    var sketch = feature.GetSpecificFeature2() as ISketch;
                    int added = 0;
                    if (sketch != null)
                    {
                        foreach (ISketchSegment segment in (sketch.GetSketchSegments() as object[] ?? new object[0]).OfType<ISketchSegment>())
                        {
                            if (segment.ConstructionGeometry) continue;
                            targets.Add(new DispatchWrapper(segment));
                            added++;
                        }
                    }
                    if (added == 0) targets.Add(new DispatchWrapper(feature));
                    count++;
                }
                catch (COMException) { }
            }
            return count;
        }

        private int OnActiveDocumentChanged() { RequestRefresh(); return 0; }
        private int OnRegenerated(object stopFeature) { RequestRefresh(); return 0; }
        private int OnChanged() { RequestRefresh(); return 0; }
        private int OnRenamed(int entityType, string oldName, string newName) { RequestRefresh(); return 0; }
        private int OnDestroyed(int destroyType)
        {
            // The document is going away: do not call into it again.
            if (destroyType == (int)swDestroyNotifyType_e.swDestroyNotifyDestroy)
            {
                Unhook();
                tracked = null;
            }
            RequestRefresh();
            return 0;
        }

        private void Hook(ModelDoc2 model)
        {
            partEvents = model as DPartDocEvents_Event;
            if (partEvents == null) return;
            partEvents.RegenPostNotify2 += OnRegenerated;
            partEvents.ActiveConfigChangePostNotify += OnChanged;
            partEvents.UndoPostNotify += OnChanged;
            partEvents.RedoPostNotify += OnChanged;
            partEvents.RenameItemNotify += OnRenamed;
            partEvents.DestroyNotify2 += OnDestroyed;
        }

        private void Unhook()
        {
            DPartDocEvents_Event events = partEvents;
            partEvents = null;
            if (events == null) return;
            try
            {
                events.RegenPostNotify2 -= OnRegenerated;
                events.ActiveConfigChangePostNotify -= OnChanged;
                events.UndoPostNotify -= OnChanged;
                events.RedoPostNotify -= OnChanged;
                events.RenameItemNotify -= OnRenamed;
                events.DestroyNotify2 -= OnDestroyed;
            }
            catch (COMException) { } catch (InvalidComObjectException) { }
        }

        private bool SameDocument(ModelDoc2 first, ModelDoc2 second)
        {
            if (ReferenceEquals(first, second)) return true;
            if (first == null || second == null) return false;
            try { return app.IsSame(first, second) == (int)swObjectEquality.swObjectSame; }
            catch (COMException) { return false; } catch (InvalidComObjectException) { return false; }
        }

        private void RefreshNow()
        {
            refreshPending = false;
            if (detached) return;
            string[] lines = null;
            try
            {
                ModelDoc2 active = app.ActiveDoc as ModelDoc2;
                if (active != null && active.GetType() != (int)swDocumentTypes_e.swDocPART) active = null;
                if (!SameDocument(active, tracked))
                {
                    Unhook();
                    tracked = active;
                    if (tracked != null) Hook(tracked);
                }
                if (tracked != null) lines = Describe(tracked);
            }
            catch (COMException ex) { Debug.WriteLine("[MARK] " + ex.Message); }
            catch (InvalidComObjectException)
            {
                partEvents = null;
                tracked = null;
            }
            if (lines == null ? markLines == null : markLines != null && lines.SequenceEqual(markLines)) return;
            markLines = lines;
            Action changed = MarksChanged;
            if (changed != null) changed();
        }

        // Reads the feature tree, not the faces. On a flattened body SOLIDWORKS can hand the wall of a hole
        // to another feature, and differently for the Flatten button and the flat-pattern configuration;
        // the names stay true in every state.
        private static string[] Describe(ModelDoc2 model)
        {
            var lines = new List<string>();
            var recognizer = new LaserMarkFeatureRecognizer();
            bool afterFlatPattern = false;
            for (var feature = model.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
            {
                if (string.Equals(feature.GetTypeName2(), "FlatPattern", StringComparison.OrdinalIgnoreCase)) { afterFlatPattern = true; continue; }
                LaserMarkFeatureRecognizer.Resolution mark;
                if (!recognizer.TryResolve(feature, out mark)) continue;
                string name = feature.Name ?? "";
                if (!string.Equals(mark.SourceName, name, StringComparison.OrdinalIgnoreCase)) continue;
                string what = string.Equals(mark.MarkedName, name, StringComparison.OrdinalIgnoreCase)
                    ? Shape(feature, mark)
                    : "pattern của " + mark.MarkedName;
                string state = afterFlatPattern ? " (ở hình trải)" : feature.IsSuppressed() ? " (đang suppress)" : "";
                lines.Add(what + " — " + name + state);
            }
            return lines.ToArray();
        }

        // What the feature draws itself: round holes by diameter, anything else as a slot, or 2D sketch segments.
        private static string Shape(Feature feature, LaserMarkFeatureRecognizer.Resolution mark)
        {
            if (mark.MarkedKind == LaserMarkFeatureKind.Sketch2D)
            {
                int segCount = 0;
                try
                {
                    var directSketch = feature.GetSpecificFeature2() as ISketch;
                    if (directSketch != null)
                    {
                        foreach (ISketchSegment seg in (directSketch.GetSketchSegments() as object[] ?? new object[0]).OfType<ISketchSegment>())
                            if (!seg.ConstructionGeometry) segCount++;
                    }
                }
                catch (COMException) { }
                return segCount > 0 ? "sketch 2D (" + segCount + " nét)" : "sketch 2D";
            }
            var holes = new SortedDictionary<double, int>();    // diameter in mm -> number of holes
            bool slot = false;
            try
            {
                Feature sub = feature.GetFirstSubFeature() as Feature;
                var wizard = feature.GetDefinition() as IWizardHoleFeatureData2;
                if (wizard != null)
                {
                    // Hole Wizard: the first sketch holds one point per hole.
                    var positions = sub == null ? null : sub.GetSpecificFeature2() as ISketch;
                    int points = positions == null ? 0 : (positions.GetSketchPoints2() as object[] ?? new object[0]).Length;
                    double diameter = Math.Round((wizard.HoleDiameter > 0 ? wizard.HoleDiameter : wizard.ThruHoleDiameter) * 1000, 2);
                    if (diameter > 0) holes[diameter] = Math.Max(1, points);
                }
                else
                    for (; sub != null; sub = sub.GetNextSubFeature() as Feature)
                    {
                        var sketch = sub.GetSpecificFeature2() as ISketch;
                        if (sketch == null) continue;
                        foreach (object segment in sketch.GetSketchSegments() as object[] ?? new object[0])
                        {
                            if (((ISketchSegment)segment).ConstructionGeometry) continue;
                            var arc = segment as ISketchArc;
                            if (arc == null || arc.IsCircle() != 1) { slot = true; continue; }
                            double diameter = Math.Round(arc.GetRadius() * 2000, 2);
                            int known;
                            holes[diameter] = (holes.TryGetValue(diameter, out known) ? known : 0) + 1;
                        }
                    }
            }
            catch (COMException) { }
            List<string> parts = holes.Select(h => "lỗ Ø" + h.Key.ToString("0.##", CultureInfo.InvariantCulture) + " ×" + h.Value).ToList();
            if (slot) parts.Add("rãnh");
            return parts.Count > 0 ? string.Join(", ", parts) : LaserMarkFeatureRecognizer.DescribeKind(mark.MarkedKind);
        }

        // One entry per separate cut of a feature named MARK on the body as it is shown now: the walls of that cut.
        private static List<List<IFace2>> Marks(ModelDoc2 model)
        {
            var marks = new List<List<IFace2>>();
            var recognizer = new LaserMarkFeatureRecognizer();
            for (var feature = model.FirstFeature() as Feature; feature != null && marks.Count < MaximumMarks; feature = feature.GetNextFeature() as Feature)
            {
                LaserMarkFeatureRecognizer.Resolution mark;
                if (!recognizer.TryResolve(feature, out mark)) continue;
                // A pattern of a MARK seed resolves with itself as the source: every instance counts.
                if (!string.Equals(mark.SourceName, feature.Name ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                List<IFace2> faces;
                try { faces = (feature.GetFaces() as object[] ?? new object[0]).OfType<IFace2>().ToList(); }
                catch (COMException) { continue; }
                if (faces.Count == 0) continue;     // suppressed in this state: nothing to point at
                List<List<IFace2>> clusters = Clusters(faces);
                // Same convention as the DXF export: next to its Ø6 holes, holes of another size are not marks.
                List<double?> holes = clusters.Select(HoleRadius).ToList();
                bool onlyMarkSize = LaserMarkFeatureRecognizer.OnlyMarkSizeIsMark(holes.Where(r => r.HasValue).Select(r => r.Value));
                for (int index = 0; index < clusters.Count && marks.Count < MaximumMarks; index++)
                {
                    if (onlyMarkSize && holes[index].HasValue && !LaserMarkFeatureRecognizer.IsMarkHoleRadius(holes[index].Value)) continue;
                    marks.Add(clusters[index]);
                }
            }
            return marks;
        }

        // Walls of the same slot touch, pattern instances do not.
        private static List<List<IFace2>> Clusters(List<IFace2> faces)
        {
            int count = faces.Count;
            var boxes = new double[count][];
            var parent = new int[count];
            for (int i = 0; i < count; i++)
            {
                parent[i] = i;
                try { boxes[i] = faces[i].GetBox() as double[]; } catch (COMException) { }
            }
            Func<int, int> find = null;
            find = i => parent[i] == i ? i : (parent[i] = find(parent[i]));
            const double touch = 5e-5;
            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                {
                    double[] a = boxes[i], b = boxes[j];
                    if (a == null || b == null || a.Length < 6 || b.Length < 6) continue;
                    if (a[0] <= b[3] + touch && b[0] <= a[3] + touch && a[1] <= b[4] + touch && b[1] <= a[4] + touch &&
                        a[2] <= b[5] + touch && b[2] <= a[5] + touch)
                        parent[find(i)] = find(j);
                }
            return Enumerable.Range(0, count).GroupBy(find).Select(g => g.Select(i => faces[i]).ToList()).ToList();
        }

        // Radius of a round hole (every wall on a cylinder of one radius), else null.
        private static double? HoleRadius(List<IFace2> cluster)
        {
            double? radius = null;
            try
            {
                foreach (IFace2 face in cluster)
                {
                    var surface = face.GetSurface() as ISurface;
                    var c = surface != null && surface.IsCylinder() ? surface.CylinderParams as double[] : null;
                    if (c == null || c.Length < 7) return null;
                    if (radius.HasValue && Math.Abs(radius.Value - c[6]) > LaserMarkFeatureRecognizer.HoleRadiusToleranceMeters) return null;
                    radius = c[6];
                }
            }
            catch (COMException) { return null; }
            return radius;
        }

        // End points and parameter range of the edge, to 0.1 µm: the same edge read through two faces gives the same key.
        private static string EdgeKey(IEdge edge)
        {
            double[] p;
            try { p = edge.GetCurveParams2() as double[]; } catch (COMException) { return ""; }
            if (p == null || p.Length < 8) return "";
            return string.Join(",", p.Take(8).Select(v => Math.Round(v * 1e7).ToString(CultureInfo.InvariantCulture)));
        }
    }
}
