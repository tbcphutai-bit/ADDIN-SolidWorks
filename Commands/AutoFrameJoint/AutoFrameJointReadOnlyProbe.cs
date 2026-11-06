using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.AutoFrameJoint
{
    /// <summary>
    /// Phase 2A - READ ONLY. Reads exactly two selected Part occurrences from an
    /// Assembly and estimates each profile axis from straight B-rep edges.
    /// NEVER writes a feature, changes selection, activates a configuration,
    /// resolves lightweight components, rebuilds, or saves any document.
    /// All calculations are performed on occurrence-transformed geometry.
    /// </summary>
    public static class AutoFrameJointReadOnlyProbe
    {
        private const double MToMm = 1000.0;
        private const double MinEdgeM = 0.000001; // numerical filter, not manufacturing tolerance
        private const double ParallelDot = 0.9961946980917455; // cos(5 degrees)

        public static void Run(ISldWorks app)
        {
            string report = AnalyzeSelected(app);
            MessageBox.Show(report, "AUTO FRAME JOINT - ANALYZE ONLY",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // Retained as a convenience for callers without a native PMP.
        // The PMP should instead call AnalyzePair with explicit marked A/B.
        public static string AnalyzeSelected(ISldWorks app)
        {
            try
            {
                if (app == null)
                    return "UNAVAILABLE: ISldWorks reference is null.";

                ModelDoc2 assembly = app.ActiveDoc as ModelDoc2;
                if (assembly == null ||
                    assembly.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                    return "UNSUPPORTED: Open an Assembly document first.";

                SelectionMgr mgr = assembly.SelectionManager as SelectionMgr;
                if (mgr == null || mgr.GetSelectedObjectCount2(-1) != 2)
                    return "NEEDS_SELECTION: Select exactly two Component occurrences.";

                Component2 a = ResolveComponent(mgr, 1);
                Component2 b = ResolveComponent(mgr, 2);
                return AnalyzePair(app, a, b);
            }
            catch (Exception ex)
            {
                return "ERROR (no cut applied): " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        /// <summary>
        /// Explicitly selected component occurrences. A/B order comes from
        /// the PropertyManager selection marks, NOT global selection indices.
        /// Read only: no Part/Assembly mutation or Feature Cut.
        /// </summary>
        public static string AnalyzePair(ISldWorks app, Component2 a, Component2 b)
        {
            ModelDoc2 expected = app == null ? null : app.ActiveDoc as ModelDoc2;
            return AnalyzePair(app, expected, a, b);
        }

        /// <summary>
        /// The expected Assembly is captured BEFORE the PropertyManager opens.
        /// Reject a switched document rather than analyzing A/B against a new
        /// active Assembly. Fail closed when COM object identity is unsupported.
        /// </summary>
        public static string AnalyzePair(ISldWorks app, ModelDoc2 expectedAssembly,
                                         Component2 a, Component2 b)
        {
            try
            {
                if (app == null || expectedAssembly == null)
                    return "UNAVAILABLE: Assembly context is unavailable.";

                ModelDoc2 assembly = app.ActiveDoc as ModelDoc2;
                if (assembly == null ||
                    assembly.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                    return "UNSUPPORTED: Open the original Assembly document.";

                if (app.IsSame(assembly, expectedAssembly) !=
                    (int)swObjectEquality.swObjectSame)
                    return "UNSUPPORTED: Active Assembly changed. Reopen the command.";

                if (a == null || b == null)
                    return "NEEDS_SELECTION: Select Frame A and Frame B.";

                if (app.IsSame(a, b) != (int)swObjectEquality.swObjectNotSame)
                    return "NEEDS_SELECTION: Two distinct component occurrences could not be confirmed.";

                MathUtility math = app.GetMathUtility() as MathUtility;
                if (math == null)
                    return "UNAVAILABLE: SOLIDWORKS MathUtility could not be read.";

                FrameScan fa = Scan(a, math);
                FrameScan fb = Scan(b, math);

                var sb = new StringBuilder();
                sb.AppendLine("AUTO FRAME JOINT - READ ONLY");
                sb.AppendLine("Assembly: " + assembly.GetTitle());
                sb.AppendLine();
                WriteFrame(sb, "A", fa);
                WriteFrame(sb, "B", fb);
                sb.AppendLine();

                if (!fa.Supported || !fb.Supported)
                {
                    sb.AppendLine("RESULT: NEEDS_REVIEW");
                    sb.AppendLine("At least one Part could not be analyzed safely.");
                    return sb.ToString();
                }

                Vec3 da = fa.Axis, db = fb.Axis;
                double dot = Math.Abs(Vec3.Dot(da, db));
                dot = Math.Max(0.0, Math.Min(1.0, dot));
                double acuteAngleDeg = Math.Acos(dot) * (180.0 / Math.PI);
                sb.AppendLine("Angle between UNDIRECTED axes: " + F(acuteAngleDeg) + " deg");
                sb.AppendLine("This is NOT the interior corner angle.");

                if (fa.AxisConfidence < 0.60 || fb.AxisConfidence < 0.60)
                {
                    sb.AppendLine("ROLE: NEEDS_REVIEW (axis direction uncertain).");
                }
                else
                {
                    double diff = fa.LengthMm - fb.LengthMm;
                    // This is only a reporting threshold. NEVER use it as a manufacturing tolerance.
                    double tieThreshold = Math.Max(0.5, 0.0005 * Math.Max(fa.LengthMm, fb.LengthMm));
                    if (Math.Abs(diff) <= tieThreshold)
                        sb.AppendLine("ROLE: EQUAL_OR_AMBIGUOUS");
                    else
                        sb.AppendLine("ROLE: " + (diff > 0 ? "A = LONG / B = SHORT" : "B = LONG / A = SHORT"));
                }

                sb.AppendLine();
                // These are ESTIMATED geometric references based on transformed
                // B-rep vertices, NOT final toolpaths nor proof of solid overlap.
                var joint = AutoFrameJointGeometryMath.Solve(
                    fa.GeometryAxis, fb.GeometryAxis);
                if (joint.Valid)
                {
                    string angle = F(joint.AngleDegrees);
                    sb.AppendLine("JOINT_STATUS: " + joint.Status);
                    sb.AppendLine("JOINT_ANGLE_DEG: " + angle);
                    sb.AppendLine("JOINT_XYZ_MM: " + PointMm(joint.Midpoint));
                    sb.AppendLine("JOINT_NEAREST_A_MM: " + PointMm(joint.NearestA));
                    sb.AppendLine("JOINT_NEAREST_B_MM: " + PointMm(joint.NearestB));
                    sb.AppendLine("JOINT_FINITE_AXIS_GAP_MM: " + F(joint.FiniteAxisGapMm));
                    sb.AppendLine("JOINT_INFINITE_AXIS_GAP_MM: " + F(joint.InfiniteAxisGapMm));
                    sb.AppendLine("JOINT_NEAR_POINTS_INSIDE_SPANS: " +
                                  (joint.InfiniteNearestWithinFinite ? "YES" : "NO"));
                    double lx = Math.Max(fa.SampleEnvelopeMin.X, fb.SampleEnvelopeMin.X);
                    double ly = Math.Max(fa.SampleEnvelopeMin.Y, fb.SampleEnvelopeMin.Y);
                    double lz = Math.Max(fa.SampleEnvelopeMin.Z, fb.SampleEnvelopeMin.Z);
                    double hx = Math.Min(fa.SampleEnvelopeMax.X, fb.SampleEnvelopeMax.X);
                    double hy = Math.Min(fa.SampleEnvelopeMax.Y, fb.SampleEnvelopeMax.Y);
                    double hz = Math.Min(fa.SampleEnvelopeMax.Z, fb.SampleEnvelopeMax.Z);
                    bool envelopeOverlap = hx >= lx && hy >= ly && hz >= lz;
                    sb.AppendLine("JOINT_VERTEX_ENVELOPE_OVERLAP_CANDIDATE: " +
                                  (envelopeOverlap ? "YES" : "NO"));
                    if (envelopeOverlap)
                    {
                        sb.AppendLine("JOINT_VERTEX_ENVELOPE_SIZE_MM: (" +
                            F((hx - lx) * 1000.0) + ", " +
                            F((hy - ly) * 1000.0) + ", " +
                            F((hz - lz) * 1000.0) + ")");
                    }
                    if (!joint.NearParallel)
                    {
                        sb.AppendLine("JOINT_BISECTOR_NORMAL_CANDIDATE_1: " +
                                      VFormat(joint.Bisector1));
                        sb.AppendLine("JOINT_BISECTOR_NORMAL_CANDIDATE_2: " +
                                      VFormat(joint.Bisector2));
                    }
                    sb.AppendLine("JOINT_GEOMETRY_NOTE: Estimated centroid axes " +
                                  "and vertex envelopes, NOT exact solid intersection.");
                    sb.AppendLine("JOINT_GEOMETRY_REASON: " + joint.Reason);
                }
                else
                {
                    sb.AppendLine("JOINT_STATUS: UNAVAILABLE");
                    sb.AppendLine("JOINT_GEOMETRY_REASON: " + joint.Reason);
                }

                // KCC26003 rule: short edge/member wins over long when applicable,
                // subject to drawing override. Do not equate LONG/SHORT to a
                // complete Web/Wall/Lip cutting solution.
                sb.AppendLine("JOINT_KCC26003: SHORT_WINS_LONG_IF_APPLICABLE; " +
                    "DRAWING_OVERRIDE_FIRST; APPLICABILITY_NOT_VERIFIED.");
                sb.AppendLine("Winner/Loser: UNCONFIRMED for this Assembly.");
                sb.AppendLine("Profile Web/Wall/Lip: NOT YET ANALYZED.");
                sb.AppendLine("Cut geometry: NOT GENERATED. No SolidWorks document was changed.");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "ERROR (no cut applied): " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        private static Component2 ResolveComponent(SelectionMgr mgr, int index)
        {
            var selected = mgr.GetSelectedObjectsComponent4(index, -1) as Component2;
            if (selected != null) return selected;
            return mgr.GetSelectedObject6(index, -1) as Component2;
        }

        private static FrameScan Scan(Component2 comp, MathUtility math)
        {
            var scan = new FrameScan { Name = comp.Name2, Supported = false };
            try
            {
                scan.Path = comp.GetPathName();
                scan.Configuration = comp.ReferencedConfiguration;

                ModelDoc2 model = comp.GetModelDoc2() as ModelDoc2;
                if (model == null)
                {
                    scan.Reason = "Lightweight/suppressed/not loaded. Resolve manually; command will NOT change component state.";
                    return scan;
                }
                if (model.GetType() != (int)swDocumentTypes_e.swDocPART)
                {
                    scan.Reason = "Selected occurrence does not reference a Part.";
                    return scan;
                }
                // GetModelDoc2 can represent a different active configuration.
                ConfigurationManager manager = model.ConfigurationManager as ConfigurationManager;
                Configuration activeConfig = manager == null ? null : manager.ActiveConfiguration as Configuration;
                if (activeConfig == null ||
                    !String.Equals(activeConfig.Name, scan.Configuration, StringComparison.OrdinalIgnoreCase))
                {
                    scan.Reason = "Component configuration differs from active Part configuration; no silent switching.";
                    return scan;
                }

                PartDoc part = model as PartDoc;
                if (part == null)
                {
                    scan.Reason = "PartDoc interface unavailable.";
                    return scan;
                }

                object[] bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
                if (bodies == null || bodies.Length != 1)
                {
                    scan.Reason = "Expected exactly one visible solid body; found " +
                                  (bodies == null ? 0 : bodies.Length).ToString(CultureInfo.InvariantCulture) + ".";
                    return scan;
                }

                // GetTotalTransform(false) avoids the optional presentation transform.
                MathTransform transform = comp.GetTotalTransform(false);
                if (transform == null)
                {
                    scan.Reason = "Component-to-Assembly transform unavailable.";
                    return scan;
                }

                Body2 body = bodies[0] as Body2;
                object[] edges = body == null ? null : body.GetEdges() as object[];
                if (edges == null || edges.Length == 0)
                {
                    scan.Reason = "No solid B-rep edges found.";
                    return scan;
                }
                if (edges.Length > 100000)
                {
                    scan.Reason = "Geometry has too many edges for this initial probe.";
                    return scan;
                }

                var segments = new List<Segment>();
                var allPoints = new List<Vec3>();
                foreach (object o in edges)
                {
                    Edge edge = o as Edge;
                    if (edge == null) continue;
                    Vertex v1 = edge.GetStartVertex() as Vertex;
                    Vertex v2 = edge.GetEndVertex() as Vertex;
                    if (v1 == null || v2 == null) continue;

                    Vec3 p1, p2;
                    if (!TryTransformPoint(v1.GetPoint(), math, transform, out p1) ||
                        !TryTransformPoint(v2.GetPoint(), math, transform, out p2))
                        continue;

                    allPoints.Add(p1);
                    allPoints.Add(p2);

                    Curve curve = edge.GetCurve() as Curve;
                    if (curve == null || !curve.IsLine()) continue;

                    Vec3 delta = p2 - p1;
                    double length = delta.Length;
                    if (length < MinEdgeM || !Finite(length)) continue;
                    segments.Add(new Segment { Direction = delta / length, Length = length });
                }

                scan.SampledPoints = allPoints.Count;
                scan.LineEdges = segments.Count;
                if (segments.Count < 2 || allPoints.Count < 4)
                {
                    scan.Reason = "Insufficient distinct straight edges / vertices to estimate a profile axis.";
                    return scan;
                }

                // Weighted clustering of long parallel edges. No global XYZ assumption.
                double longest = 0.0;
                foreach (Segment s in segments) longest = Math.Max(longest, s.Length);
                double edgeThreshold = longest * 0.20;

                double totalCandidateWeight = 0.0;
                foreach (Segment s in segments)
                    if (s.Length >= edgeThreshold) totalCandidateWeight += s.Length;

                double bestScore = 0.0;
                Vec3 bestAxis = new Vec3(0, 0, 0);
                int supportCount = 0;
                foreach (Segment candidate in segments)
                {
                    if (candidate.Length < edgeThreshold) continue;
                    double score = 0.0;
                    int matches = 0;
                    foreach (Segment edge in segments)
                    {
                        if (edge.Length < edgeThreshold) continue;
                        if (Math.Abs(Vec3.Dot(candidate.Direction, edge.Direction)) >= ParallelDot)
                        {
                            score += edge.Length;
                            matches++;
                        }
                    }
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestAxis = candidate.Direction;
                        supportCount = matches;
                    }
                }

                if (bestScore <= 0.0 || supportCount < 2 || totalCandidateWeight <= 0.0)
                {
                    scan.Reason = "No sufficiently supported longitudinal direction.";
                    return scan;
                }

                // Project vertices onto estimated axis to measure the span along it.
                double min = double.PositiveInfinity, max = double.NegativeInfinity;
                double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
                double minZ = double.PositiveInfinity, maxX = double.NegativeInfinity;
                double maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
                double sumX = 0.0, sumY = 0.0, sumZ = 0.0;
                int validPointCount = 0;
                foreach (Vec3 p in allPoints)
                {
                    if (!p.IsFinite) continue;
                    double t = Vec3.Dot(p, bestAxis);
                    if (!Finite(t)) continue;
                    min = Math.Min(min, t);
                    max = Math.Max(max, t);
                    minX = Math.Min(minX, p.X);
                    minY = Math.Min(minY, p.Y);
                    minZ = Math.Min(minZ, p.Z);
                    maxX = Math.Max(maxX, p.X);
                    maxY = Math.Max(maxY, p.Y);
                    maxZ = Math.Max(maxZ, p.Z);
                    sumX += p.X;
                    sumY += p.Y;
                    sumZ += p.Z;
                    validPointCount++;
                }
                double lengthMm = (max - min) * MToMm;
                if (!Finite(lengthMm) || lengthMm <= 0.0 ||
                    validPointCount < 4)
                {
                    scan.Reason = "Projected span or centroid is invalid.";
                    return scan;
                }

                var origin = new AutoFrameJointGeometryMath.V(
                    sumX / validPointCount,
                    sumY / validPointCount,
                    sumZ / validPointCount);
                var direction = new AutoFrameJointGeometryMath.V(
                    bestAxis.X, bestAxis.Y, bestAxis.Z);
                if (!origin.Finite || !direction.Finite)
                {
                    scan.Reason = "Transformed axis has nonfinite sample data.";
                    return scan;
                }
                double originT = AutoFrameJointGeometryMath.V.Dot(origin, direction);
                scan.GeometryAxis = new AutoFrameJointGeometryMath.Axis
                {
                    Origin = origin,
                    Direction = direction,
                    MinT = min - originT,
                    MaxT = max - originT
                };
                scan.SampleEnvelopeMin =
                    new AutoFrameJointGeometryMath.V(minX, minY, minZ);
                scan.SampleEnvelopeMax =
                    new AutoFrameJointGeometryMath.V(maxX, maxY, maxZ);

                scan.Axis = bestAxis;
                scan.LengthMm = lengthMm;
                scan.AxisConfidence = bestScore / totalCandidateWeight;
                scan.Supported = true;
                scan.Reason = "ESTIMATE from long linear B-rep edges; NOT verified frame design axis.";
                return scan;
            }
            catch (Exception ex)
            {
                scan.Reason = "COM/geometry read error: " + ex.GetType().Name + ": " + ex.Message;
                return scan;
            }
        }

        private static bool TryTransformPoint(object raw, MathUtility math, MathTransform transform,
                                               out Vec3 point)
        {
            point = new Vec3(0, 0, 0);
            double[] coords = raw as double[];
            if (coords == null || coords.Length < 3) return false;
            if (!Finite(coords[0]) || !Finite(coords[1]) || !Finite(coords[2])) return false;

            MathPoint local = math.CreatePoint(new double[] { coords[0], coords[1], coords[2] }) as MathPoint;
            MathPoint world = local == null ? null : local.MultiplyTransform(transform) as MathPoint;
            double[] transformed = world == null ? null : world.ArrayData as double[];
            if (transformed == null || transformed.Length < 3) return false;
            point = new Vec3(transformed[0], transformed[1], transformed[2]);
            return point.IsFinite;
        }

        private static bool Finite(double x)
        {
            return !Double.IsNaN(x) && !Double.IsInfinity(x);
        }
        private static string F(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string VFormat(AutoFrameJointGeometryMath.V v)
        {
            return "(" + F(v.X) + ", " + F(v.Y) + ", " + F(v.Z) + ")";
        }

        private static string PointMm(AutoFrameJointGeometryMath.V point)
        {
            return "(" + F(point.X * MToMm) + ", " +
                         F(point.Y * MToMm) + ", " +
                         F(point.Z * MToMm) + ")";
        }
        private static void WriteFrame(StringBuilder sb, string label, FrameScan f)
        {
            sb.AppendLine("Frame " + label + ": " + f.Name);
            sb.AppendLine("  Path: " + f.Path);
            sb.AppendLine("  Config: " + f.Configuration);
            sb.AppendLine("  Status: " + (f.Supported ? "ANALYZED (provisional)" : "NEEDS_REVIEW"));
            sb.AppendLine("  Reason: " + f.Reason);
            sb.AppendLine("  Straight edges: " + f.LineEdges +
                          "  Sampled endpoints: " + f.SampledPoints);
            if (f.Supported)
            {
                sb.AppendLine("  Estimated length: " + F(f.LengthMm) + " mm");
                sb.AppendLine("  Estimated axis: " + f.Axis);
                sb.AppendLine("  Edge support fraction: " + F(f.AxisConfidence));
            }
        }

        private sealed class FrameScan
        {
            public string Name, Path, Configuration, Reason;
            public bool Supported;
            public int LineEdges, SampledPoints;
            public double LengthMm, AxisConfidence;
            public Vec3 Axis;
            public AutoFrameJointGeometryMath.Axis GeometryAxis;
            public AutoFrameJointGeometryMath.V SampleEnvelopeMin, SampleEnvelopeMax;
        }
        private struct Segment
        {
            public Vec3 Direction;
            public double Length;
        }
        private struct Vec3
        {
            public readonly double X, Y, Z;
            public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
            public bool IsFinite { get { return Finite(X) && Finite(Y) && Finite(Z); } }
            public double Length { get { return Math.Sqrt(X * X + Y * Y + Z * Z); } }
            public static Vec3 operator -(Vec3 a, Vec3 b) { return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
            public static Vec3 operator /(Vec3 a, double s) { return new Vec3(a.X / s, a.Y / s, a.Z / s); }
            public static double Dot(Vec3 a, Vec3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
            public override string ToString()
            {
                return "(" + F(X) + ", " + F(Y) + ", " + F(Z) + ")";
            }
        }
    }
}
