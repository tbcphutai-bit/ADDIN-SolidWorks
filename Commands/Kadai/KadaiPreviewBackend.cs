using System;
using System.Collections.Generic;
using System.IO;
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
            public List<KadaiProfile3D> Blanks;
            public int JoinedCrossings;
            public Dictionary<string, List<KadaiJointCut>> Joints;
        }

        private readonly ISldWorks app;
        private readonly int threadId = Thread.CurrentThread.ManagedThreadId;
        private const int HighlightColor = 0xFFB060;
        private readonly Dictionary<string, (Body2 Body, int Color, double[] Across)> visibleBodies =
            new Dictionary<string, (Body2 Body, int Color, double[] Across)>(StringComparer.Ordinal);
        private ModelDoc2 previewDocument;
        private object previewContext;
        private string highlightedRibId;
        private string isolatedRibId;
        private KadaiUraSide highlightedSide;
        private Body2 uraPreview;
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
            int sign = mid > planeCoordinate ? 1 : -1;
            string materialDatabase;
            string material = ((PartDoc)selected.PanelPart).GetMaterialPropertyName2(selected.PanelConfiguration, out materialDatabase);
            return new KadaiBaseDatum { Source = selected, HeightAxis = axis, HeightSign = sign,
                PlaneCoordinateMm = planeCoordinate * 1000,
                BoundsInPanelMeters = MeasureBounds(face, selected, panel),
                MinPanelHeightMm = sign * (bounds[sign > 0 ? axis : axis + 3] - planeCoordinate) * 1000,
                MaterialName = material, MaterialDatabase = materialDatabase };
        }

        internal static double[] IntoPanelFrame(double[] value, double[] from, double[] panel, bool point)
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
            return MeasureBounds(face, null, null);
        }

        private double[] MeasureBounds(Face2 face, KadaiPanelSource from, KadaiPanelSource to)
        {
            CheckThread();
            Body2 sheet = null;
            try {
                sheet = face.CreateSheetBody() as Body2;
                if (sheet == null) throw new InvalidOperationException("Không đo được biên face.");
                var bounds = new double[6];
                for (int axis = 0; axis < 3; axis++) foreach (int sign in new[] { -1, 1 }) {
                    var direction = Unit(axis); direction[axis] = sign;
                    direction = IntoPanelFrame(direction, to?.ComponentPlacement, from?.ComponentPlacement, false);
                    double x, y, z;
                    if (!sheet.GetExtremePoint(direction[0], direction[1], direction[2], out x, out y, out z))
                        throw new InvalidOperationException("Không đo được cực trị face.");
                    var point = IntoPanelFrame(new[] { x, y, z }, from?.ComponentPlacement, to?.ComponentPlacement, true);
                    bounds[axis + (sign > 0 ? 3 : 0)] = point[axis];
                }
                return bounds;
            } finally { ReleaseOwned(sheet); }
        }
        public List<KadaiProfile3D> CalculateProfiles(KadaiPlan plan, KadaiPanelSource panel)
        {
            return CalculateProfiles(plan, panel, null, 0);
        }

        private List<KadaiProfile3D> CalculateProfiles(KadaiPlan plan, KadaiPanelSource panel,
            KadaiBaseDatum datum, double straightHeightMm)
        {
            CheckThread();
            var face = Resolve(panel);
            if (face.GetLoopCount() != 1)
                throw new NotSupportedException("P2 chưa hỗ trợ face có lỗ/nhiều loop; chọn một face liên tục.");
            var surface = (Surface)face.GetSurface();
            var modeler = (Modeler)app.GetModeler();
            var baseFace = datum == null ? null : Resolve(datum.Source);
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
                bool followsPanel = datum == null || rib.Direction == settings.PanelRibDirection;
                Func<double, double[]> panelAt = coordinate => Intersect(face, surface, modeler, h, along, across,
                    coordinate, fixedCoordinate, box[h] - 1, box[h + 3] + 1, sign * settings.GapToPanelMm * .001);
                List<double[]> samples;
                bool hasPanelContact = false;
                if (baseFace == null)
                {
                    samples = TraceSection(panelAt, start, start + span, along, h, settings.PreviewChordToleranceMm * .001);
                    if (samples.Count == 0)
                        throw new InvalidOperationException(rib.Id + " nằm ngoài mặt panel.");
                }
                else
                {
                    Func<double, double[]> baseAt = coordinate =>
                    {
                        var point = new double[3];
                        point[h] = datum.PlaneCoordinateMm * .001;
                        point[along] = coordinate;
                        point[across] = fixedCoordinate;
                        var onBase = IntoPanelFrame(point, panel.ComponentPlacement, datum.Source.ComponentPlacement, true);
                        if (!IsOnFace(baseFace, onBase)) return null;
                        point[h] += sign * straightHeightMm * .001;
                        return point;
                    };
                    var baseSection = TraceSection(baseAt, start, start + span, along, h, 0);
                    if (baseSection.Count == 0)
                        throw new InvalidOperationException(rib.Id + " nằm ngoài mặt base. Kiểm tra khoảng chừa.");
                    samples = baseSection;
                    if (followsPanel)
                    {
                        var contact = TraceSection(panelAt, baseSection[0][along], baseSection[1][along],
                            along, h, settings.PreviewChordToleranceMm * .001);
                        if (contact.Count > 0)
                        {
                            hasPanelContact = true;
                            // Carry the nearest contact height horizontally to each edge of the base.
                            ExtendToBaseEdge(contact, baseSection[0][along], along, true);
                            ExtendToBaseEdge(contact, baseSection[1][along], along, false);
                            samples = contact;
                        }
                    }
                }
                double bottom = settings.BottomMm * .001;
                if (samples.Any(p => sign * p[h] <= bottom + .0001))
                    throw new InvalidOperationException(rib.Id + ": mặt đế ở sai phía panel hoặc quá sát. Kiểm tra hướng cao và mặt đế.");
                var heightDirection = Unit(h); heightDirection[h] = sign;
                var profile = new KadaiProfile3D { RibId = rib.Id, Direction = rib.Direction,
                    HasPanelContact = hasPanelContact,
                    ThicknessMeters = (longitudinal ? settings.LongThicknessMm : settings.CrossThicknessMm) * .001,
                    ExtrusionDirection = Unit(across), AlongDirection = Unit(along), HeightDirection = heightDirection,
                    Closed = true, ValidationMessage = "Panel section midpoint tolerance " + settings.PreviewChordToleranceMm + " mm." };
                profile.PointsWorldMeters.AddRange(samples);
                var bottomEnd = (double[])samples.Last().Clone(); bottomEnd[h] = sign * bottom;
                var bottomStart = (double[])samples.First().Clone(); bottomStart[h] = sign * bottom;
                profile.PointsWorldMeters.Add(bottomEnd); profile.PointsWorldMeters.Add(bottomStart);
                results.Add(profile);
            }
            return results;
        }

        internal SlottedLayout BuildBaseLayout(KadaiRequest settings, KadaiPanelSource panel, KadaiBaseDatum datum)
        {
            if (datum == null || !IsSourceCurrent(datum.Source))
                throw new InvalidOperationException("Chọn mặt phẳng base trước khi Preview.");
            if (!settings.PanelRibDirection.HasValue || !settings.StartClearanceMm.HasValue || !settings.EndClearanceMm.HasValue)
                throw new ArgumentException("Chọn phương bám panel và nhập hai khoảng chừa.");
            var bounds = datum.BoundsInPanelMeters;
            var request = settings.Snapshot();
            request.HeightAxis = datum.HeightAxis;
            request.HeightSign = datum.HeightSign;
            request.BottomMm = datum.PlaneCoordinateMm * datum.HeightSign;
            int u = UAxis(datum.HeightAxis), v = VAxis(datum.HeightAxis);
            request.OriginUmm = bounds[u] * 1000;
            request.OriginVmm = bounds[v] * 1000;
            request.LengthXmm = (bounds[u + 3] - bounds[u]) * 1000;
            request.LengthZmm = (bounds[v + 3] - bounds[v]) * 1000;
            const double depthClearanceMm = .1;
            double straightHeight = request.FixedHeightMm;
            double thickness = Math.Max(request.LongThicknessMm, request.CrossThicknessMm);
            if (double.IsNaN(straightHeight) || double.IsInfinity(straightHeight) || straightHeight <= 2 * thickness + depthClearanceMm)
                throw new InvalidOperationException("Chiều cao thanh cố định quá thấp để tạo rãnh ghép.");
            if (straightHeight >= datum.MinPanelHeightMm - .05)
                throw new InvalidOperationException("Chiều cao thanh cố định chạm hoặc quá sát panel. Giảm chiều cao.");
            double seamMm = request.JointHeightMm ?? (straightHeight >= 100 + thickness ? 100 : (straightHeight + depthClearanceMm) / 2);
            if (double.IsNaN(seamMm) || double.IsInfinity(seamMm) ||
                seamMm < thickness + depthClearanceMm || seamMm > straightHeight - thickness)
                throw new InvalidOperationException("Chiều cao rãnh từ mặt đế phải chừa đủ vật liệu ở cả hai phía của thanh đỡ.");
            var plan = KadaiPlanner.Build(request);
            var blanks = CalculateProfiles(plan, panel, datum, straightHeight);
            var cuts = KadaiJointGeometry.Build(plan, blanks, seamMm, depthClearanceMm);
            return new SlottedLayout { Plan = plan, Blanks = blanks, Joints = cuts, JoinedCrossings = cuts.Values.Sum(list => list.Count) / 2,
                Profiles = KadaiJointGeometry.NotchedProfiles(plan, blanks, cuts) };
        }

        internal string ApplyBaseLayout(SlottedLayout layout, KadaiPanelSource panel, KadaiBaseDatum datum,
            string parentDirectory, KadaiApplyOptions options, Action<string> progress, Action<ModelDoc2, View> dimensionView)
        {
            CheckThread();
            if (layout == null || !IsSourceCurrent(panel) || datum == null || !IsSourceCurrent(datum.Source))
                throw new InvalidOperationException("Mô hình đã thay đổi. Hãy tạo lại Preview trước khi Apply.");
            var targetAssembly = panel.DocumentTransient;
            var assembly = targetAssembly as AssemblyDoc;
            if (assembly == null || !IsAssemblyEditingRoot(assembly))
                throw new InvalidOperationException("Apply cần Assembly đang mở ở chế độ chỉnh sửa cấp trên cùng.");
            if (targetAssembly.IsOpenedReadOnly() || targetAssembly.IsOpenedViewOnly())
                throw new InvalidOperationException("Assembly đang mở ở chế độ chỉ đọc. Hãy mở lại với quyền chỉnh sửa để Apply.");
            if (!Directory.Exists(parentDirectory))
                throw new InvalidOperationException("Chọn thư mục để lưu bộ chi tiết Kadai.");

            options.Validate(layout.Blanks);
            var openPartPaths = ((app.GetDocuments() as object[]) ?? new object[0]).OfType<ModelDoc2>()
                .Where(doc => doc.GetType() == (int)swDocumentTypes_e.swDocPART)
                .Select(doc => doc.GetPathName());
            var componentPaths = ((assembly.GetComponents(false) as object[]) ?? new object[0]).OfType<Component2>()
                .Select(component => component.GetPathName());
            var occupiedPartNames = openPartPaths.Concat(componentPaths)
                .Where(path => string.Equals(Path.GetExtension(path), ".sldprt", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileNameWithoutExtension)
                .Where(stem => !string.IsNullOrEmpty(stem));
            var conflict = occupiedPartNames.Intersect(layout.Blanks.Select(rib => options.PartName(rib.RibId)),
                StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (conflict != null)
                throw new InvalidOperationException("Tên Part " + conflict + " đã được dùng trong Assembly hoặc tài liệu đang mở.");

            const string bendTable = @"C:\sw共有ライブラリ\テーブル\菊川ベンドテーブル_170905\BD_Kiku_ST_2020.xlsx";
            if (!File.Exists(bendTable))
                throw new InvalidOperationException("Không tìm thấy bảng uốn BD_Kiku_ST_2020.xlsx.");

            string name = "Kadai-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")
                + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            string target = Path.Combine(parentDirectory, name);
            ClearTemporaryPreview(null);
            try
            {
                var writer = new KadaiNativePrototype(app, options.MaterialDatabase, options.MaterialName, bendTable,
                    options.PartTemplate, options.DrawingTemplate);
                writer.InsertLayout(layout, targetAssembly, target, panel, progress, options, dimensionView);
                return target;
            }
            catch (Exception ex)
            {
                if (Directory.Exists(target))
                    throw new InvalidOperationException(ex.Message + " Gói chưa hoàn tất: " + target, ex);
                throw;
            }
            finally
            {
                int error = 0;
                app.ActivateDoc3(panel.DocumentTransient.GetTitle(), false, 1, ref error);
            }
        }

        internal string ResolveMaterialDatabase(string database)
        {
            if (string.IsNullOrWhiteSpace(database)) return null;
            if (Path.IsPathRooted(database) && File.Exists(database)) return database;
            const string companyDatabase = @"C:\sw共有ライブラリ\材料ﾃﾞｰﾀﾍﾞｰｽ\kiku_板材_材料 2019.sldmat";
            string name = Path.GetFileNameWithoutExtension(database);
            var registered = app.GetMaterialDatabases() as object[] ?? new object[0];
            var matches = registered.OfType<string>().Concat(new[] { companyDatabase })
                .Where(path => File.Exists(path) && string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        private static List<double[]> TraceSection(Func<double, double[]> at, double start, double end,
            int along, int height, double chordTolerance)
        {
            var samples = new List<double[]>();
            const int intervals = 64;
            double step = (end - start) / intervals;
            var grid = new double[intervals + 1][];
            int first = -1, last = -1;
            for (int i = 0; i <= intervals; i++)
            {
                grid[i] = at(i == intervals ? end : start + step * i);
                if (grid[i] != null) { if (first < 0) first = i; last = i; }
            }
            if (first < 0) return samples;
            if (first == last)
                throw new InvalidOperationException("Vùng tiếp xúc quá hẹp để dựng một thanh.");
            for (int i = first + 1; i < last; i++)
                if (grid[i] == null)
                    throw new InvalidOperationException("Mặt được chọn tạo đoạn thanh bị ngắt. Kiểm tra biên base và panel.");
            var left = first == 0 ? grid[first] : Boundary(at, start + step * (first - 1), grid[first], along);
            var right = last == intervals ? grid[last] : Boundary(at, start + step * (last + 1), grid[last], along);
            samples.Add(left);
            if (chordTolerance == 0) samples.Add(right);
            else
            {
                for (int i = first; i <= last; i++)
                    Refine(at, samples, samples[samples.Count - 1], grid[i], along, height, chordTolerance, 0);
                Refine(at, samples, samples[samples.Count - 1], right, along, height, chordTolerance, 0);
            }
            return samples;
        }

        private static void ExtendToBaseEdge(List<double[]> top, double coordinate, int along, bool first)
        {
            int index = first ? 0 : top.Count - 1;
            if (Math.Abs(top[index][along] - coordinate) <= 1e-7)
            {
                top[index][along] = coordinate;
                return;
            }
            var end = (double[])top[index].Clone();
            end[along] = coordinate;
            if (first) top.Insert(0, end);
            else top.Add(end);
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
            if (Math.Abs(a[along] - b[along]) <= 1e-7) return;
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
                    if (IsOnFace(face, p)
                        && !hits.Any(q0 => Math.Abs(q0[height] - p[height]) < 1e-7)) hits.Add(p);
                }
                if (hits.Count > 1) throw new NotSupportedException("Panel không single-valued theo datum đã chọn.");
                if (hits.Count == 0) return null;
                hits[0][height] -= gap; return hits[0];
            } finally { ReleaseOwned(bounded); ReleaseOwned(line); }
        }

        private static bool IsOnFace(Face2 face, double[] point)
        {
            var closest = face.GetClosestPointOn(point[0], point[1], point[2]) as double[];
            return closest != null && Enumerable.Range(0, 3).Sum(i =>
                (point[i] - closest[i]) * (point[i] - closest[i])) < 1e-14;
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
                if (profile.OrientationHole == null) return solid;
                try { return CutPreviewHole(modeler, solid, profile); }
                finally { ReleaseOwned(solid); }
            } finally { foreach (var c in curves.Concat(untrimmed).Distinct()) ReleaseOwned(c);
                ReleaseOwned(normal); ReleaseOwned(sheet); ReleaseOwned(plane); }
        }

        private static Body2 CutPreviewHole(Modeler modeler, Body2 solid, KadaiProfile3D profile)
        {
            var hole = profile.OrientationHole;
            var origin = profile.PointsWorldMeters[profile.PointsWorldMeters.Count - 1];
            var axis = profile.ExtrusionDirection;
            const double overrun = .001;
            var center = Enumerable.Range(0, 3).Select(i => origin[i] + hole.X * profile.AlongDirection[i]
                + hole.Y * profile.HeightDirection[i] - axis[i] * (profile.ThicknessMeters / 2 + overrun));
            var parameters = center.Concat(axis).Concat(new[] { hole.Diameter / 2, profile.ThicknessMeters + 2 * overrun }).ToArray();
            var punch = modeler.CreateBodyFromCyl(parameters) as Body2;
            if (punch == null) throw new InvalidOperationException("Không dựng được lỗ định hướng Preview.");
            try
            {
                var results = (solid.Operations2((int)swBodyOperationType_e.SWBODYCUT, punch, out int error)
                    as object[] ?? new object[0]).OfType<Body2>().ToArray();
                if (error == 0 && results.Length == 1) return results[0];
                foreach (var body in results) ReleaseOwned(body);
                throw new InvalidOperationException("Không cắt được lỗ định hướng Preview cho " + profile.RibId + ".");
            }
            finally { ReleaseOwned(punch); }
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
                    int color = rib.Direction == KadaiRibDirection.Longitudinal ? 0x70C040 : 0x4090F0;
                    var body = CreatePreviewBody(rib);
                    visibleBodies.Add(rib.RibId, (body, color, rib.ExtrusionDirection));
                    // Display3 with a Component2 applies its placement to Part-local geometry.
                    // swTempBodySelectOptionNone = 0. Bodies cannot steal source face selection.
                    DisplayPreviewBody(body, color);
                }
                document.GraphicsRedraw2();
                if (document.GetSaveFlag() != dirty) throw new InvalidOperationException("Preview unexpectedly changed document save state.");
            } catch { ClearTemporaryPreview(null); throw; }
        }

        internal void SetHighlightedRib(string ribId, KadaiUraSide side)
        {
            CheckThread();
            if (ribId == highlightedRibId && side == highlightedSide) return;
            var active = app.ActiveDoc as ModelDoc2;
            if (previewDocument == null || active == null || !SameDocument(previewDocument, active))
                throw new InvalidOperationException("Kích hoạt lại tài liệu chứa Preview 3D.");
            if (ribId != null && !visibleBodies.ContainsKey(ribId))
                throw new InvalidOperationException("Không tìm thấy thanh trong Preview 3D: " + ribId);

            try
            {
                RemoveUraPreview();
                if (highlightedRibId != null)
                {
                    var previous = visibleBodies[highlightedRibId];
                    previous.Body.Hide(previewContext);
                    if (isolatedRibId == null || isolatedRibId == highlightedRibId)
                        DisplayPreviewBody(previous.Body, previous.Color);
                }
                if (ribId != null)
                {
                    var current = visibleBodies[ribId];
                    current.Body.Hide(previewContext);
                    DisplayPreviewBody(current.Body, HighlightColor);
                    if (side != KadaiUraSide.Unset)
                    {
                        var face = (current.Body.GetFaces() as object[] ?? new object[0]).OfType<Face2>().Single(candidate =>
                            candidate.Normal is double[] normal &&
                            normal.Zip(current.Across, (x, y) => x * y).Sum() * (int)side > .999999);
                        uraPreview = face.CreateSheetBody() as Body2;
                        if (uraPreview == null) throw new InvalidOperationException("Không dựng được Preview mặt ウラ.");
                        var shift = new[] { 1d, 0, 0, 0, 1d, 0, 0, 0, 1d }
                            .Concat(current.Across.Select(axis => axis * (int)side * .00005))
                            .Concat(new[] { 1d, 0, 0, 0 }).ToArray();
                        var transform = ((MathUtility)app.GetMathUtility()).CreateTransform(shift) as MathTransform;
                        if (!uraPreview.ApplyTransform(transform))
                            throw new InvalidOperationException("Không hiển thị được mặt ウラ đã chọn.");
                        DisplayPreviewBody(uraPreview, KadaiUraAppearance.ColorRef);
                    }
                }
                highlightedRibId = ribId;
                highlightedSide = side;
                previewDocument.GraphicsRedraw2();
            }
            catch { ClearTemporaryPreview(null); throw; }
        }

        internal void SetInspectionIsolation(string ribId)
        {
            CheckThread();
            if (isolatedRibId == ribId) return;
            if (previewDocument == null || (ribId != null && !visibleBodies.ContainsKey(ribId)))
                throw new InvalidOperationException("Không tìm được thanh trong Preview.");
            try
            {
                foreach (var entry in visibleBodies)
                {
                    var body = entry.Value;
                    body.Body.Hide(previewContext);
                    if (ribId == null || ribId == entry.Key)
                        DisplayPreviewBody(body.Body, entry.Key == highlightedRibId ? HighlightColor : body.Color);
                }
                isolatedRibId = ribId;
                previewDocument.GraphicsRedraw2();
            }
            catch { ClearTemporaryPreview(null); throw; }
        }

        internal InspectionView BeginInspection(KadaiPanelSource source)
        {
            CheckThread();
            if (!IsSourceCurrent(source) || !(source.DocumentTransient.ActiveView is ModelView view))
                throw new InvalidOperationException("Mở lại Assembly để xem mặt ウラ.");
            return new InspectionView(this, source, view);
        }

        internal sealed class InspectionView : IDisposable
        {
            private readonly KadaiPreviewBackend backend;
            private readonly KadaiPanelSource source;
            private readonly ModelView view;
            private readonly MathUtility math;
            private readonly double[] orientation, translation;
            private readonly double scale;
            private bool adjusted;

            internal InspectionView(KadaiPreviewBackend backend, KadaiPanelSource source, ModelView view)
            {
                this.backend = backend;
                this.source = source;
                this.view = view;
                math = (MathUtility)backend.app.GetMathUtility();
                orientation = (double[])view.Orientation3.ArrayData;
                translation = (double[])view.Translation3.ArrayData;
                scale = view.Scale2;
            }

            internal KadaiUraSide VisibleSide(KadaiProfile3D rib)
            {
                var worldNormal = IntoPanelFrame(rib.ExtrusionDirection, source.ComponentPlacement, null, false);
                var vector = (MathVector)math.CreateVector(worldNormal);
                var relative = (MathVector)vector.MultiplyTransform(view.Orientation3);
                var direction = (double[])relative.ArrayData;
                if (Math.Abs(direction[2]) < .25 * Math.Sqrt(direction.Sum(value => value * value)))
                    return KadaiUraSide.Unset;
                return direction[2] < 0 ? KadaiUraSide.First : KadaiUraSide.Second;
            }

            internal void LookAt(KadaiProfile3D rib, KadaiUraSide side)
            {
                if (!backend.IsSourceCurrent(source) || !backend.visibleBodies.TryGetValue(rib.RibId, out var displayed))
                    throw new InvalidOperationException("Quay lại Assembly đã Preview để xem thanh.");
                var normal = IntoPanelFrame(rib.ExtrusionDirection, source.ComponentPlacement, null, false)
                    .Select(value => value * (int)side).ToArray();
                var up = IntoPanelFrame(rib.HeightDirection, source.ComponentPlacement, null, false);
                var right = new[] { up[1] * normal[2] - up[2] * normal[1], up[2] * normal[0] - up[0] * normal[2],
                    up[0] * normal[1] - up[1] * normal[0] };
                var worldToView = new[] { right[0], up[0], normal[0], right[1], up[1], normal[1],
                    right[2], up[2], normal[2], 0d, 0, 0, 1, 0, 0, 0 };
                adjusted = true;
                view.Orientation3 = (MathTransform)math.CreateTransform(worldToView);
                var box = (double[])displayed.Body.GetBodyBox();
                var corners = (from x in new[] { box[0], box[3] }
                               from y in new[] { box[1], box[4] }
                               from z in new[] { box[2], box[5] }
                               select IntoPanelFrame(new[] { x, y, z }, source.ComponentPlacement, null, true)).ToArray();
                const double padding = .02;
                source.DocumentTransient.ViewZoomTo2(corners.Min(point => point[0]) - padding,
                    corners.Min(point => point[1]) - padding, corners.Min(point => point[2]) - padding,
                    corners.Max(point => point[0]) + padding, corners.Max(point => point[1]) + padding,
                    corners.Max(point => point[2]) + padding);
                source.DocumentTransient.GraphicsRedraw2();
            }

            public void Dispose()
            {
                try
                {
                    if (!adjusted || !backend.IsSourceCurrent(source)) return;
                    view.Orientation3 = (MathTransform)math.CreateTransform(orientation);
                    view.Translation3 = (MathVector)math.CreateVector(translation);
                    view.Scale2 = scale;
                    source.DocumentTransient.GraphicsRedraw2();
                }
                catch (COMException) { }
            }
        }

        private void DisplayPreviewBody(Body2 body, int color)
        {
            int result = body.Display3(previewContext, color, 0);
            if (result != 0) throw new InvalidOperationException("Display3 failed: " + result);
        }

        public void ClearTemporaryPreview(ModelDoc2 ignored)
        {
            CheckThread();
            RemoveUraPreview();
            foreach (var preview in visibleBodies.Values) {
                var body = preview.Body;
                try { if (previewContext != null) body.Hide(previewContext); } catch (COMException) { }
                finally { ReleaseOwned(body); }
            }
            visibleBodies.Clear();
            highlightedRibId = null;
            isolatedRibId = null;
            highlightedSide = KadaiUraSide.Unset;
            try { if (previewDocument != null) previewDocument.GraphicsRedraw2(); } catch (COMException) { }
            previewDocument = null;
            previewContext = null;
        }

        private void RemoveUraPreview()
        {
            if (uraPreview == null) return;
            try { if (previewContext != null) uraPreview.Hide(previewContext); }
            catch (COMException) { }
            finally { ReleaseOwned(uraPreview); uraPreview = null; }
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
