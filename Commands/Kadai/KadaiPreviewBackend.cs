using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.Kadai
{
    // P2: principal-axis sections of one trimmed face in a Part. The polyline is
    // a measured preview approximation, never a certified manufacturing contour.
    internal sealed class KadaiPreviewBackend : IKadaiCadBackend, IDisposable
    {
        private readonly ISldWorks app;
        private readonly int threadId = Thread.CurrentThread.ManagedThreadId;
        private readonly List<Body2> visibleBodies = new List<Body2>();
        private ModelDoc2 previewDocument;
        internal int VisibleBodyCount { get { return visibleBodies.Count; } }
        internal KadaiPreviewBackend(ISldWorks app) { this.app = app; }
        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != threadId)
                throw new InvalidOperationException("Kadai CAD calls must remain on their owning STA/UI thread.");
        }
        public KadaiPanelSource CaptureSource(ISldWorks a, ModelDoc2 d)
        {
            CheckThread();
            if (d == null || d.GetType() != (int)swDocumentTypes_e.swDocPART)
                throw new InvalidOperationException("P2: mở Part chứa panel; chọn đúng một Face. Assembly transforms chưa được kiểm chứng.");
            var sel = (SelectionMgr)d.SelectionManager;
            if (sel.GetSelectedObjectCount2(-1) != 1 || !(sel.GetSelectedObject6(1, -1) is Face2))
                throw new InvalidOperationException("Chọn đúng một mặt panel (Face), rồi Capture face.");
            var face = (Face2)sel.GetSelectedObject6(1, -1);
            byte[] id = d.Extension.GetPersistReference3(face) as byte[];
            if (id == null || id.Length == 0) throw new InvalidOperationException("Face không có persistent reference.");
            return new KadaiPanelSource { DocumentPath = d.GetPathName(),
                Configuration = d.ConfigurationManager.ActiveConfiguration.Name,
                DocumentTransient = d, SelectedFaceTransient = face,
                PersistentReferenceEncoded = Convert.ToBase64String(id) };
        }
        private Face2 Resolve(KadaiPanelSource panel)
        {
            CheckThread();
            if (panel == null || panel.DocumentTransient == null || app.ActiveDoc == null
                || !SameDocument(panel.DocumentTransient, (ModelDoc2)app.ActiveDoc))
                throw new InvalidOperationException("Panel document đã đổi hoặc đã đóng; Capture lại mặt.");
            if (panel.DocumentTransient.ConfigurationManager.ActiveConfiguration.Name != panel.Configuration)
                throw new InvalidOperationException("Configuration đã đổi; Capture lại mặt.");
            int error;
            var face = panel.DocumentTransient.Extension.GetObjectByPersistReference3(
                Convert.FromBase64String(panel.PersistentReferenceEncoded), out error) as Face2;
            if (face == null || error != 0) throw new InvalidOperationException("Panel reference mất; Capture lại mặt.");
            return face;
        }
        private bool SameDocument(ModelDoc2 a, ModelDoc2 b)
        {
            return app.IsSame(a, b) == (int)swObjectEquality.swObjectSame;
        }
        internal static int UAxis(int height) { return height == 0 ? 1 : 0; }
        internal static int VAxis(int height) { return height == 2 ? 1 : 2; }
        internal static double[] Unit(int axis) { var p = new double[3]; p[axis] = 1; return p; }
        internal double[] MeasureBounds(Face2 face)
        {
            CheckThread();
            Body2 sheet = null;
            try {
                sheet = face.CreateSheetBody() as Body2;
                if (sheet == null) throw new InvalidOperationException("Không đo được biên face.");
                var bounds = new double[6];
                for (int axis = 0; axis < 3; axis++) foreach (int sign in new[] { -1, 1 }) {
                    var direction = Unit(axis); direction[axis] = sign;
                    double x, y, z;
                    if (!sheet.GetExtremePoint(direction[0], direction[1], direction[2], out x, out y, out z))
                        throw new InvalidOperationException("Không đo được cực trị face.");
                    bounds[axis + (sign > 0 ? 3 : 0)] = new[] { x, y, z }[axis];
                }
                return bounds;
            } finally { ReleaseOwned(sheet); }
        }
        public List<KadaiProfile3D> CalculateProfiles(KadaiPlan plan, KadaiPanelSource panel)
        {
            CheckThread();
            var face = Resolve(panel);
            if (face.GetLoopCount() != 1)
                throw new NotSupportedException("P2 chưa hỗ trợ face có lỗ/nhiều loop; chọn một face liên tục.");
            var surface = (Surface)face.GetSurface();
            var modeler = (Modeler)app.GetModeler();
            var settings = plan.Settings;
            int h = settings.HeightAxis, u = UAxis(h), v = VAxis(h);
            double[] box = (double[])face.GetBox();
            var results = new List<KadaiProfile3D>();
            foreach (var rib in plan.LongRibs.Concat(plan.CrossRibs))
            {
                bool longitudinal = rib.Direction == KadaiRibDirection.Longitudinal;
                int along = longitudinal ? v : u, across = longitudinal ? u : v;
                double start = (longitudinal ? settings.OriginVmm : settings.OriginUmm) * .001;
                double span = (longitudinal ? settings.LengthZmm : settings.LengthXmm) * .001;
                double fixedCoordinate = ((longitudinal ? settings.OriginUmm : settings.OriginVmm) + rib.CoordinateMm) * .001;
                Func<double, double[]> intersect = coordinate => Intersect(face, surface, modeler,
                    h, along, across, coordinate, fixedCoordinate, box[h] - 1, box[h + 3] + 1,
                    settings.GapToPanelMm * .001);
                var samples = new List<double[]>();
                int first = -1, last = -1;
                // Discover one connected projected interval; never bridge a hole or
                // silently join two surface branches. Boundary refinement is 0.01 mm.
                var grid = new double[65][];
                for (int i = 0; i < grid.Length; i++) {
                    grid[i] = intersect(start + span * i / 64);
                    if (grid[i] != null) { if (first < 0) first = i; last = i; }
                }
                if (first < 0 || first == last) throw new InvalidOperationException(rib.Id + ": mặt không giao footprint hoặc quá hẹp.");
                for (int i = first; i <= last; i++)
                    if (grid[i] == null) throw new InvalidOperationException(rib.Id + ": section bị tách; dừng preview.");
                if (first > 0) samples.Add(Boundary(intersect, start + span * (first - 1) / 64, grid[first], along));
                samples.Add(grid[first]);
                for (int i = first + 1; i <= last; i++)
                    Refine(intersect, samples, samples[samples.Count - 1], grid[i], along, h,
                        settings.PreviewChordToleranceMm * .001, 0);
                if (last < 64) {
                    var end = Boundary(intersect, start + span * (last + 1) / 64, grid[last], along);
                    Refine(intersect, samples, samples[samples.Count - 1], end, along, h,
                        settings.PreviewChordToleranceMm * .001, 0);
                }
                double bottom = settings.BottomMm * .001;
                if (samples.Any(p => p[h] <= bottom + .0001))
                    throw new InvalidOperationException(rib.Id + ": datum đáy nằm trên/tiếp xúc panel.");
                var profile = new KadaiProfile3D { RibId = rib.Id, Direction = rib.Direction,
                    ThicknessMeters = (longitudinal ? settings.LongThicknessMm : settings.CrossThicknessMm) * .001,
                    ExtrusionDirection = Unit(across), AlongDirection = Unit(along), HeightDirection = Unit(h),
                    Closed = true, ValidationMessage = "Preview polyline; midpoint tolerance " + settings.PreviewChordToleranceMm + " mm. Manufacturing gate remains closed." };
                profile.PointsWorldMeters.AddRange(samples);
                var bottomEnd = (double[])samples.Last().Clone(); bottomEnd[h] = bottom;
                var bottomStart = (double[])samples.First().Clone(); bottomStart[h] = bottom;
                profile.PointsWorldMeters.Add(bottomEnd); profile.PointsWorldMeters.Add(bottomStart);
                results.Add(profile);
            }
            return results;
        }
        private static double[] Boundary(Func<double, double[]> intersect, double outside, double[] inside, int along)
        {
            for (int i = 0; i < 24 && Math.Abs(inside[along] - outside) > .00001; i++) {
                double mid = (inside[along] + outside) * .5;
                var p = intersect(mid); if (p == null) outside = mid; else inside = p;
            }
            return inside;
        }
        private static void Refine(Func<double, double[]> intersect, List<double[]> samples,
            double[] a, double[] b, int along, int height, double tolerance, int depth)
        {
            if (Math.Abs(a[along] - b[along]) < 1e-8) return;
            var mid = intersect((a[along] + b[along]) * .5);
            if (mid == null) throw new InvalidOperationException("Section có khoảng trống; không nối tắt qua biên face.");
            if (Math.Abs(mid[height] - (a[height] + b[height]) * .5) > tolerance) {
                if (depth >= 12 || samples.Count > 4096) throw new InvalidOperationException("Không đạt tolerance trong preview budget.");
                Refine(intersect, samples, a, mid, along, height, tolerance, depth + 1);
                Refine(intersect, samples, mid, b, along, height, tolerance, depth + 1);
            } else samples.Add(b);
        }
        private static double[] Intersect(Face2 face, Surface surface, Modeler modeler,
            int height, int along, int across, double coordinate, double fixedCoordinate,
            double low, double high, double gap)
        {
            var a = new double[3]; a[height] = low; a[along] = coordinate; a[across] = fixedCoordinate;
            var b = (double[])a.Clone(); b[height] = high;
            Curve line = null, bounded = null;
            try {
                line = (Curve)modeler.CreateLine(a, Unit(height));
                bounded = line.CreateTrimmedCurve2(a[0], a[1], a[2], b[0], b[1], b[2]);
                object points = null, parameters = null, uv = null;
                if (bounded == null || !surface.IntersectCurve2(bounded, a.Concat(b).ToArray(), ref points, ref parameters, ref uv)) return null;
                var xyz = points as double[]; if (xyz == null) return null;
                var hits = new List<double[]>();
                for (int i = 0; i + 2 < xyz.Length; i += 3) {
                    var p = new[] { xyz[i], xyz[i + 1], xyz[i + 2] };
                    var q = face.GetClosestPointOn(p[0], p[1], p[2]) as double[];
                    if (q != null && Enumerable.Range(0, 3).Sum(k => (p[k] - q[k]) * (p[k] - q[k])) < 1e-14
                        && !hits.Any(q0 => Math.Abs(q0[height] - p[height]) < 1e-7)) hits.Add(p);
                }
                if (hits.Count > 1) throw new NotSupportedException("Panel không single-valued theo datum đã chọn.");
                if (hits.Count == 0) return null;
                hits[0][height] -= gap; return hits[0];
            } finally { ReleaseOwned(bounded); ReleaseOwned(line); }
        }
        internal Body2 CreatePreviewBody(KadaiProfile3D profile)
        {
            CheckThread();
            var modeler = (Modeler)app.GetModeler();
            var math = (MathUtility)app.GetMathUtility();
            var curves = new List<Curve>();
            var untrimmed = new List<Curve>();
            Surface plane = null; Body2 sheet = null; MathVector normal = null;
            try {
                var points = profile.PointsWorldMeters;
                for (int i = 0; i < points.Count; i++) {
                    var a = (double[])points[i].Clone(); var b = (double[])points[(i + 1) % points.Count].Clone();
                    // The grid coordinate is the rib center plane.
                    for (int k = 0; k < 3; k++) { a[k] -= profile.ExtrusionDirection[k] * profile.ThicknessMeters / 2;
                        b[k] -= profile.ExtrusionDirection[k] * profile.ThicknessMeters / 2; }
                    var direction = Enumerable.Range(0, 3).Select(k => b[k] - a[k]).ToArray();
                    double length = Math.Sqrt(direction.Sum(x => x * x));
                    if (length < 1e-8) throw new InvalidOperationException("Degenerate preview edge.");
                    Curve line = (Curve)modeler.CreateLine(a, direction.Select(x => x / length).ToArray());
                    untrimmed.Add(line);
                    var trim = line.CreateTrimmedCurve2(a[0], a[1], a[2], b[0], b[1], b[2]);
                    if (trim == null) throw new InvalidOperationException("Invalid preview edge."); curves.Add(trim);
                }
                var root = (double[])points[0].Clone();
                for (int k = 0; k < 3; k++) root[k] -= profile.ExtrusionDirection[k] * profile.ThicknessMeters / 2;
                plane = (Surface)modeler.CreatePlanarSurface2(root, profile.ExtrusionDirection, profile.AlongDirection);
                sheet = plane.CreateTrimmedSheet5(curves.ToArray(), true, 1e-7) as Body2;
                if (sheet == null) throw new InvalidOperationException("Cannot close the section profile.");
                normal = (MathVector)math.CreateVector(profile.ExtrusionDirection);
                var solid = modeler.CreateExtrudedBody(sheet, normal, profile.ThicknessMeters);
                if (solid == null || solid.GetType() != 0) { ReleaseOwned(solid); throw new InvalidOperationException("Preview extrusion failed."); }
                return solid;
            } finally { foreach (var c in curves.Concat(untrimmed).Distinct()) ReleaseOwned(c);
                ReleaseOwned(normal); ReleaseOwned(sheet); ReleaseOwned(plane); }
        }
        public void ShowTemporaryPreview(ModelDoc2 document, KadaiPlan plan, List<KadaiProfile3D> ribs)
        {
            CheckThread(); ClearTemporaryPreview(null);
            if (document == null || document.GetType() != 1 || !SameDocument(document, (ModelDoc2)app.ActiveDoc))
                throw new InvalidOperationException("Preview requires its active Part document.");
            bool dirty = document.GetSaveFlag();
            previewDocument = document;
            try {
                foreach (var rib in ribs) {
                    var body = CreatePreviewBody(rib); visibleBodies.Add(body);
                    // swTempBodySelectOptionNone = 0. Bodies cannot steal source face selection.
                    int result = body.Display3(document, rib.Direction == KadaiRibDirection.Longitudinal ? 0x70C040 : 0x4090F0, 0);
                    if (result != 0) throw new InvalidOperationException("Display3 failed: " + result);
                }
                document.GraphicsRedraw2();
                if (document.GetSaveFlag() != dirty) throw new InvalidOperationException("Preview unexpectedly changed document save state.");
            } catch { ClearTemporaryPreview(null); throw; }
        }
        public void ClearTemporaryPreview(ModelDoc2 ignored)
        {
            CheckThread();
            foreach (var body in visibleBodies) {
                try { if (previewDocument != null) body.Hide(previewDocument); } catch (COMException) { }
                finally { ReleaseOwned(body); }
            }
            visibleBodies.Clear();
            try { if (previewDocument != null) previewDocument.GraphicsRedraw2(); } catch (COMException) { }
            previewDocument = null;
        }
        private static void ReleaseOwned(object value)
        {
            // Only locally created Modeler objects; never final-release app/doc/face.
            if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }
        public List<KadaiPartResult> ApplyToNewWorkingCopy(ModelDoc2 d, KadaiPlan p, List<KadaiProfile3D> r)
        { throw new NotSupportedException("Apply locked: exact contour, assembly fit and regeneration have not passed production validation."); }
        public void GenerateManufacturingFiles(List<KadaiPartResult> parts)
        { throw new NotSupportedException("Manufacturing locked until full package validation."); }
        public void Dispose() { ClearTemporaryPreview(null); }
    }
}
