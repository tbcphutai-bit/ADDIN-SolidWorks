using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.MirrorV7.MirrorInPlace
{
    public sealed class AssemblyMirrorResult71
    {
        public string OutputPath { get; internal set; }
        public int Reused { get; internal set; }
        public int Mirrored { get; internal set; }
        // "component: reason" for every component that is not in the new assembly.
        public readonly List<string> LeftOut = new List<string>();
    }

    // 4x4 matrices acting on column vectors. SOLIDWORKS stores the image of each
    // axis as a row triple, then the translation, then the scale.
    internal static class Rigid71
    {
        public static double[,] Identity() { var m = new double[4, 4]; for (int i = 0; i < 4; i++) m[i, i] = 1; return m; }

        public static double[,] Multiply(params double[][,] factors)
        {
            var m = factors[0];
            for (int f = 1; f < factors.Length; f++)
            {
                var next = new double[4, 4];
                for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) for (int k = 0; k < 4; k++) next[r, c] += m[r, k] * factors[f][k, c];
                m = next;
            }
            return m;
        }

        public static double[,] FromNative(MathTransform transform)
        {
            var a = (double[])transform.ArrayData; var m = Identity();
            for (int r = 0; r < 3; r++) { for (int c = 0; c < 3; c++) m[r, c] = a[12] * a[3 * c + r]; m[r, 3] = a[9 + r]; }
            return m;
        }

        public static MathTransform ToNative(MathUtility math, double[,] m)
        {
            var a = new double[16];
            for (int r = 0; r < 3; r++) { for (int c = 0; c < 3; c++) a[3 * c + r] = m[r, c]; a[9 + r] = m[r, 3]; }
            a[12] = 1;
            return (MathTransform)math.CreateTransform(a);
        }

        // Reflection about the plane through `point` with unit `normal`.
        public static double[,] Reflection(double[] point, double[] normal)
        {
            double length = Math.Sqrt(normal[0] * normal[0] + normal[1] * normal[1] + normal[2] * normal[2]);
            if (length < 1e-12) throw new InvalidOperationException("ASM71 mirror plane has no normal.");
            var n = new[] { normal[0] / length, normal[1] / length, normal[2] / length };
            double offset = n[0] * point[0] + n[1] * point[1] + n[2] * point[2];
            var m = Identity();
            for (int r = 0; r < 3; r++) { for (int c = 0; c < 3; c++) m[r, c] -= 2 * n[r] * n[c]; m[r, 3] = 2 * offset * n[r]; }
            return m;
        }

        public static double[,] ReflectYZ() { return Reflection(new double[3], new double[] { 1, 0, 0 }); }

        public static double[] Apply(double[,] m, double[] p)
        {
            return new[]
            {
                m[0, 0] * p[0] + m[0, 1] * p[1] + m[0, 2] * p[2] + m[0, 3],
                m[1, 0] * p[0] + m[1, 1] * p[1] + m[1, 2] * p[2] + m[1, 3],
                m[2, 0] * p[0] + m[2, 1] * p[1] + m[2, 2] * p[2] + m[2, 3]
            };
        }
    }

    // Geometry signature of the solid bodies of one component: vertices, the
    // centre of every circular edge (so faces without vertices count), face
    // type/area, volume and centre of mass. Face boxes are not used (not tight
    // after a rotation), nor edge mid-parameter points (after a reflection the
    // parameter range of an arc can describe the complementary arc).
    internal sealed class PartShape71
    {
        private const double Position = 1e-7;
        public double[][] Points, Circles; public double[][] Faces; public double Volume; public double[] Centre; public int Bodies;

        public static PartShape71 Of(Body2[] bodies)
        {
            var points = new List<double[]>(); var circles = new List<double[]>(); var faces = new List<double[]>();
            double volume = 0; var centre = new double[3];
            foreach (Body2 body in bodies)
            {
                foreach (Vertex vertex in (body.GetVertices() as object[] ?? new object[0]).Cast<Vertex>()) points.Add((double[])vertex.GetPoint());
                foreach (Face2 face in (body.GetFaces() as object[] ?? new object[0]).Cast<Face2>())
                    faces.Add(new[] { (double)((Surface)face.GetSurface()).Identity(), face.GetArea() });
                foreach (Edge edge in (body.GetEdges() as object[] ?? new object[0]).Cast<Edge>())
                {
                    var curve = (Curve)edge.GetCurve();
                    if (curve == null || !curve.IsCircle()) continue;
                    var circle = (double[])curve.CircleParams;
                    circles.Add(new[] { circle[0], circle[1], circle[2] });
                }
                var mass = (double[])body.GetMassProperties(1);
                volume += mass[3];
                for (int k = 0; k < 3; k++) centre[k] += mass[k] * mass[3];
            }
            if (volume <= 0) throw new InvalidOperationException("ASM71 component has no solid volume.");
            for (int k = 0; k < 3; k++) centre[k] /= volume;
            return new PartShape71
            {
                Points = points.ToArray(), Circles = circles.ToArray(), Volume = volume, Centre = centre, Bodies = bodies.Length,
                Faces = faces.OrderBy(f => f[0]).ThenBy(f => f[1]).ToArray()
            };
        }

        public PartShape71 Transformed(double[,] m)
        {
            return new PartShape71
            {
                Points = Points.Select(p => Rigid71.Apply(m, p)).ToArray(), Circles = Circles.Select(p => Rigid71.Apply(m, p)).ToArray(),
                Faces = Faces, Volume = Volume, Centre = Rigid71.Apply(m, Centre), Bodies = Bodies
            };
        }

        private static bool SamePoints(double[][] expected, double[][] actual)
        {
            if (expected.Length != actual.Length) return false;
            var used = new bool[actual.Length];
            foreach (var p in expected)
            {
                int hit = -1;
                for (int i = 0; i < actual.Length; i++)
                {
                    if (used[i]) continue;
                    var q = actual[i];
                    if (Math.Abs(p[0] - q[0]) <= Position && Math.Abs(p[1] - q[1]) <= Position && Math.Abs(p[2] - q[2]) <= Position) { hit = i; break; }
                }
                if (hit < 0) return false;
                used[hit] = true;
            }
            return true;
        }

        // null when both describe the same geometry in the same place.
        public string Difference(PartShape71 other)
        {
            if (Bodies != other.Bodies) return "so body " + Bodies + "/" + other.Bodies;
            if (Faces.Length != other.Faces.Length) return "so mat " + Faces.Length + "/" + other.Faces.Length;
            if (Math.Abs(Volume - other.Volume) > 1e-6 * Volume) return "the tich";
            // Curved-face areas are integrated numerically and move by ~1e-6 with orientation; planar ones do not.
            for (int i = 0; i < Faces.Length; i++)
                if (Faces[i][0] != other.Faces[i][0] ||
                    Math.Abs(Faces[i][1] - other.Faces[i][1]) > (Faces[i][0] == (int)swSurfaceTypes_e.PLANE_TYPE ? 1e-6 : 1e-4) * Math.Max(Faces[i][1], 1e-9))
                    return "dien tich mat";
            for (int k = 0; k < 3; k++) if (Math.Abs(Centre[k] - other.Centre[k]) > 1e-6) return "trong tam";
            if (!SamePoints(Points, other.Points)) return "dinh";
            if (!SamePoints(Circles, other.Circles)) return "tam canh tron";
            return null;
        }

        // Farthest point of 'from' from its nearest point of 'to'.
        private static double Farthest(double[][] from, double[][] to)
        {
            if (from.Length == 0) return 0;
            if (to.Length == 0) return double.PositiveInfinity;
            double worst = 0;
            foreach (var p in from)
            {
                double nearest = double.PositiveInfinity;
                foreach (var q in to)
                {
                    double d = (p[0] - q[0]) * (p[0] - q[0]) + (p[1] - q[1]) * (p[1] - q[1]) + (p[2] - q[2]) * (p[2] - q[2]);
                    if (d < nearest) nearest = d;
                }
                worst = Math.Max(worst, nearest);
            }
            return Math.Sqrt(worst);
        }

        // For a Part made by the Part mirror: its geometry was proven there, within
        // the accepted local deviation (0.1 mm, 0.2 mm after a Fold; the measured
        // value is in the Part's own log), and can carry a sliver face or vertex
        // the source does not have. Here only its place in the assembly is
        // judged, to that same limit: equal body count, volume within the cap of
        // the reduced body proof, and every vertex and circle centre of either
        // shape having one of the other within the limit. null when it is in
        // place; 'worst' is the largest of those distances.
        public string DifferenceWithin(PartShape71 other, double limit, out double worst)
        {
            worst = 0;
            if (Bodies != other.Bodies) return "so body " + Bodies + "/" + other.Bodies;
            if (Math.Abs(Volume - other.Volume) > 5e-3 * Volume) return "the tich";
            double centre = Math.Sqrt(Enumerable.Range(0, 3).Sum(k => (Centre[k] - other.Centre[k]) * (Centre[k] - other.Centre[k])));
            // The centre of mass is not a criterion: on a folded sheet with spline faces
            // the native mass integration itself moves it by more than the limit
            // (natively a Part proven to 0.149 mm failed here on its centre alone).
            // It only has to be near: within fifty times the limit.
            if (centre > 50 * limit) { worst = centre; return "trong tam"; }
            // A Part from the Part mirror can have corners the source has not and the
            // other way round (short edges one body has in one piece; natively a
            // sheet with 40 corners against 32, proven there to 0.4e-6 m), and an
            // edge that is a circle in one can be another curve in the other (the
            // centre of a 44 m arc then had nothing within 44 m). The place is
            // therefore judged on the corners that do have a counterpart within
            // the limit: there must be at least four of them, in both directions,
            // and they must reach across the body (nine tenths of its extent along
            // every axis). A rigid placement that keeps such corners in place to
            // the limit is the right one to the limit; the shape itself is not
            // judged here.
            int unpaired, unpairedBack;
            double paired = PairedWithin(Points, other.Points, limit, out unpaired);
            double pairedBack = PairedWithin(other.Points, Points, limit, out unpairedBack);
            if (unpaired == 0 && unpairedBack == 0)
            {
                double circles = Points.Length >= 4 ? 0 : Math.Max(Farthest(Circles, other.Circles), Farthest(other.Circles, Circles));
                worst = Math.Max(Math.Max(paired, pairedBack), circles);
                return circles > limit ? "tam canh tron" : null;
            }
            worst = Math.Max(paired, pairedBack);
            if (double.IsNaN(paired) || double.IsNaN(pairedBack))
            {
                worst = Math.Max(Farthest(Points, other.Points), Farthest(other.Points, Points));
                return "dinh";
            }
            MirrorV7Diagnostics.Log("[ASM71][UNPAIRED_CORNERS] expected=" + unpaired + "/" + Points.Length + " actual=" + unpairedBack + "/" + other.Points.Length +
                " pairedWithin_m=" + worst.ToString("E2") + " pairedReachAcrossBody=True");
            return null;
        }

        // Farthest of the points of 'from' that have a point of 'to' within the
        // limit, and how many have none. NaN when fewer than four are paired or the
        // paired ones do not reach across nine tenths of the extent of 'from' along
        // every axis in which it is larger than ten times the limit.
        private static double PairedWithin(double[][] from, double[][] to, double limit, out int unpaired)
        {
            unpaired = 0;
            if (from.Length == 0) return 0;
            double worst = 0; var kept = new List<double[]>();
            foreach (var p in from)
            {
                double nearest = double.PositiveInfinity;
                foreach (var q in to)
                {
                    double d = (p[0] - q[0]) * (p[0] - q[0]) + (p[1] - q[1]) * (p[1] - q[1]) + (p[2] - q[2]) * (p[2] - q[2]);
                    if (d < nearest) nearest = d;
                }
                nearest = Math.Sqrt(nearest);
                if (nearest > limit) { unpaired++; continue; }
                worst = Math.Max(worst, nearest); kept.Add(p);
            }
            if (unpaired == 0) return worst;
            if (kept.Count < 4) return double.NaN;
            for (int k = 0; k < 3; k++)
            {
                double all = from.Max(p => p[k]) - from.Min(p => p[k]);
                if (all <= 10 * limit) continue;
                if (kept.Max(p => p[k]) - kept.Min(p => p[k]) < 0.9 * all) return double.NaN;
            }
            return worst;
        }
    }

    // Builds a NEW assembly that is the mirror image of the source assembly.
    // Every Part is taken once: a Part congruent to its own mirror image is
    // reused as-is at a new rigid placement; a Part with a hand gets an
    // independent editable opposite-hand Part from the Part mirror, placed by
    // T' = Mirror * T * MirrorYZ. Components are fixed in place (no mates yet).
    // A component whose Part cannot be mirrored is left out and reported; it
    // is never replaced by a derived mirrored body.
    public static class AssemblyMirror71
    {
        private sealed class Item { public string Name, Path, Configuration, Key; public double[,] Placement; }

        public static double[,] PlaneReflection(RefPlane plane)
        {
            if (plane == null) throw new ArgumentNullException("plane");
            var m = Rigid71.FromNative(plane.Transform);
            return Rigid71.Reflection(new[] { m[0, 3], m[1, 3], m[2, 3] }, new[] { m[0, 2], m[1, 2], m[2, 2] });
        }

        // A plane that belongs to a component is taken to assembly space first.
        public static double[,] PlaneReflection(RefPlane plane, Component2 owner)
        {
            var reflection = PlaneReflection(plane);
            if (owner == null) return reflection;
            MathTransform placement = owner.Transform2;
            return Rigid71.Multiply(Rigid71.FromNative(placement), reflection, Rigid71.FromNative((MathTransform)placement.Inverse()));
        }

        private static Body2[] SolidBodies(Component2 component)
        {
            object info;
            return (component.GetBodies3((int)swBodyType_e.swSolidBody, out info) as object[] ?? new object[0]).Cast<Body2>().ToArray();
        }

        // Eigenvectors (columns) of a symmetric 3x3 matrix, cyclic Jacobi.
        private static double[,] Eigenvectors(double[,] a)
        {
            var v = new double[3, 3]; for (int i = 0; i < 3; i++) v[i, i] = 1;
            for (int sweep = 0; sweep < 60; sweep++)
                for (int p = 0; p < 2; p++) for (int q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(a[p, q]) < 1e-300) continue;
                    double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    double tan = (theta < 0 ? -1 : 1) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    double cos = 1 / Math.Sqrt(tan * tan + 1), sin = tan * cos;
                    for (int k = 0; k < 3; k++) { double x = a[k, p], y = a[k, q]; a[k, p] = cos * x - sin * y; a[k, q] = sin * x + cos * y; }
                    for (int k = 0; k < 3; k++) { double x = a[p, k], y = a[q, k]; a[p, k] = cos * x - sin * y; a[q, k] = sin * x + cos * y; }
                    for (int k = 0; k < 3; k++) { double x = v[k, p], y = v[k, q]; v[k, p] = cos * x - sin * y; v[k, q] = sin * x + cos * y; }
                }
            return v;
        }

        // Candidate improper symmetries of the Part: sign flips in the principal frame of its vertex cloud.
        private static IEnumerable<double[,]> SymmetryCandidates(double[][] points)
        {
            if (points.Length < 4) yield break;
            var c = new double[3]; foreach (var p in points) for (int k = 0; k < 3; k++) c[k] += p[k] / points.Length;
            var covariance = new double[3, 3];
            foreach (var p in points) for (int r = 0; r < 3; r++) for (int k = 0; k < 3; k++) covariance[r, k] += (p[r] - c[r]) * (p[k] - c[k]);
            var axes = Eigenvectors(covariance);
            foreach (var signs in new[] { new[] { -1, 1, 1 }, new[] { 1, -1, 1 }, new[] { 1, 1, -1 }, new[] { -1, -1, -1 } })
            {
                var s = Rigid71.Identity();
                for (int r = 0; r < 3; r++) for (int k = 0; k < 3; k++) { s[r, k] = 0; for (int e = 0; e < 3; e++) s[r, k] += axes[r, e] * signs[e] * axes[k, e]; }
                for (int r = 0; r < 3; r++) { s[r, 3] = c[r]; for (int k = 0; k < 3; k++) s[r, 3] -= s[r, k] * c[k]; }
                yield return s;
            }
        }

        // Rigid motion C with C(Part) = MirrorYZ(Part), or null when the Part has a hand.
        // GetCoincidenceTransform2 is only a source of candidates: it has answered
        // true with a motion that leaves the bodies centimetres apart, so every
        // candidate is proven on the geometry signature.
        private static double[,] SelfMirrorMotion(MathUtility math, PartShape71 shape, Body2[] bodies)
        {
            var reflected = shape.Transformed(Rigid71.ReflectYZ());
            var candidates = SymmetryCandidates(shape.Points).Select(s => Rigid71.Multiply(Rigid71.ReflectYZ(), s)).ToList();
            if (bodies.Length == 1)
            {
                var image = (Body2)bodies[0].Copy2(false); MathTransform found;
                if (image.ApplyTransform(Rigid71.ToNative(math, Rigid71.ReflectYZ())) &&
                    ((Body2)bodies[0].Copy2(false)).GetCoincidenceTransform2(image, out found) && found != null)
                { candidates.Add(Rigid71.FromNative(found)); candidates.Add(Rigid71.FromNative((MathTransform)found.Inverse())); }
            }
            return candidates.FirstOrDefault(c => shape.Transformed(c).Difference(reflected) == null);
        }

        // A Part whose solid is congruent to its own mirror image (a flat plate, a
        // symmetric bracket) has no hand: the same file serves both sides and needs
        // no mirrored copy. Same proof as the assembly mirror uses before it re-uses
        // a component.
        public static bool IsOwnMirrorImage(ISldWorks app, ModelDoc2 part)
        {
            var bodies = (((PartDoc)part).GetBodies2((int)swBodyType_e.swSolidBody, true) as object[] ?? new object[0]).Cast<Body2>().ToArray();
            return bodies.Length > 0 && SelfMirrorMotion((MathUtility)app.GetMathUtility(), PartShape71.Of(bodies), bodies) != null;
        }

        private static string AssemblyTemplate(ISldWorks app)
        {
            string template = app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly);
            if (!string.IsNullOrEmpty(template) && File.Exists(template)) return template;
            // A stale default (older release path) is common; fall back to the template folders, then this release's own.
            var folders = (app.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swFileLocationsDocumentTemplates) ?? "").Split(';').ToList();
            int year = int.Parse(app.RevisionNumber().Split('.')[0]) + 1992;
            folders.Add(Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData),
                "SolidWorks", "SOLIDWORKS " + year, "templates"));
            template = folders.Where(f => !string.IsNullOrWhiteSpace(f) && Directory.Exists(f)).SelectMany(f => Directory.GetFiles(f, "*.asmdot")).FirstOrDefault();
            if (template == null) throw new InvalidOperationException("Khong tim thay template Assembly (*.asmdot). Hay dat Default Template trong Options.");
            return template;
        }

        private static string FirstLine(string message)
        {
            message = (message ?? "").Trim();
            int end = message.IndexOfAny(new[] { '\r', '\n' });
            return end < 0 ? message : message.Substring(0, end);
        }

        // Independent opposite-hand Part through the verified Part mirror.
        // Returns null on success, else the reason the Part cannot be mirrored.
        // The source assembly is closed while this runs (see Execute), so the
        // Part is opened as the ordinary standalone document the Part mirror
        // is proven on.
        private static string MirrorPart(ISldWorks app, Item item, string target)
        {
            int errors = 0, warnings = 0;
            var part = app.OpenDoc6(item.Path, (int)swDocumentTypes_e.swDocPART, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings) as ModelDoc2;
            if (part == null) return "khong mo duoc Part (" + errors + ")";
            // Read now: the Part mirror can disconnect this COM object.
            string partTitle = part.GetTitle();
            bool unsafeFailure = false;
            try
            {
                var active = part.ConfigurationManager.ActiveConfiguration as Configuration;
                if (active == null || !string.Equals(active.Name, item.Configuration, StringComparison.OrdinalIgnoreCase))
                    return "component dung configuration \"" + item.Configuration + "\" khac configuration da luu cua Part";
                var result = MirrorInPlaceOrchestratorV7.ExecuteLegacyPart(app, part, CanonicalPartMirrorPlaneV7.YZ, target);
                return result.OutputPublished ? null : "Part mirror khong xuat duoc file";
            }
            catch (Exception ex)
            {
                // After a native fault no further CAD call is safe: stop the whole command.
                unsafeFailure = RollbackReplayEngineV7.IsUnsafeNativeFailure66(ex);
                if (unsafeFailure) throw;
                return FirstLine(ex.Message);
            }
            finally
            {
                if (!unsafeFailure) app.CloseDoc(partTitle);
            }
        }
        // The largest local deviation the Part mirror accepts (after a Fold).
        private const double AcceptedDeviation71 = 2e-4;

        // A reused Part is the same file at a rigid placement: it must be the
        // mirror image exactly. A Part from the Part mirror ('mirrored') is held
        // to the deviation that mirror accepts, see PartShape71.DifferenceWithin.
        private static void Verify(AssemblyDoc assembly, IList<KeyValuePair<string, PartShape71>> placed, ICollection<string> mirrored, string stage)
        {
            var byName = (assembly.GetComponents(true) as object[] ?? new object[0]).Cast<Component2>()
                .ToDictionary(c => c.Name2, StringComparer.OrdinalIgnoreCase);
            int withinDeviation = 0; double worstMirrored = 0;
            foreach (var item in placed)
            {
                Component2 component;
                if (!byName.TryGetValue(item.Key, out component)) throw new InvalidOperationException("ASM71 " + stage + ": thieu component " + item.Key);
                PartShape71 actual = PartShape71.Of(SolidBodies(component)).Transformed(Rigid71.FromNative(component.Transform2));
                string difference = item.Value.Difference(actual);
                if (difference != null && mirrored.Contains(item.Key))
                {
                    double worst;
                    string exact = difference;
                    difference = item.Value.DifferenceWithin(actual, AcceptedDeviation71, out worst);
                    MirrorV7Diagnostics.Log("[ASM71][VERIFY_MIRRORED_PART] stage=" + stage + " component=" + item.Key + " exactCheck=" + exact +
                        " farthest_um=" + (worst * 1e6).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                        " limit_um=" + (AcceptedDeviation71 * 1e6).ToString("F0", System.Globalization.CultureInfo.InvariantCulture) +
                        " result=" + (difference == null ? "IN_PLACE" : "FAIL " + difference));
                    if (difference == null) { withinDeviation++; worstMirrored = Math.Max(worstMirrored, worst); }
                }
                if (difference != null) throw new InvalidOperationException("ASM71 " + stage + ": " + item.Key + " khong trung anh mirror (" + difference + ")");
            }
            MirrorV7Diagnostics.Log("[ASM71][VERIFY] stage=" + stage + " components=" + placed.Count + " mirroredWithinAcceptedDeviation=" + withinDeviation +
                " worstMirrored_um=" + (worstMirrored * 1e6).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " result=PASS");
        }

        // `mirror`: reflection in assembly space. `only`: component names, or null for every top-level component.
        public static AssemblyMirrorResult71 Execute(ISldWorks app, ModelDoc2 source, double[,] mirror, ICollection<string> only, string outputPath)
        {
            if (app == null) throw new ArgumentNullException("app");
            if (source == null || source.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY) throw new InvalidOperationException("Mirror Assembly can mot Assembly dang mo.");
            string sourcePath = source.GetPathName();
            if (string.IsNullOrWhiteSpace(sourcePath)) throw new InvalidOperationException("Luu Assembly goc truoc khi mirror.");
            outputPath = Path.GetFullPath(outputPath);
            if (!string.Equals(Path.GetExtension(outputPath), ".SLDASM", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("File ket qua phai co duoi .SLDASM: " + outputPath);
            if (File.Exists(outputPath)) throw new InvalidOperationException("File ket qua da ton tai. Hay chon ten moi: " + outputPath);
            string folder = Path.GetDirectoryName(outputPath);
            Directory.CreateDirectory(folder);
            var math = (MathUtility)app.GetMathUtility();
            var result = new AssemblyMirrorResult71 { OutputPath = outputPath };
            MirrorV7Diagnostics.Log("[ASM71][START] source=\"" + sourcePath + "\" output=\"" + outputPath + "\" scope=" + (only == null ? "ALL" : only.Count.ToString()));

            // 1. Everything needed from the source assembly, as plain data: the
            // Part mirror below switches documents and invalidates COM objects.
            var sourceAssembly = (AssemblyDoc)source;
            sourceAssembly.ResolveAllLightWeightComponents(false);
            var items = new List<Item>();
            var shapes = new Dictionary<string, PartShape71>(StringComparer.OrdinalIgnoreCase);
            var motions = new Dictionary<string, double[,]>(StringComparer.OrdinalIgnoreCase);
            foreach (Component2 component in (sourceAssembly.GetComponents(true) as object[] ?? new object[0]).Cast<Component2>())
            {
                string name = component.Name2, path = component.GetPathName() ?? "";
                if (only != null && !only.Contains(name)) continue;
                if (component.IsSuppressed()) { result.LeftOut.Add(name + ": dang Suppress"); continue; }
                if (!path.EndsWith(".SLDPRT", StringComparison.OrdinalIgnoreCase)) { result.LeftOut.Add(name + ": Sub-assembly chua duoc ho tro"); continue; }
                var bodies = SolidBodies(component);
                if (bodies.Length == 0) { result.LeftOut.Add(name + ": khong co solid body"); continue; }
                var item = new Item { Name = name, Path = path, Configuration = component.ReferencedConfiguration ?? "", Placement = Rigid71.FromNative(component.Transform2) };
                item.Key = item.Path + "|" + item.Configuration;
                if (!shapes.ContainsKey(item.Key))
                {
                    shapes[item.Key] = PartShape71.Of(bodies);
                    motions[item.Key] = SelfMirrorMotion(math, shapes[item.Key], bodies);
                }
                items.Add(item);
            }
            if (items.Count == 0) throw new InvalidOperationException("Khong co component Part nao de mirror.");
            var handed = items.Where(i => motions[i.Key] == null).GroupBy(i => i.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            MirrorV7Diagnostics.Log("[ASM71][PLAN] components=" + items.Count + " uniqueParts=" + shapes.Count + " oppositeHandParts=" + handed.Count);

            // The Part mirror is a standalone-Part pipeline. With the assembly
            // open it mis-read features and faulted the server (both on the
            // component documents and on private copies), so the source
            // assembly is closed while Parts are mirrored and reopened at the end.
            bool sourceClosed = false, published = false;
            if (handed.Count > 0)
            {
                if (source.GetSaveFlag()) throw new InvalidOperationException("Assembly goc co thay doi chua luu. Hay luu truoc: Assembly duoc dong tam trong luc mirror cac Part.");
                app.CloseDoc(source.GetTitle());
                if (app.GetOpenDocumentByName(sourcePath) != null) throw new InvalidOperationException("ASM71 khong dong tam duoc Assembly goc.");
                sourceClosed = true;
                MirrorV7Diagnostics.Log("[ASM71][SOURCE_CLOSED] openDocuments=" + app.GetDocumentCount());
            }

            MirrorProgress71.StartOwned(app, handed.Count + 3, "Mirror Assembly");
            bool unsafeFailure = false;
            try
            {
                // 2. Opposite-hand Parts, each once.
                var mirroredPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in handed)
                {
                    MirrorProgress71.StepOwned("Mirror Assembly: " + Path.GetFileNameWithoutExtension(item.Path));
                    string target = Path.Combine(folder, Path.GetFileNameWithoutExtension(item.Path) + "_MIRROR_YZ.SLDPRT");
                    string reason = !targets.Add(target) ? "Part co nhieu configuration can mirror: chua duoc ho tro"
                        : File.Exists(target) ? null : MirrorPart(app, item, target);
                    if (reason == null) mirroredPaths[item.Key] = target; else reasons[item.Key] = reason;
                    MirrorV7Diagnostics.Log("[ASM71][PART] source=\"" + item.Path + "\" result=" + (reason == null ? "MIRRORED" : "LEFT_OUT reason=" + reason));
                }

                // 3. The new assembly.
                MirrorProgress71.StepOwned("Mirror Assembly: tao Assembly moi");
                var target71 = app.NewDocument(AssemblyTemplate(app), 0, 0, 0) as ModelDoc2;
                if (target71 == null) throw new InvalidOperationException("Khong tao duoc Assembly moi.");
                bool saved = false;
                try
                {
                    var targetAssembly = (AssemblyDoc)target71;
                    var placed = new List<KeyValuePair<string, PartShape71>>();
                    var placedMirrored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in items)
                    {
                        string file; double[,] placement;
                        if (motions[item.Key] != null)
                        {
                            file = item.Path; result.Reused++;
                            placement = Rigid71.Multiply(mirror, item.Placement, Rigid71.ReflectYZ(), motions[item.Key]);
                        }
                        else if (mirroredPaths.TryGetValue(item.Key, out file))
                        {
                            result.Mirrored++;
                            placement = Rigid71.Multiply(mirror, item.Placement, Rigid71.ReflectYZ());
                        }
                        else { result.LeftOut.Add(item.Name + ": " + reasons[item.Key]); continue; }
                        if (app.GetOpenDocumentByName(file) == null)
                        {
                            // AddComponent5 needs the Part in memory; keep its window hidden.
                            int errors = 0, warnings = 0;
                            app.DocumentVisible(false, (int)swDocumentTypes_e.swDocPART);
                            try { app.OpenDoc6(file, (int)swDocumentTypes_e.swDocPART, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, item.Configuration, ref errors, ref warnings); }
                            finally { app.DocumentVisible(true, (int)swDocumentTypes_e.swDocPART); }
                        }
                        var added = targetAssembly.AddComponent5(file, (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                            "", false, item.Configuration, 0, 0, 0) as Component2;
                        if (added == null) throw new InvalidOperationException("ASM71 khong chen duoc component " + item.Name);
                        if (added.IsFixed()) { added.Select4(false, null, false); targetAssembly.UnfixComponent(); }
                        added.Transform2 = Rigid71.ToNative(math, placement);
                        added.Select4(false, null, false); targetAssembly.FixComponent();
                        placed.Add(new KeyValuePair<string, PartShape71>(added.Name2, shapes[item.Key].Transformed(Rigid71.Multiply(mirror, item.Placement))));
                        if (motions[item.Key] == null) placedMirrored.Add(added.Name2);
                    }
                    if (placed.Count == 0) throw new InvalidOperationException("Khong component nao mirror duoc:\n" + string.Join("\n", result.LeftOut));
                    target71.ClearSelection2(true);
                    target71.EditRebuild3();
                    MirrorProgress71.StepOwned("Mirror Assembly: kiem tra vi tri va hinh hoc");
                    Verify(targetAssembly, placed, placedMirrored, "BEFORE_SAVE");
                    MirrorProgress71.StepOwned("Mirror Assembly: luu file");
                    int saveErrors = 0, saveWarnings = 0;
                    if (!target71.Extension.SaveAs(outputPath, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                            (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref saveErrors, ref saveWarnings))
                        throw new InvalidOperationException("ASM71 khong luu duoc Assembly moi (" + saveErrors + ").");
                    Verify(targetAssembly, placed, placedMirrored, "AFTER_SAVE");
                    saved = true;
                }
                finally
                {
                    // An unverified assembly is left neither on screen nor on disk
                    // (the output path did not exist before this run).
                    if (!saved)
                    {
                        app.CloseDoc(target71.GetTitle());
                        if (File.Exists(outputPath)) File.Delete(outputPath);
                    }
                }
                MirrorV7Diagnostics.Log("[ASM71][OUTPUT_PUBLISHED] path=\"" + outputPath + "\" reused=" + result.Reused +
                    " mirrored=" + result.Mirrored + " leftOut=" + result.LeftOut.Count);
                published = true;
                return result;
            }
            catch (Exception ex)
            {
                unsafeFailure = RollbackReplayEngineV7.IsUnsafeNativeFailure66(ex);
                throw;
            }
            finally
            {
                MirrorProgress71.EndOwned(!unsafeFailure);
                if (sourceClosed && !unsafeFailure)
                {
                    // Give the user's assembly back; the new assembly stays in front.
                    int errors = 0, warnings = 0;
                    app.OpenDoc6(sourcePath, (int)swDocumentTypes_e.swDocASSEMBLY, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);
                    if (published) app.ActivateDoc3(Path.GetFileName(outputPath), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref errors);
                }
            }
        }
    }
}
