using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.Kadai
{
    // Principal-axis sections in panel Part coordinates, displayed in the active model.
    internal sealed class KadaiPreviewBackend : IKadaiCadBackend, IDisposable
    {
        internal sealed class SlottedLayout
        {
            public KadaiPlan Plan;
            public List<KadaiProfile3D> Profiles;
        }

        private readonly ISldWorks app;
        private readonly int threadId = Thread.CurrentThread.ManagedThreadId;
        private readonly List<Body2> visibleBodies = new List<Body2>();
        private ModelDoc2 previewDocument;
        private object previewContext;
        internal int VisibleBodyCount { get { return visibleBodies.Count; } }
        internal KadaiPreviewBackend(ISldWorks app) { this.app = app; }
        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != threadId)
                throw new InvalidOperationException("Kadai CAD calls must remain on their owning STA/UI thread.");
        }
        public KadaiPanelSource CaptureSource(ISldWorks a, ModelDoc2 d)
        {
            return CaptureSelectedFace(d, "panel");
        }

        private KadaiPanelSource CaptureSelectedFace(ModelDoc2 d, string role)
        {
            CheckThread();
            var assembly = d as AssemblyDoc;
            if (d == null || (assembly == null && d.GetType() != (int)swDocumentTypes_e.swDocPART))
                throw new InvalidOperationException("Mở Assembly chứa panel hoặc mở Part panel.");
            var sel = (SelectionMgr)d.SelectionManager;
            if (sel.GetSelectedObjectCount2(-1) != 1 || !(sel.GetSelectedObject6(1, -1) is Face2))
                throw new InvalidOperationException("Chọn đúng một mặt " + role + " trong vùng đồ họa.");
            var face = (Face2)sel.GetSelectedObject6(1, -1);
            ModelDoc2 part = d;
            Component2 component = null;
            double[] placement = null;
            if (assembly != null)
            {
                if (!IsAssemblyEditingRoot(assembly))
                    throw new InvalidOperationException("Thoát Edit Part/Edit Component rồi chọn lại mặt panel.");
                component = sel.GetSelectedObjectsComponent3(1, -1) as Component2;
                if (component == null || component.GetParent() != null)
                    throw new InvalidOperationException("Chọn mặt của Part ở cấp hiện hành. Nếu Part nằm trong cụm con, hãy mở cụm con.");
                part = component.GetModelDoc2() as ModelDoc2;
                if (part == null || part.GetType() != (int)swDocumentTypes_e.swDocPART
                    || part.ConfigurationManager.ActiveConfiguration.Name != component.ReferencedConfiguration)
                    throw new InvalidOperationException("Resolve component được chọn và kiểm tra configuration đang dùng trong Assembly.");
                face = part.Extension.GetCorrespondingEntity2(face) as Face2;
                if (face == null)
                    throw new InvalidOperationException("Mặt đã chọn không có mặt tương ứng trong Part.");
                placement = component.Transform2?.ArrayData as double[];
                if (placement == null || placement.Length != 16 || placement.Any(value => double.IsNaN(value) || double.IsInfinity(value))
                    || Math.Abs(placement[12] - 1) > 1e-9)
                    throw new InvalidOperationException("Không đọc được phép đặt component panel.");
            }
            byte[] id = part.Extension.GetPersistReference3(face) as byte[];
            if (id == null || id.Length == 0) throw new InvalidOperationException("Face không có persistent reference.");
            return new KadaiPanelSource { DocumentPath = d.GetPathName(),
                Configuration = d.ConfigurationManager.ActiveConfiguration.Name,
                DocumentTransient = d, SelectedFaceTransient = face, PanelPart = part,
                PanelConfiguration = part.ConfigurationManager.ActiveConfiguration.Name,
                PanelComponent = component, ComponentPlacement = placement,
                DocumentStamp = d.GetUpdateStamp(), PanelStamp = part.GetUpdateStamp(),
                PersistentReferenceEncoded = Convert.ToBase64String(id) };
        }

        internal KadaiBaseDatum CaptureBase(KadaiPanelSource panel)
        {
            var panelFace = Resolve(panel);
            var selected = CaptureSelectedFace(panel.DocumentTransient, "đế");
            var face = (Face2)selected.SelectedFaceTransient;
            var surface = face.GetSurface() as Surface;
            if (surface == null || !surface.IsPlane())
                throw new InvalidOperationException("Mặt đế phải là mặt phẳng. Chọn lại mặt tỳ của các thanh.");
            var plane = surface.PlaneParams as double[];
            if (plane == null || plane.Length != 6)
                throw new InvalidOperationException("Không đọc được mặt phẳng đế.");
            var normal = IntoPanelFrame(plane.Take(3).ToArray(), selected.ComponentPlacement, panel.ComponentPlacement, false);
            var point = IntoPanelFrame(plane.Skip(3).ToArray(), selected.ComponentPlacement, panel.ComponentPlacement, true);
            int axis = Enumerable.Range(0, 3).OrderByDescending(i => Math.Abs(normal[i])).First();
            if (Enumerable.Range(0, 3).Any(i => i != axis && Math.Abs(normal[i]) > 1e-6))
                throw new InvalidOperationException("Mặt đế đang nghiêng so với các trục của Part panel. Chọn mặt tỳ vuông góc với một trục của panel.");
            var bounds = MeasureBounds(panelFace);
            double planeCoordinate = point[axis];
            if (bounds[axis] < planeCoordinate - 1e-6 && bounds[axis + 3] > planeCoordinate + 1e-6)
                throw new InvalidOperationException("Mặt đế cắt qua panel. Chọn mặt tỳ nằm về một phía của toàn bộ mặt panel.");
            double mid = (bounds[axis] + bounds[axis + 3]) / 2;
            if (Math.Abs(mid - planeCoordinate) <= 1e-6)
                throw new InvalidOperationException("Mặt đế trùng mặt panel. Chọn mặt tỳ khác.");
            return new KadaiBaseDatum { Source = selected, HeightAxis = axis,
                HeightSign = mid > planeCoordinate ? 1 : -1, PlaneCoordinateMm = planeCoordinate * 1000 };
        }

        private static double[] IntoPanelFrame(double[] value, double[] from, double[] panel, bool point)
        {
            // SolidWorks stores three local basis vectors followed by translation.
            // Transform2 has unit scale, so the inverse rotation is the transpose.
            var assembly = from == null ? value : Enumerable.Range(0, 3).Select(i =>
                value[0] * from[i] + value[1] * from[3 + i] + value[2] * from[6 + i]
                + (point ? from[9 + i] : 0)).ToArray();
            if (panel == null) return assembly;
            return Enumerable.Range(0, 3).Select(i => Enumerable.Range(0, 3).Sum(j =>
                (assembly[j] - (point ? panel[9 + j] : 0)) * panel[3 * i + j])).ToArray();
        }

        private static bool IsAssemblyEditingRoot(AssemblyDoc assembly)
        {
            var editComponent = assembly.GetEditTargetComponent();
            return editComponent == null || editComponent.IsRoot();
        }

        internal bool IsSourceCurrent(KadaiPanelSource panel)
        {
            CheckThread();
            var active = app.ActiveDoc as ModelDoc2;
            if (panel == null || active == null || !SameDocument(panel.DocumentTransient, active)
                || active.ConfigurationManager.ActiveConfiguration.Name != panel.Configuration
                || active.GetUpdateStamp() != panel.DocumentStamp
                || panel.PanelPart.ConfigurationManager.ActiveConfiguration.Name != panel.PanelConfiguration
                || panel.PanelPart.GetUpdateStamp() != panel.PanelStamp) return false;
            var component = panel.PanelComponent;
            if (component == null) return true;
            if (!IsAssemblyEditingRoot((AssemblyDoc)active) || component.IsSuppressed()
                || component.ReferencedConfiguration != panel.PanelConfiguration) return false;
            var part = component.GetModelDoc2() as ModelDoc2;
            var placement = component.Transform2?.ArrayData as double[];
            return part != null && SameDocument(part, panel.PanelPart) && placement != null
                && placement.Length == panel.ComponentPlacement.Length
                && placement.Zip(panel.ComponentPlacement, (current, captured) => Math.Abs(current - captured))
                    .All(delta => delta <= 1e-9);
        }

        private Face2 Resolve(KadaiPanelSource panel)
        {
            if (!IsSourceCurrent(panel))
                throw new InvalidOperationException("Assembly, Part panel, configuration hoặc vị trí đã đổi; Capture lại mặt.");
            int error;
            var face = panel.PanelPart.Extension.GetObjectByPersistReference3(
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
            int h = settings.HeightAxis, sign = settings.HeightSign, u = UAxis(h), v = VAxis(h);
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
                    sign * settings.GapToPanelMm * .001);
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
                if (samples.Any(p => sign * p[h] <= bottom + .0001))
                    throw new InvalidOperationException(rib.Id + ": mặt đế ở sai phía panel hoặc quá sát. Kiểm tra hướng cao và mặt đế.");
                var heightDirection = Unit(h); heightDirection[h] = sign;
                var profile = new KadaiProfile3D { RibId = rib.Id, Direction = rib.Direction,
                    ThicknessMeters = (longitudinal ? settings.LongThicknessMm : settings.CrossThicknessMm) * .001,
                    ExtrusionDirection = Unit(across), AlongDirection = Unit(along), HeightDirection = heightDirection,
                    Closed = true, ValidationMessage = "Preview polyline; midpoint tolerance " + settings.PreviewChordToleranceMm + " mm. Manufacturing gate remains closed." };
                profile.PointsWorldMeters.AddRange(samples);
                var bottomEnd = (double[])samples.Last().Clone(); bottomEnd[h] = sign * bottom;
                var bottomStart = (double[])samples.First().Clone(); bottomStart[h] = sign * bottom;
                profile.PointsWorldMeters.Add(bottomEnd); profile.PointsWorldMeters.Add(bottomStart);
                results.Add(profile);
            }
            return results;
        }

        internal SlottedLayout BuildLayout(KadaiRequest settings, KadaiPanelSource panel,
            double seamMm, double clearanceMm, bool autoMargins)
        {
            string rejection = null, suggestion = null;
            var factors = autoMargins ? new[] { 0d, .12, .22, .30, .36, .42 } : new[] { 0d };
            foreach (double factor in factors)
            {
                var adjusted = settings.Snapshot();
                adjusted.LongEndMarginMm = Math.Max(settings.LongEndMarginMm, Math.Ceiling(settings.LengthXmm * factor * 10) / 10);
                adjusted.CrossEndMarginMm = Math.Max(settings.CrossEndMarginMm, Math.Ceiling(settings.LengthZmm * factor * 10) / 10);
                var plan = KadaiPlanner.Build(adjusted);
                List<KadaiProfile3D> profiles = null;
                try
                {
                    profiles = CalculateProfiles(plan, panel);
                    var cuts = KadaiJointGeometry.Build(plan, profiles, seamMm, clearanceMm);
                    return new SlottedLayout { Plan = plan, Profiles = KadaiJointGeometry.NotchedProfiles(plan, profiles, cuts) };
                }
                catch (InvalidOperationException ex)
                {
                    if (rejection == null) rejection = ex.Message;
                    if (profiles != null) suggestion = KadaiJointGeometry.SuggestMargins(plan, profiles);
                }
            }
            throw new InvalidOperationException(rejection + (string.IsNullOrEmpty(suggestion) ? "" : " " + suggestion));
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
            if (document?.GetType() != (int)swDocumentTypes_e.swDocPART)
                throw new InvalidOperationException("Preview trong Assembly cần mặt của component đã Capture.");
            ShowTemporaryPreview(document, ribs, null);
        }

        internal void ShowTemporaryPreview(KadaiPanelSource source, List<KadaiProfile3D> ribs)
        {
            if (!IsSourceCurrent(source))
                throw new InvalidOperationException("Nguồn panel đã đổi; Capture lại mặt.");
            ShowTemporaryPreview(source.DocumentTransient, ribs, source.PanelComponent);
        }

        private void ShowTemporaryPreview(ModelDoc2 document, List<KadaiProfile3D> ribs, Component2 component)
        {
            CheckThread(); ClearTemporaryPreview(null);
            var active = app.ActiveDoc as ModelDoc2;
            if (document == null || active == null || !SameDocument(document, active))
                throw new InvalidOperationException("Kích hoạt lại Assembly hoặc Part chứa panel.");
            bool dirty = document.GetSaveFlag();
            previewDocument = document;
            previewContext = (object)component ?? document;
            try {
                foreach (var rib in ribs) {
                    var body = CreatePreviewBody(rib); visibleBodies.Add(body);
                    // Display3 with a Component2 applies its placement to Part-local geometry.
                    // swTempBodySelectOptionNone = 0. Bodies cannot steal source face selection.
                    int result = body.Display3(previewContext, rib.Direction == KadaiRibDirection.Longitudinal ? 0x70C040 : 0x4090F0, 0);
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
                try { if (previewContext != null) body.Hide(previewContext); } catch (COMException) { }
                finally { ReleaseOwned(body); }
            }
            visibleBodies.Clear();
            try { if (previewDocument != null) previewDocument.GraphicsRedraw2(); } catch (COMException) { }
            previewDocument = null;
            previewContext = null;
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
