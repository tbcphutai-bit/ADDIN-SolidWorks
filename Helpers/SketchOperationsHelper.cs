using System;
using System.Collections.Generic;
using System.Linq;
using ADDIN.Commands;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Helpers
{
    public sealed class SketchSupportSnapshot20
    {
        public double Area;
        public double[][][] Boundaries;
        public string Kind;
        public int ReferenceType;
        public byte[] PersistentReference;
        public double[] PlaneOrigin;
        public double[] PlaneNormal;
    }
    // A contour or region of its sketch that an extrusion uses: start, middle and
    // end of every boundary edge, in model space.
    public sealed class SelectedContour71
    {
        public bool Region;
        public double[][][] Edges;
    }
    public sealed class CutAuditSnapshot21
    {
        public List<SelectedContour71> SelectedContours71;
        public SketchSupportSnapshot20 BeforeSupport;
        public List<SketchPrimitiveSnapshot58> BeforePrimitives53;
        public string CaptureError;
        public Body2 BeforeBody23;
        public double SourceResidual23 = double.NaN;
        public bool BeforeChainVerified23;
        public string CircleInvariant26;
        public List<SketchPointSnapshot> CirclePoints26;
        public List<RelationReference30> References30;
        public List<ConvertedEdge71> ConvertedEdges71 = new List<ConvertedEdge71>();
        public string ProfileKind50;
        public int SegmentCount50;
        public int ContourCount50;
        public int SelectedContourCount50;
        public bool BothDirections50;
        public bool ReverseDirection50;
        public bool FlipSideToCut50;
        public bool FromOffsetReverse50;
        public ExtrudeDirectionAudit50 D1_50;
        public ExtrudeDirectionAudit50 D2_50;
        public ExtrudeCutRecipe44 Recipe44;
    }
    public sealed class ExtrudeDirectionAudit50
    {
        public bool Forward;
        public int EndCondition;
        public string EndConditionName;
        public double Depth;
        public bool ReferencePresent;
        public int ReferenceSelectionType;
        public string ReferenceEntityKind;
        public byte[] PersistentReference;
        public double[] ReferencePoint;
        public double[][] ReferenceEdgeSamples;
        public SketchSupportSnapshot20 ReferenceFace;
        public string CaptureError;
    }
    // A sketch segment that is a copy of a model edge (On Edge / Offset Edge) and is
    // not a line or an arc: which relation, which entity of it is the edge, and the
    // edge as model-space samples of the source.
    public sealed class ConvertedEdge71
    {
        public int Relation, Entity, RelationCount, RelationType;
        public double[][] EdgeSamples;
    }
    public sealed class RelationReference30
    {
        public string Key;
        public SketchRelation SourceRelation31;
        public int EntityIndex;
        public int EntityCount;
        public Func<ModelDoc2, PlaneData, object> Resolve;
        public Func<ModelDoc2, object, object, bool> Verify31;
        public Func<PlaneData, double[]> PickPoint31;
        public Func<PlaneData, double[][]> Geometry31;
        public string Kind31;
    }
    public class SketchPointSnapshot
    {
        public int Id1;
        public int Id2;
        public int Index;
        public double X;
        public double Y;
        public double ModelX;
        public double ModelY;
        public double ModelZ;
        public bool HasModelCoords;
    }

    public class SketchSlotSnapshot
    {
        public int CreationType;
        public int LengthType;
        public double Length;
        public double Width;
        public double X1, Y1, Z1;
        public double X2, Y2, Z2;
        public double X3, Y3, Z3;
        public double ModelX1, ModelY1, ModelZ1;
        public double ModelX2, ModelY2, ModelZ2;
        public double ModelX3, ModelY3, ModelZ3;
        public bool HasModelCoords;
        public int CenterArcDirection;
    }

    public sealed class SketchPrimitiveSnapshot58
    {
        public int Type;
        public bool Construction;
        public bool Circle;
        public double Length;
        public double[] StartModel;
        public double[] EndModel;
        public double[] CenterModel;
        public double[] MiddleModel;
        // A spline: 129 points along it in model space (null when it could not be read).
        public double[][] SamplesModel;
        // A spline, when its curve hands it out: order, knots and control points
        // (model space) of the non-rational, non-periodic B-curve it is.
        public int SplineOrder;
        public double[] SplineKnots;
        public double[][] SplineControlModel;
        // Weights of the control points when the B-curve is rational (null otherwise), and whether
        // SOLIDWORKS hands its control points out multiplied by them.
        public double[] SplineWeights;
        public bool SplineHomogeneous;
    }

    public sealed class SketchPointMoveRejected58 : InvalidOperationException
    {
        public SketchPointMoveRejected58(string message) : base(message) { }
    }

    public static partial class SketchOperationsHelper
    {
        // Capture the original segment geometry, not the staging geometry: an upstream
        // feature rebuild can move only some of a downstream sketch's points.
        public static List<SketchPrimitiveSnapshot58> CapturePristineSketchPrimitives58(Sketch sketch)
        {
            var result = new List<SketchPrimitiveSnapshot58>();
            if (sketch == null) return result;
            MathTransform modelToSketch = sketch.ModelToSketchTransform as MathTransform;
            MathTransform sketchToModel = modelToSketch == null ? null : modelToSketch.Inverse() as MathTransform;
            ISldWorks app = SwAddin.InstanceSwApp;
            IMathUtility math = app == null ? null : app.GetMathUtility() as IMathUtility;
            if (sketchToModel == null || math == null) return result;
            foreach (object item in sketch.GetSketchSegments() as object[] ?? new object[0])
            {
                SketchSegment segment = item as SketchSegment;
                if (segment == null) continue;
                var entry = new SketchPrimitiveSnapshot58 { Type = segment.GetType() };
                try
                {
                    entry.Construction = segment.ConstructionGeometry;
                    entry.Length = segment.GetLength();
                    if (entry.Type == (int)swSketchSegments_e.swSketchLINE)
                    {
                        ISketchLine line = segment as ISketchLine;
                        if (line == null) throw new InvalidOperationException("Line interface unavailable.");
                        entry.StartModel = PrimitivePointToModel58(math, sketchToModel, line.GetStartPoint2() as SketchPoint);
                        entry.EndModel = PrimitivePointToModel58(math, sketchToModel, line.GetEndPoint2() as SketchPoint);
                    }
                    else if (entry.Type == (int)swSketchSegments_e.swSketchARC)
                    {
                        ISketchArc arc = segment as ISketchArc;
                        if (arc == null) throw new InvalidOperationException("Arc interface unavailable.");
                        entry.Circle = arc.IsCircle() != 0;
                        entry.StartModel = PrimitivePointToModel58(math, sketchToModel, arc.GetStartPoint2() as SketchPoint);
                        if (!entry.Circle)
                            entry.EndModel = PrimitivePointToModel58(math, sketchToModel, arc.GetEndPoint2() as SketchPoint);
                        entry.CenterModel = PrimitivePointToModel58(math, sketchToModel, arc.GetCenterPoint2() as SketchPoint);
                        if (!entry.Circle)
                        {
                            double radius = arc.GetRadius();
                            if (radius <= 0) throw new InvalidOperationException("Invalid arc radius.");
                            SketchPoint center = arc.GetCenterPoint2() as SketchPoint;
                            SketchPoint start = arc.GetStartPoint2() as SketchPoint;
                            double halfAngle = segment.GetLength() / radius * 0.5 *
                                (arc.GetRotationDir() >= 0 ? 1.0 : -1.0);
                            double vx = start.X - center.X, vy = start.Y - center.Y;
                            double[] middleLocal = {
                                center.X + vx * Math.Cos(halfAngle) - vy * Math.Sin(halfAngle),
                                center.Y + vx * Math.Sin(halfAngle) + vy * Math.Cos(halfAngle), 0.0
                            };
                            entry.MiddleModel = PrimitiveLocalToModel58(math, sketchToModel, middleLocal);
                        }
                    }
                    else if (entry.Type == (int)swSketchSegments_e.swSketchSPLINE)
                    {
                        entry.SamplesModel = SplineSamplesModel58(math, sketchToModel, segment, 129);
                        entry.StartModel = entry.SamplesModel[0];
                        entry.EndModel = entry.SamplesModel[entry.SamplesModel.Length - 1];
                        CaptureSplineDefinition58(math, sketchToModel, segment, entry);
                    }
                }
                catch (Exception ex)
                {
                    CreateMirrorPartPackage.LogDebug("[PRIMITIVE58][CAPTURE_INCOMPLETE] type=" + entry.Type +
                        " reason=" + ex.Message);
                }
                // Preserve unsupported segment types in the snapshot so the fallback
                // rejects them explicitly before deleting anything.
                result.Add(entry);
            }
            return result;
        }

        // Points along a sketch spline in model space, evenly in the parameter of
        // its curve. The curve of a sketch segment is handed out in the space of
        // its sketch (natively, on a 2D sketch, its first point was the first
        // spline point in sketch coordinates). The curve of a sketch segment need
        // not end where the segment ends (it does not for an arc), so the chords
        // of the samples must add up to the length of the segment and the samples
        // must start and end on the first and last point of the spline.
        internal static double[][] SplineSamplesModel58(IMathUtility math, MathTransform sketchToModel, SketchSegment segment, int count)
        {
            var curve = segment.GetCurve() as Curve;
            double a, b; bool closed, periodic;
            if (curve == null || !curve.GetEndParams(out a, out b, out closed, out periodic) || closed)
                throw new InvalidOperationException("SPLINE58: Spline curve unavailable or closed.");
            double[][] local = Enumerable.Range(0, count).Select(i =>
            {
                var p = curve.Evaluate2(a + (b - a) * i / (count - 1), 0) as double[];
                RequirePoint25(p);
                return new[] { p[0], p[1], p[2] };
            }).ToArray();
            Func<double[], double[], double> distance = (p, q) => Math.Sqrt((p[0] - q[0]) * (p[0] - q[0]) + (p[1] - q[1]) * (p[1] - q[1]) + (p[2] - q[2]) * (p[2] - q[2]));
            double chords = Enumerable.Range(1, count - 1).Sum(i => distance(local[i - 1], local[i])), length = segment.GetLength();
            if (Math.Abs(chords - length) > Math.Max(1e-6, length * 2e-3))
                throw new InvalidOperationException("SPLINE58: Spline samples do not span the segment. chords_m=" + chords.ToString("R") + " length_m=" + length.ToString("R"));
            var spline = segment as SketchSpline;
            var points = spline == null ? null : spline.GetPoints2() as object[];
            if (points == null || points.Length < 2) throw new InvalidOperationException("SPLINE58: Spline points unavailable.");
            double[] first = { ((SketchPoint)points[0]).X, ((SketchPoint)points[0]).Y, ((SketchPoint)points[0]).Z };
            double[] last = { ((SketchPoint)points[points.Length - 1]).X, ((SketchPoint)points[points.Length - 1]).Y, ((SketchPoint)points[points.Length - 1]).Z };
            double ends = Math.Min(Math.Max(distance(local[0], first), distance(local[count - 1], last)), Math.Max(distance(local[0], last), distance(local[count - 1], first)));
            if (ends > 1e-6) throw new InvalidOperationException("SPLINE58: Spline samples do not end on the spline points. residual_m=" + ends.ToString("R"));
            return local.Select(p => PrimitiveLocalToModel58(math, sketchToModel, p)).ToArray();
        }

        // The spline as the B-curve it is, so that it can be made again exactly: a
        // spline drawn through sample points is another curve between the points
        // (natively 0.5 mm shorter over 2981 mm with 65 points, and a Curve Driven
        // Pattern with equal spacing along it put its last instance 0.25 mm off).
        // The curve may be rational; asked for without weights, an 8-point spline
        // came back 0.365 mm from itself. How SOLIDWORKS stores weighted control
        // points is not assumed: the definition is evaluated here, both ways, at the
        // parameters the samples were taken at, and kept only when it gives the
        // samples back and spans exactly the segment.
        private static void CaptureSplineDefinition58(IMathUtility math, MathTransform sketchToModel, SketchSegment segment, SketchPrimitiveSnapshot58 entry)
        {
            SplineParamData data = null;
            try
            {
                var curve = segment.GetCurve() as Curve;
                double a, b; bool closed, periodic;
                if (curve == null || !curve.GetEndParams(out a, out b, out closed, out periodic)) return;
                data = curve.GetBCurveParams5(false, false, true, false) as SplineParamData;
                object controlObject = null, knotObject = null;
                if (data == null || (data.Dimension != 3 && data.Dimension != 4) || data.Periodic != 0 ||
                    !data.GetControlPoints(out controlObject) || !data.GetKnotPoints(out knotObject)) return;
                int dimension = data.Dimension, count = data.ControlPointsCount, order = data.Order;
                var control = controlObject as double[]; var knots = knotObject as double[];
                if (control == null || knots == null || count < 2 || control.Length != dimension * count || knots.Length != count + order) return;
                bool spansSegment = Math.Abs(knots[order - 1] - a) <= 1e-12 * Math.Max(1, Math.Abs(a)) && Math.Abs(knots[count] - b) <= 1e-12 * Math.Max(1, Math.Abs(b));
                double best = double.PositiveInfinity; double[][] bestModel = null; double[] bestWeights = null; bool bestHomogeneous = false;
                foreach (bool homogeneous in dimension == 4 ? new[] { false, true } : new[] { false })
                {
                    double[] weights = dimension == 4 ? Enumerable.Range(0, count).Select(i => control[4 * i + 3]).ToArray() : null;
                    if (weights != null && weights.Any(w => !(w > 0))) continue;
                    double[][] model = Enumerable.Range(0, count).Select(i =>
                    {
                        double scale = homogeneous ? weights[i] : 1.0;
                        return PrimitiveLocalToModel58(math, sketchToModel, new[] { control[dimension * i] / scale, control[dimension * i + 1] / scale, control[dimension * i + 2] / scale });
                    }).ToArray();
                    double off = DefinitionDeviation58(order, knots, model, weights, entry.SamplesModel);
                    if (off < best) { best = off; bestModel = model; bestWeights = weights; bestHomogeneous = homogeneous; }
                }
                // The parameter of the curve need not be the parameter of its B-curve
                // (natively the knots of three splines did not span the end
                // parameters of their curves, and the definition was the curve all
                // the same); what counts is that the definition gives the samples back.
                bool kept = best <= 1e-6;
                CreateMirrorPartPackage.LogDebug("[SPLINE58][DEFINITION] order=" + order + " controlPoints=" + count + " knots=" + knots.Length + " rational=" + (dimension == 4) +
                    " weightedControlPoints=" + bestHomogeneous + " spansSegment=" + spansSegment +
                    " givesSamplesBack_m=" + best.ToString("E2", System.Globalization.CultureInfo.InvariantCulture) + " kept=" + kept);
                if (!kept) return;
                entry.SplineOrder = order; entry.SplineKnots = knots; entry.SplineControlModel = bestModel;
                entry.SplineWeights = bestWeights; entry.SplineHomogeneous = bestHomogeneous;
            }
            catch (Exception ex)
            {
                CreateMirrorPartPackage.LogDebug("[SPLINE58][DEFINITION] unavailable reason=" + ex.Message);
            }
            finally
            {
                // Not released by hand: with an explicit release here SOLIDWORKS ended with a
                // corrupted heap half a minute into four of nine native runs.
                GC.KeepAlive(data);
            }
        }

        // How far a B-curve definition is from samples taken along the curve it is
        // said to be. Point by point when the samples are spread over its parameter
        // range the way they were taken (either direction); otherwise every sample
        // against the curve drawn finely (which a wrong reading of the weights fails
        // by tenths of a millimetre, natively 0.365 mm).
        private static double DefinitionDeviation58(int order, double[] knots, double[][] points, double[] weights, double[][] samples)
        {
            double lo = knots[order - 1], hi = knots[points.Length];
            Func<double[], double[], double> distance = (p, q) => Math.Sqrt((p[0] - q[0]) * (p[0] - q[0]) + (p[1] - q[1]) * (p[1] - q[1]) + (p[2] - q[2]) * (p[2] - q[2]));
            int last = samples.Length - 1;
            double forward = 0, backward = 0;
            for (int s = 0; s <= last; s++)
            {
                double[] p = EvaluateBSpline58(order, knots, points, weights, lo + (hi - lo) * s / last);
                forward = Math.Max(forward, distance(p, samples[s]));
                backward = Math.Max(backward, distance(p, samples[last - s]));
            }
            double pointwise = Math.Min(forward, backward);
            if (pointwise <= 1e-9) return pointwise;
            double[][] fine = Enumerable.Range(0, 8193).Select(k => EvaluateBSpline58(order, knots, points, weights, lo + (hi - lo) * k / 8192.0)).ToArray();
            return Math.Min(pointwise, PolylineDeviation58(samples, fine));
        }

        // One point of a B-curve (weights null: not rational) at parameter u.
        private static double[] EvaluateBSpline58(int order, double[] knots, double[][] points, double[] weights, double u)
        {
            int n = points.Length, p = order - 1, span = p;
            u = Math.Max(knots[p], Math.Min(knots[n], u));
            while (span < n - 1 && u >= knots[span + 1]) span++;
            var basis = new double[p + 1]; var left = new double[p + 1]; var right = new double[p + 1];
            basis[0] = 1;
            for (int j = 1; j <= p; j++)
            {
                left[j] = u - knots[span + 1 - j]; right[j] = knots[span + j] - u;
                double saved = 0;
                for (int r = 0; r < j; r++)
                {
                    double denominator = right[r + 1] + left[j - r];
                    double term = denominator == 0 ? 0 : basis[r] / denominator;
                    basis[r] = saved + right[r + 1] * term;
                    saved = left[j - r] * term;
                }
                basis[j] = saved;
            }
            double x = 0, y = 0, z = 0, w = 0;
            for (int j = 0; j <= p; j++)
            {
                int i = span - p + j; double weight = weights == null ? 1 : weights[i];
                x += basis[j] * weight * points[i][0]; y += basis[j] * weight * points[i][1]; z += basis[j] * weight * points[i][2]; w += basis[j] * weight;
            }
            return new[] { x / w, y / w, z / w };
        }

        private sealed class EdgeSpline71
        {
            internal int Order; internal double[] Knots, Weights; internal bool Homogeneous;
            internal double[][] Control;   // in the sketch
            internal double Deviation;
        }

        // The piece of a body edge a source spline ran along, as a B-curve in the
        // sketch. The mirrored body is the mirror image only within the accepted
        // local deviation; a profile that has to share an edge with it (natively a
        // Boss beside a sheet, whose side is a spline fitted to the sheet edge) must
        // lie on the edge of the body as it is, not on the exact mirror image of
        // the source edge, or the two do not meet and the Boss stays a body of its
        // own. Null when no edge of the solid bodies holds every reflected point of
        // the spline within that deviation.
        private static EdgeSpline71 SplineOnBodyEdge71(ModelDoc2 model, IMathUtility math, MathTransform modelToSketch,
            SketchPrimitiveSnapshot58 source, PlaneData plane, bool alreadyReflected)
        {
            double limit = NativeBodyCoincidence67.LocalDeviationLimit71;
            double[][] wanted = source.SamplesModel.Select(p => alreadyReflected ? p : ReflectReference30(p, plane)).ToArray();
            Func<double[], double[], double> distance = (p, q) => Math.Sqrt((p[0] - q[0]) * (p[0] - q[0]) + (p[1] - q[1]) * (p[1] - q[1]) + (p[2] - q[2]) * (p[2] - q[2]));
            Func<Edge, double[], double[]> nearest = (edge, p) =>
            {
                var q = edge.GetClosestPointOn(p[0], p[1], p[2]) as double[];
                return q == null || q.Length < 3 ? null : new[] { q[0], q[1], q[2] };
            };
            Func<Edge, double[], double> off = (edge, p) => { double[] q = nearest(edge, p); return q == null ? double.PositiveInfinity : distance(p, q); };
            Edge best = null; double bestOff = double.PositiveInfinity; int edges = 0;
            foreach (Body2 body in ((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, true) as object[] ?? new object[0])
                foreach (Edge edge in body.GetEdges() as object[] ?? new object[0])
                {
                    edges++;
                    if (off(edge, wanted[0]) > limit || off(edge, wanted[wanted.Length - 1]) > limit || off(edge, wanted[wanted.Length / 2]) > limit) continue;
                    double worst = wanted.Max(p => off(edge, p));
                    if (worst < bestOff) { bestOff = worst; best = edge; }
                }
            if (best == null || bestOff > limit)
            {
                CreateMirrorPartPackage.LogDebug("[SPLINE71][BODY_EDGE] edges=" + edges + " found=False nearest_m=" + bestOff.ToString("E2", System.Globalization.CultureInfo.InvariantCulture));
                return null;
            }
            SplineParamData data = null;
            try
            {
                double[] from = nearest(best, wanted[0]), to = nearest(best, wanted[wanted.Length - 1]);
                var whole = best.GetCurve() as Curve;
                Curve piece = whole == null ? null : whole.CreateTrimmedCurve2(from[0], from[1], from[2], to[0], to[1], to[2]) as Curve;
                bool otherWay = false;
                if (piece == null && whole != null)
                {
                    piece = whole.CreateTrimmedCurve2(to[0], to[1], to[2], from[0], from[1], from[2]) as Curve;
                    otherWay = piece != null;
                }
                double a = 0, b = 0; bool closed = false, periodic = false;
                if (piece == null || !piece.GetEndParams(out a, out b, out closed, out periodic))
                {
                    var range = best.GetCurveParams2() as double[];
                    CreateMirrorPartPackage.LogDebug("[SPLINE71][BODY_EDGE] edges=" + edges + " found=True onEdge_m=" + bestOff.ToString("E2", System.Globalization.CultureInfo.InvariantCulture) +
                        " curve=" + (whole != null) + " pieceBetweenEnds=" + (piece != null) + " curveType=" + (whole == null ? -1 : whole.Identity()) +
                        (range == null || range.Length < 6 ? "" : " edgeStartToSplineEnds_m=" + Math.Min(distance(new[] { range[0], range[1], range[2] }, from), distance(new[] { range[0], range[1], range[2] }, to)).ToString("E2", System.Globalization.CultureInfo.InvariantCulture) +
                            " edgeEndToSplineEnds_m=" + Math.Min(distance(new[] { range[3], range[4], range[5] }, from), distance(new[] { range[3], range[4], range[5] }, to)).ToString("E2", System.Globalization.CultureInfo.InvariantCulture)) +
                        " usable=False");
                    return null;
                }
                double[][] onPiece = Enumerable.Range(0, 129).Select(s =>
                {
                    var p = piece.Evaluate2(a + (b - a) * s / 128.0, 0) as double[];
                    RequirePoint25(p);
                    return new[] { p[0], p[1], p[2] };
                }).ToArray();
                double ends = Math.Min(Math.Max(distance(onPiece[0], from), distance(onPiece[128], to)), Math.Max(distance(onPiece[0], to), distance(onPiece[128], from)));
                data = piece.GetBCurveParams5(false, false, true, false) as SplineParamData;
                object controlObject = null, knotObject = null;
                if (ends > 1e-7 || data == null || (data.Dimension != 3 && data.Dimension != 4) || data.Periodic != 0 ||
                    !data.GetControlPoints(out controlObject) || !data.GetKnotPoints(out knotObject))
                {
                    CreateMirrorPartPackage.LogDebug("[SPLINE71][BODY_EDGE] found=True onEdge_m=" + bestOff.ToString("E2", System.Globalization.CultureInfo.InvariantCulture) +
                        " pieceEnds_m=" + ends.ToString("E2", System.Globalization.CultureInfo.InvariantCulture) + " definition=False");
                    return null;
                }
                int dimension = data.Dimension, count = data.ControlPointsCount, order = data.Order;
                var control = controlObject as double[]; var knots = knotObject as double[];
                if (control == null || knots == null || count < 2 || control.Length != dimension * count || knots.Length != count + order)
                {
                    CreateMirrorPartPackage.LogDebug("[SPLINE71][BODY_EDGE] found=True definitionShape=False dimension=" + dimension + " controlPoints=" + count + " order=" + order +
                        " controlValues=" + (control == null ? -1 : control.Length) + " knots=" + (knots == null ? -1 : knots.Length) + " otherWay=" + otherWay);
                    return null;
                }
                bool spans = Math.Abs(knots[order - 1] - a) <= 1e-12 * Math.Max(1, Math.Abs(a)) && Math.Abs(knots[count] - b) <= 1e-12 * Math.Max(1, Math.Abs(b));
                double bestBack = double.PositiveInfinity; double[][] bestModel = null; double[] bestWeights = null; bool bestHomogeneous = false;
                foreach (bool homogeneous in dimension == 4 ? new[] { false, true } : new[] { false })
                {
                    double[] weights = dimension == 4 ? Enumerable.Range(0, count).Select(i => control[4 * i + 3]).ToArray() : null;
                    if (weights != null && weights.Any(w => !(w > 0))) continue;
                    double[][] points = Enumerable.Range(0, count).Select(i =>
                    {
                        double scale = homogeneous ? weights[i] : 1.0;
                        return new[] { control[dimension * i] / scale, control[dimension * i + 1] / scale, control[dimension * i + 2] / scale };
                    }).ToArray();
                    double back = DefinitionDeviation58(order, knots, points, weights, onPiece);
                    if (back < bestBack) { bestBack = back; bestModel = points; bestWeights = weights; bestHomogeneous = homogeneous; }
                }
                // Into the sketch: the control points of the curve projected on its plane.
                double offPlane = 0;
                double[][] local = bestModel == null ? null : bestModel.Select(p =>
                {
                    var q = (double[])((MathPoint)((MathPoint)math.CreatePoint(p)).MultiplyTransform(modelToSketch)).ArrayData;
                    offPlane = Math.Max(offPlane, Math.Abs(q[2]));
                    return new[] { q[0], q[1], 0.0 };
                }).ToArray();
                bool usable = bestBack <= 1e-6 && local != null;
                CreateMirrorPartPackage.LogDebug("[SPLINE71][BODY_EDGE] edges=" + edges + " found=True onEdge_m=" + bestOff.ToString("E2", System.Globalization.CultureInfo.InvariantCulture) +
                    " order=" + order + " controlPoints=" + count + " rational=" + (dimension == 4) + " spansPiece=" + spans +
                    " givesPieceBack_m=" + bestBack.ToString("E2", System.Globalization.CultureInfo.InvariantCulture) +
                    " controlOffSketchPlane_m=" + offPlane.ToString("E2", System.Globalization.CultureInfo.InvariantCulture) + " usable=" + usable);
                return !usable ? null : new EdgeSpline71 { Order = order, Knots = knots, Weights = bestWeights, Homogeneous = bestHomogeneous, Control = local, Deviation = bestOff };
            }
            catch (Exception ex)
            {
                CreateMirrorPartPackage.LogDebug("[SPLINE71][BODY_EDGE] unavailable reason=" + ex.Message);
                return null;
            }
            finally
            {
                // Not released by hand: with an explicit release here SOLIDWORKS ended with a
                // corrupted heap half a minute into four of nine native runs.
                GC.KeepAlive(data);
            }
        }

        // Farthest of 'points' from the polyline through 'path' (both in one space).
        private static double PolylineDeviation58(double[][] points, double[][] path)
        {
            double worst = 0;
            foreach (double[] p in points)
            {
                double best = double.PositiveInfinity;
                for (int i = 1; i < path.Length; i++)
                {
                    double[] a = path[i - 1], b = path[i];
                    double[] ab = { b[0] - a[0], b[1] - a[1], b[2] - a[2] }, ap = { p[0] - a[0], p[1] - a[1], p[2] - a[2] };
                    double span = ab[0] * ab[0] + ab[1] * ab[1] + ab[2] * ab[2];
                    double t = span <= 0 ? 0 : Math.Max(0, Math.Min(1, (ap[0] * ab[0] + ap[1] * ab[1] + ap[2] * ab[2]) / span));
                    double dx = ap[0] - t * ab[0], dy = ap[1] - t * ab[1], dz = ap[2] - t * ab[2];
                    best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy + dz * dz));
                }
                worst = Math.Max(worst, best);
            }
            return worst;
        }

        // How far the splines of a sketch are from the mirror image of the source
        // splines; 0 for a sketch without splines. Moving the points of a spline
        // moves its ends, not its shape: natively a two-point spline that was the
        // path of a Curve Driven Pattern kept its reflected ends to 26e-6 m and
        // came out 2769 mm long instead of 2982 mm, and the pattern put its
        // instances elsewhere.
        public static double SplineShapeDeviation58(Sketch sketch, List<SketchPrimitiveSnapshot58> pristine, PlaneData plane)
        {
            if (sketch == null || pristine == null || plane == null ||
                !pristine.Any(p => p.Type == (int)swSketchSegments_e.swSketchSPLINE && p.SamplesModel != null)) return 0;
            var segments = (sketch.GetSketchSegments() as object[] ?? new object[0]).OfType<SketchSegment>().ToList();
            MathTransform modelToSketch = sketch.ModelToSketchTransform as MathTransform;
            MathTransform sketchToModel = modelToSketch == null ? null : modelToSketch.Inverse() as MathTransform;
            IMathUtility math = SwAddin.InstanceSwApp == null ? null : SwAddin.InstanceSwApp.GetMathUtility() as IMathUtility;
            if (segments.Count != pristine.Count || sketchToModel == null || math == null) return double.PositiveInfinity;
            double worst = 0;
            for (int i = 0; i < pristine.Count; i++)
            {
                if (pristine[i].Type != (int)swSketchSegments_e.swSketchSPLINE || pristine[i].SamplesModel == null) continue;
                if (segments[i].GetType() != (int)swSketchSegments_e.swSketchSPLINE) return double.PositiveInfinity;
                double[][] live;
                try { live = SplineSamplesModel58(math, sketchToModel, segments[i], 257); }
                catch (InvalidOperationException) { return double.PositiveInfinity; }
                worst = Math.Max(worst, PolylineDeviation58(pristine[i].SamplesModel.Select(p => ReflectReference30(p, plane)).ToArray(), live));
            }
            return worst;
        }

        private static double[] PrimitivePointToModel58(IMathUtility math, MathTransform transform, SketchPoint point)
        {
            if (point == null) throw new InvalidOperationException("PRIMITIVE58: Sketch point unavailable.");
            return PrimitiveLocalToModel58(math, transform, new[] { point.X, point.Y, point.Z });
        }

        private static double[] PrimitiveLocalToModel58(IMathUtility math, MathTransform transform, double[] local)
        {
            MathPoint p = math.CreatePoint(local) as MathPoint;
            MathPoint mapped = p == null ? null : p.MultiplyTransform(transform) as MathPoint;
            double[] data = mapped == null ? null : mapped.ArrayData as double[];
            if (data == null || data.Length < 3) throw new InvalidOperationException("PRIMITIVE58: Point transform failed.");
            return new[] { data[0], data[1], data[2] };
        }
        /// <summary>
        /// Lưu lại thông số các rãnh Slot nguyên bản trong Sketch trước khi rebuild
        /// </summary>
        public static List<SketchSlotSnapshot> CapturePristineSketchSlots(Sketch swSketch)
        {
            List<SketchSlotSnapshot> list = new List<SketchSlotSnapshot>();
            if (swSketch == null) return list;

            int slotCount = 0;
            try { slotCount = swSketch.GetSketchSlotCount(); } catch { }
            if (slotCount <= 0) return list;

            object[] slots = swSketch.GetSketchSlots() as object[];
            if (slots == null) return list;

            MathTransform s2m = null;
            try
            {
                MathTransform m2s = swSketch.ModelToSketchTransform;
                if (m2s != null) s2m = m2s.Inverse() as MathTransform;
            }
            catch { }

            ISldWorks swApp = SwAddin.InstanceSwApp;
            IMathUtility mathUtility = swApp != null ? swApp.GetMathUtility() as IMathUtility : null;

            for (int i = 0; i < slots.Length; i++)
            {
                ISketchSlot slot = slots[i] as ISketchSlot;
                if (slot == null) continue;

                object[] pts = slot.GetSlotPoints() as object[];
                SketchPoint p0 = (pts != null && pts.Length > 0) ? pts[0] as SketchPoint : null;
                SketchPoint p1 = (pts != null && pts.Length > 1) ? pts[1] as SketchPoint : null;
                SketchPoint p2 = (pts != null && pts.Length > 2) ? pts[2] as SketchPoint : null;

                double x1 = p0?.X ?? 0, y1 = p0?.Y ?? 0, z1 = p0?.Z ?? 0;
                double x2 = p1?.X ?? 0, y2 = p1?.Y ?? 0, z2 = p1?.Z ?? 0;
                double x3 = p2?.X ?? 0, y3 = p2?.Y ?? 0, z3 = p2?.Z ?? 0;

                double mx1 = 0, my1 = 0, mz1 = 0;
                double mx2 = 0, my2 = 0, mz2 = 0;
                double mx3 = 0, my3 = 0, mz3 = 0;
                bool hasModel = false;

                if (s2m != null && mathUtility != null)
                {
                    try
                    {
                        MathPoint mp1 = mathUtility.CreatePoint(new double[] { x1, y1, 0.0 }) as MathPoint;
                        MathPoint mpm1 = mp1 != null ? mp1.MultiplyTransform(s2m) as MathPoint : null;
                        double[] a1 = mpm1 != null ? mpm1.ArrayData as double[] : null;
                        if (a1 != null && a1.Length >= 3) { mx1 = a1[0]; my1 = a1[1]; mz1 = a1[2]; }

                        MathPoint mp2 = mathUtility.CreatePoint(new double[] { x2, y2, 0.0 }) as MathPoint;
                        MathPoint mpm2 = mp2 != null ? mp2.MultiplyTransform(s2m) as MathPoint : null;
                        double[] a2 = mpm2 != null ? mpm2.ArrayData as double[] : null;
                        if (a2 != null && a2.Length >= 3) { mx2 = a2[0]; my2 = a2[1]; mz2 = a2[2]; }

                        if (p2 != null)
                        {
                            MathPoint mp3 = mathUtility.CreatePoint(new double[] { x3, y3, 0.0 }) as MathPoint;
                            MathPoint mpm3 = mp3 != null ? mp3.MultiplyTransform(s2m) as MathPoint : null;
                            double[] a3 = mpm3 != null ? mpm3.ArrayData as double[] : null;
                            if (a3 != null && a3.Length >= 3) { mx3 = a3[0]; my3 = a3[1]; mz3 = a3[2]; }
                        }
                        hasModel = true;
                    }
                    catch { }
                }

                list.Add(new SketchSlotSnapshot
                {
                    CreationType = slot.CreationType,
                    LengthType = slot.LengthType,
                    Length = slot.Length,
                    Width = slot.Width,
                    X1 = x1, Y1 = y1, Z1 = z1,
                    X2 = x2, Y2 = y2, Z2 = z2,
                    X3 = x3, Y3 = y3, Z3 = z3,
                    ModelX1 = mx1, ModelY1 = my1, ModelZ1 = mz1,
                    ModelX2 = mx2, ModelY2 = my2, ModelZ2 = mz2,
                    ModelX3 = mx3, ModelY3 = my3, ModelZ3 = mz3,
                    HasModelCoords = hasModel,
                    CenterArcDirection = slot.CenterArcDirection
                });
            }

            return list;
        }

        /// <summary>
        /// Lưu lại bản đồ tọa độ sạch nguyên bản của mọi điểm trong Sketch trước khi có bất kỳ Feature nào Rebuild
        /// </summary>
        private static void Audit21(ModelDoc2 model, string message)
        {
            string line = "[CUTAUDIT21] " + message;
            CreateMirrorPartPackage.LogDebug(line);
            try
            {
                string path = model.GetPathName() + ".CutAudit21.txt";
                System.IO.File.AppendAllText(path, line + System.Environment.NewLine, System.Text.Encoding.UTF8);
            }
            catch (Exception ex) { CreateMirrorPartPackage.LogDebug("[CUTAUDIT21][REPORT_WRITE_FAILED] " + ex.Message); }
        }

        public static CutAuditSnapshot21 CaptureCutAudit21(ModelDoc2 model, Feature feature, Feature profile)
        {
            var snapshot = new CutAuditSnapshot21();
            var definition = feature.GetDefinition() as IExtrudeFeatureData2;
            bool access = false;
            try
            {
                if (definition == null) throw new InvalidOperationException("Not an extrusion definition");
                access = definition.AccessSelections(model, null);
                Audit21(model, "SOURCE feature=" + feature.Name + " access=" + access);
                if (!access) throw new InvalidOperationException("AccessSelections returned false");
                var sourceSketch50 = profile.GetSpecificFeature2() as Sketch;
                object[] segments50 = sourceSketch50 == null ? null : sourceSketch50.GetSketchSegments() as object[];
                object[] contours50 = sourceSketch50 == null ? null : sourceSketch50.GetSketchContours() as object[];
                object[] selectedContours50 = null;
                try { selectedContours50 = definition.Contours as object[]; } catch { }
                snapshot.ProfileKind50 = sourceSketch50 == null ? "NO_SKETCH" :
                    (IsOpenProfileSketch(sourceSketch50) ? "OPEN" : "CLOSED");
                snapshot.SegmentCount50 = segments50 == null ? 0 : segments50.Length;
                snapshot.ContourCount50 = contours50 == null ? 0 : contours50.Length;
                snapshot.SelectedContourCount50 = selectedContours50 == null ? 0 : selectedContours50.Length;
                // What the feature chose inside its sketch, by geometry (see ReselectContours71).
                snapshot.SelectedContours71 = (selectedContours50 ?? new object[0]).Select(item =>
                    new SelectedContour71 { Region = item is SketchRegion, Edges = ContourBoundary71(item) }).ToList();
                // Required replay options must not silently become false on an API read error.
                snapshot.BothDirections50 = definition.BothDirections;
                snapshot.ReverseDirection50 = definition.ReverseDirection;
                snapshot.FlipSideToCut50 = definition.FlipSideToCut;
                if (definition.FromType == (int)swExtrudeFrom_e.swExtrudeFrom_Offset)
                    snapshot.FromOffsetReverse50 = definition.FromOffsetReverse;
                snapshot.D1_50 = CaptureExtrudeDirection50(model, definition, true);
                snapshot.D2_50 = CaptureExtrudeDirection50(model, definition, false);
                snapshot.Recipe44 = CaptureCutRecipe44(model, definition, sourceSketch50, snapshot);
                foreach (bool first in new[] { true, false })
                {
                    try { Audit21(model, "OPTION direction=" + (first ? "D1" : "D2") +
                        " endCondition=" + definition.GetEndCondition(first) + " depth_m=" + definition.GetDepth(first).ToString("R", System.Globalization.CultureInfo.InvariantCulture)); }
                    catch (Exception ex) { Audit21(model, "OPTION_READ_FAILED direction=" + first + " reason=" + ex.Message); }
                }
                foreach (var property in typeof(IExtrudeFeatureData2).GetProperties())
                {
                    if (!property.CanRead || property.GetIndexParameters().Length != 0 ||
                        !(property.PropertyType.IsPrimitive || property.PropertyType.IsEnum || property.PropertyType == typeof(string))) continue;
                    try { Audit21(model, "OPTION " + property.Name + "=" + Convert.ToString(property.GetValue(definition, null), System.Globalization.CultureInfo.InvariantCulture)); }
                    catch (Exception ex) { Audit21(model, "OPTION_READ_FAILED property=" + property.Name + " reason=" + ex.Message); }
                }
                var sourceSketch26 = profile.GetSpecificFeature2() as Sketch;
                snapshot.BeforeSupport = CaptureSupport20(sourceSketch26, model);
                snapshot.BeforePrimitives53 = CapturePristineSketchPrimitives58(sourceSketch26);
                if (snapshot.BeforePrimitives53.Count != snapshot.SegmentCount50)
                    throw new InvalidOperationException("PROFILE53 source primitive capture is incomplete.");
                Audit21(model, "[PROFILE53][SOURCE_CAPTURE] feature=" + feature.Name +
                    " checkpoint=BEFORE_CUT primitives=" + snapshot.BeforePrimitives53.Count);
                snapshot.CircleInvariant26 = CircleInvariant26(sourceSketch26);
                snapshot.CirclePoints26 = CapturePristineSketchPoints(sourceSketch26);
                snapshot.ConvertedEdges71 = CaptureConvertedEdges71(sourceSketch26);
                try
                {
                    snapshot.References30 = CaptureReferences30(model, sourceSketch26);
                }
                catch (Exception referenceError56)
                {
                    // Geometry-first replay does not need the original constraint graph.
                    // A dangling/null relation entity must not discard the already captured
                    // profile, support, cut options and body oracle.
                    snapshot.References30 = new List<RelationReference30>();
                    Audit21(model, "[GEOMETRY_FIRST56][REFERENCE_CAPTURE_IGNORED] feature=" +
                        feature.Name + " reason=" + referenceError56.Message);
                }
                if (snapshot.BeforeSupport == null)
                    throw new InvalidOperationException("CUT23_SOURCE: sketch support unavailable");
                int referenceType23 = 0;
                var sourceFace23 = sourceSketch26.GetReferenceEntity(ref referenceType23) as Face2;
                if (sourceFace23 != null && snapshot.BeforeSupport.Boundaries != null)
                    snapshot.SourceResidual23 = BoundaryResidual23(sourceFace23, snapshot.BeforeSupport.Boundaries);
                string bodyError23;
                snapshot.BeforeBody23 = BodyOperationsHelper.GetSolidBodyCopyStrict(model, out bodyError23);
                if (snapshot.BeforeBody23 == null) throw new InvalidOperationException("CUT23_SOURCE_BODY: " + bodyError23);
                Audit21(model, "[CUT23] SOURCE_SELF_CHECK feature=" + feature.Name + " residual_m=" + snapshot.SourceResidual23.ToString("R") +
                    " result=" + (snapshot.BeforeSupport.Boundaries == null ? "NOT_APPLICABLE_NON_FACE" :
                        snapshot.SourceResidual23 <= 1e-7 ? "PASS" : "SOURCE_DATA_INVALID"));
                Audit21(model, "SOURCE_SUPPORT_BEFORE feature=" + feature.Name + " " + SupportLabel21(snapshot.BeforeSupport));
            }
            catch (Exception ex)
            {
                snapshot.CaptureError = ex.Message;
                Audit21(model, "SOURCE_CAPTURE_INCOMPLETE feature=" + feature.Name + " reason=" + ex.Message +
                    " at=" + ((ex.StackTrace ?? "").Split(new[] { '\n' }, 2)[0]).Trim());
            }
            finally { if (access) definition.ReleaseSelectionAccess(); }
            return snapshot;
        }

        // Null when the boundary cannot be read; reading it must never fail a capture.
        private static double[][][] ContourBoundary71(object item)
        {
            try
            {
                var contour = item as SketchContour; var region = item as SketchRegion;
                object[] edges = contour != null ? contour.GetEdges() as object[] : region != null ? region.GetEdges() as object[] : null;
                var result = new List<double[][]>();
                foreach (Edge edge in (edges ?? new object[0]).OfType<Edge>())
                {
                    var p = edge.GetCurveParams2() as double[];
                    var middle = p == null || p.Length < 8 ? null : edge.Evaluate2((p[6] + p[7]) / 2, 0) as double[];
                    if (middle == null || middle.Length < 3) return null;
                    result.Add(new[] { new[] { p[0], p[1], p[2] }, new[] { middle[0], middle[1], middle[2] }, new[] { p[3], p[4], p[5] } });
                }
                return result.Count == 0 ? null : result.ToArray();
            }
            catch (System.Runtime.InteropServices.COMException) { return null; }
            catch (InvalidCastException) { return null; }
        }

        // Farthest edge of 'wanted' from its nearest edge of 'have' (ends in either
        // order, and the middle), measured within the sketch plane: SOLIDWORKS
        // reports a region a fraction of a micrometre off its plane (natively 6.3e-7 m).
        private static double BoundaryApart71(double[][][] wanted, double[][][] have, double[] normal)
        {
            if (have == null || wanted.Length != have.Length) return double.PositiveInfinity;
            Func<double[], double[], double> inPlane = (a, b) =>
            {
                double[] d = { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
                double along = d[0] * normal[0] + d[1] * normal[1] + d[2] * normal[2];
                return Math.Sqrt(Math.Max(0, d[0] * d[0] + d[1] * d[1] + d[2] * d[2] - along * along));
            };
            double worst = 0;
            foreach (double[][] w in wanted)
                worst = Math.Max(worst, have.Min(h => Math.Max(inPlane(w[1], h[1]),
                    Math.Min(Math.Max(inPlane(w[0], h[0]), inPlane(w[2], h[2])), Math.Max(inPlane(w[0], h[2]), inPlane(w[2], h[0]))))));
            return worst;
        }

        // An extrusion made from chosen contours or regions of its sketch (natively
        // seven regions between two arcs) loses that choice when its profile has to
        // be drawn again: the sketch then consists of new entities and the feature
        // cannot rebuild. The choice is made again by geometry - each recorded
        // boundary, reflected, is the boundary of exactly one contour or region of
        // the re-drawn sketch - and handed back to the feature. A list SOLIDWORKS
        // does not take back as a whole (natively: with an open contour in it,
        // which cuts nothing) is handed back as its regions only; the body checks
        // that follow judge the result either way.
        public static void ReselectContours71(ModelDoc2 model, Feature feature, Feature profile, CutAuditSnapshot21 audit, PlaneData plane)
        {
            List<SelectedContour71> recorded = audit == null ? null : audit.SelectedContours71;
            if (recorded == null || recorded.Count == 0) return;
            string featureName = feature.Name, profileName = profile.Name;
            if (recorded.Any(r => r == null || r.Edges == null))
                throw new InvalidOperationException("CONTOUR71 a chosen contour of " + featureName + " could not be recorded in the source.");
            double norm = plane.Normal.Sum(v => v * v);
            Func<double[], double[]> reflect = p =>
            {
                double d = Enumerable.Range(0, 3).Sum(i => (p[i] - plane.Origin[i]) * plane.Normal[i]) / norm;
                return Enumerable.Range(0, 3).Select(i => p[i] - 2 * d * plane.Normal[i]).ToArray();
            };
            foreach (bool regionsOnly in new[] { false, true })
            {
                List<SelectedContour71> wanted = recorded.Where(r => !regionsOnly || r.Region).ToList();
                if (wanted.Count == 0 || (regionsOnly && wanted.Count == recorded.Count)) break;
                var live = ((PartDoc)model).FeatureByName(featureName) as Feature;
                var data = live == null ? null : live.GetDefinition() as IExtrudeFeatureData2;
                if (data == null || !data.AccessSelections(model, null))
                    throw new InvalidOperationException("CONTOUR71 cannot open the definition of " + featureName);
                bool accepted = false;
                try
                {
                    var liveProfile = ((PartDoc)model).FeatureByName(profileName) as Feature;
                    var sketch = liveProfile == null ? null : liveProfile.GetSpecificFeature2() as Sketch;
                    if (sketch == null) throw new InvalidOperationException("CONTOUR71 profile sketch missing: " + profileName);
                    double[] m = (double[])sketch.ModelToSketchTransform.ArrayData;
                    double[] normal = { m[2], m[5], m[8] };
                    var candidates = new List<Tuple<object, bool, double[][][]>>();
                    foreach (object c in sketch.GetSketchContours() as object[] ?? new object[0]) candidates.Add(Tuple.Create(c, false, ContourBoundary71(c)));
                    foreach (object r in sketch.GetSketchRegions() as object[] ?? new object[0]) candidates.Add(Tuple.Create(r, true, ContourBoundary71(r)));
                    var chosen = new List<object>();
                    foreach (SelectedContour71 item in wanted)
                    {
                        double[][][] goal = item.Edges.Select(e => e.Select(reflect).ToArray()).ToArray();
                        var same = candidates.Where(c => c.Item2 == item.Region && BoundaryApart71(goal, c.Item3, normal) < 1e-7).ToList();
                        if (same.Count != 1)
                            throw new InvalidOperationException("CONTOUR71 a chosen " + (item.Region ? "region" : "contour") + " of " + featureName +
                                " has " + same.Count + " counterparts in the re-drawn profile (contours and regions there: " + candidates.Count + ").");
                        chosen.Add(same[0].Item1);
                    }
                    data.Contours = chosen.Select(c => new System.Runtime.InteropServices.DispatchWrapper(c)).ToArray();
                    accepted = live.ModifyDefinition(data, model, null);
                }
                finally { if (!accepted) data.ReleaseSelectionAccess(); }
                CreateMirrorPartPackage.LogDebug("[CONTOUR71][RESELECT] feature=" + featureName + " recorded=" + recorded.Count +
                    " regions=" + recorded.Count(r => r.Region) + " handedBack=" + wanted.Count + " contoursLeftOut=" + (recorded.Count - wanted.Count) +
                    " accepted=" + accepted + " decidedBy=BODY_CHECK");
                if (accepted) return;
            }
            throw new InvalidOperationException("CONTOUR71 SOLIDWORKS did not take back the chosen contours of " + featureName + ".");
        }

        private static ExtrudeDirectionAudit50 CaptureExtrudeDirection50(
            ModelDoc2 model, IExtrudeFeatureData2 definition, bool forward)
        {
            var result = new ExtrudeDirectionAudit50 { Forward = forward, ReferenceEntityKind = "NONE" };
            try
            {
                result.EndCondition = definition.GetEndCondition(forward);
                result.EndConditionName = Enum.GetName(typeof(swEndConditions_e), result.EndCondition) ??
                    ("VALUE_" + result.EndCondition);
                try { result.Depth = definition.GetDepth(forward); } catch { }
                int selectionType;
                object reference = definition.GetEndConditionReference(forward, out selectionType);
                result.ReferenceSelectionType = selectionType;
                result.ReferencePresent = reference != null;
                result.ReferenceEntityKind = EndReferenceKind50(reference);
                if (reference != null)
                {
                    try { result.PersistentReference = model.Extension.GetPersistReference3(reference) as byte[]; }
                    catch { result.PersistentReference = null; }
                    var vertex = reference as Vertex;
                    var edge = reference as Edge;
                    var point = reference as SketchPoint;
                    var face = reference as Face2;
                    if (vertex != null) result.ReferencePoint = vertex.GetPoint() as double[];
                    else if (edge != null) result.ReferenceEdgeSamples = SampleEdge24(edge);
                    else if (point != null) result.ReferencePoint = SketchPointWorld50(point);
                    else if (face != null) result.ReferenceFace = CaptureFaceSupport24(face);
                }
            }
            catch (Exception ex)
            {
                result.CaptureError = ex.Message;
            }
            return result;
        }

        private static string EndReferenceKind50(object reference)
        {
            if (reference == null) return "NONE";
            if (reference is Vertex) return "Vertex";
            if (reference is Face2) return "Face";
            if (reference is Body2) return "Body";
            if (reference is Edge) return "Edge";
            if (reference is SketchPoint) return "SketchPoint";
            if (reference is Feature) return "Feature";
            return reference.GetType().FullName ?? reference.GetType().Name;
        }

        public static void RebindExtrudeEndReferences50(ModelDoc2 model, Feature feature,
            CutAuditSnapshot21 audit, PlaneData plane)
        {
            if (model == null || feature == null || audit == null) return;
            var directions = new[] { audit.D1_50, audit.D2_50 };
            if (!directions.Any(direction => direction != null && direction.ReferencePresent)) return;

            IExtrudeFeatureData2 definition = feature.GetDefinition() as IExtrudeFeatureData2;
            bool access = false;
            bool committed = false;
            try
            {
                if (definition == null) throw new InvalidOperationException("ENDREF50: Extrude definition unavailable.");
                access = definition.AccessSelections(model, null);
                if (!access) throw new InvalidOperationException("ENDREF50: AccessSelections returned false.");
                foreach (ExtrudeDirectionAudit50 direction in directions)
                {
                    if (direction == null || !direction.ReferencePresent) continue;
                    object target = ResolveEndReference50(model, direction, plane);
                    if (target == null) throw new InvalidOperationException("ENDREF50: Mapped target is null for " + direction.EndConditionName);
                    definition.SetEndConditionReference(direction.Forward, target);
                    int readbackSelectionType;
                    object readback = definition.GetEndConditionReference(direction.Forward, out readbackSelectionType);
                    if (readback == null)
                        throw new InvalidOperationException("ENDREF50: End reference readback is null for " + direction.EndConditionName);
                    CreateMirrorPartPackage.LogDebug("[ENDREF50][BOUND] direction=" + (direction.Forward ? "D1" : "D2") +
                        " end=" + direction.EndConditionName + " sourceKind=" + direction.ReferenceEntityKind +
                        " sourceSelectionType=" + direction.ReferenceSelectionType +
                        " readbackSelectionType=" + readbackSelectionType + " targetKind=" + EndReferenceKind50(readback));
                }
                if (!feature.ModifyDefinition(definition, model, null))
                    throw new InvalidOperationException("ENDREF50: ModifyDefinition returned false.");
                committed = true;
            }
            finally
            {
                if (access && !committed)
                {
                    try { definition.ReleaseSelectionAccess(); } catch { }
                }
            }
        }

        private static object ResolveEndReference50(ModelDoc2 model, ExtrudeDirectionAudit50 direction, PlaneData plane)
        {
            if (direction.ReferenceEdgeSamples != null)
                return ADDIN.Commands.MirrorV7.MirrorInPlace.RollbackReplayEngineV7.FindEdge(model,
                    direction.ReferenceEdgeSamples.Select(point => ReflectReference30(point, plane)).ToArray());
            if (direction.ReferenceFace != null && direction.ReferenceFace.Boundaries != null)
                return ADDIN.Commands.MirrorV7.MirrorInPlace.RollbackReplayEngineV7.FindFace(model,
                    direction.ReferenceFace.Area,
                    direction.ReferenceFace.Boundaries.Select(boundary => boundary
                        .Select(point => ReflectReference30(point, plane)).ToArray()).ToArray());
            if (direction.ReferencePoint != null)
            {
                if (string.Equals(direction.ReferenceEntityKind, "SketchPoint", StringComparison.Ordinal))
                    return FindReflectedSketchPoint50(model, direction.ReferencePoint, plane);
                return ADDIN.Commands.MirrorV7.MirrorInPlace.RollbackReplayEngineV7.FindVertex(model,
                    ReflectReference30(direction.ReferencePoint, plane));
            }
            throw new InvalidOperationException("ENDREF50: No geometry mapper for " + direction.ReferenceEntityKind +
                " (end=" + direction.EndConditionName + ").");
        }

        private static string CircleInvariant26(Sketch sketch)
        {
            // Conservative fast path: full circles only. An arc needs orientation/end-point checks.
            var segments = sketch.GetSketchSegments() as object[] ?? new object[0];
            if (segments.Length == 0) return null;
            var signature = new List<string>();
            foreach (SketchSegment segment in segments)
            {
                var arc = segment as SketchArc;
                if (arc == null || arc.IsCircle() != 1 || segment.ConstructionGeometry) return null;
                var ids = segment.GetID() as int[];
                if (ids == null || ids.Length < 2) return null;
                signature.Add("C:" + ids[0] + "," + ids[1] + ":" + arc.GetRadius().ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            foreach (SketchRelation relation in sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll) as object[] ?? new object[0])
            {
                var display = relation.GetDisplayDimension() as DisplayDimension;
                var dimension = display?.GetDimension() as Dimension;
                signature.Add("R:" + relation.GetRelationType());
                if (dimension != null) signature.Add("D:" + dimension.FullName + ":" + dimension.DrivenState + ":" +
                    dimension.SystemValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            signature.Sort(StringComparer.Ordinal);
            return string.Join("|", signature);
        }

        public static bool TryAlreadyReflectedCircles26(ISldWorks app, ModelDoc2 model, Feature profile,
            CutAuditSnapshot21 source, PlaneData plane)
        {
            if (source?.CircleInvariant26 == null || source.CirclePoints26 == null) return false;
            var sketch = profile.GetSpecificFeature2() as Sketch;
            if (sketch == null || CircleInvariant26(sketch) != source.CircleInvariant26) return false;
            var live = CapturePristineSketchPoints(sketch);
            if (live.Count != source.CirclePoints26.Count || live.Count == 0) return false;
            double norm2 = plane.Normal.Sum(v => v * v);
            if (norm2 < 1e-20) return false;
            foreach (var point in source.CirclePoints26)
            {
                var matches = live.Where(p => p.Id1 == point.Id1 && p.Id2 == point.Id2).ToArray();
                if (matches.Length != 1 || !point.HasModelCoords || !matches[0].HasModelCoords) return false;
                var original = new[] { point.ModelX, point.ModelY, point.ModelZ };
                var actual = new[] { matches[0].ModelX, matches[0].ModelY, matches[0].ModelZ };
                double d = Enumerable.Range(0, 3).Sum(i => (original[i] - plane.Origin[i]) * plane.Normal[i]) / norm2;
                double error = Math.Sqrt(Enumerable.Range(0, 3).Sum(i => Math.Pow(actual[i] - (original[i] - 2 * d * plane.Normal[i]), 2)));
                if (double.IsNaN(error) || error > 1e-7) return false;
            }
            bool warning;
            int sketchError = profile.GetErrorCode2(out warning);
            if (sketchError != 0) return false;
            CreateMirrorPartPackage.LogDebug("[SKETCH26][ALREADY_REFLECTED] sketch=" + profile.Name +
                " fullCircles=True centersVerified=True radiiDimensionsUnchanged=True constraintTypesUnchanged=True dimensionsReleased=0 cutValidationStillRequired=True");
            return true;
        }

        private static double BoundaryResidual23(Face2 face, double[][][] boundaries)
        {
            if (boundaries == null || boundaries.Length == 0 || boundaries.Any(e => e == null || e.Length == 0))
                throw new InvalidOperationException("Empty source boundary samples");
            double maximum = 0;
            foreach (var p in boundaries.SelectMany(e => e))
            {
                if (p.Length != 3 || p.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
                    throw new InvalidOperationException("Non-finite boundary sample");
                var q = face.GetClosestPointOn(p[0], p[1], p[2]) as double[];
                if (q == null || q.Length < 3 || q.Take(3).Any(v => double.IsNaN(v) || double.IsInfinity(v)))
                    throw new InvalidOperationException("Closest point unavailable");
                maximum = Math.Max(maximum, Math.Sqrt(Enumerable.Range(0, 3).Sum(i => (p[i] - q[i]) * (p[i] - q[i]))));
            }
            return maximum;
        }

        private static int SupportCandidates23(Body2 body, SketchSupportSnapshot20 support, double[][][] boundaries, out double best)
        {
            best = double.PositiveInfinity;
            int count = 0, otherEdgeCount = 0, areaNoise = 0;
            double areaNoiseRelative = 0;
            foreach (Face2 face in body.GetFaces() as object[] ?? new object[0])
            {
                double residual = BoundaryResidual23(face, boundaries);
                best = Math.Min(best, residual);
                // The same face with another area: natively a 0.45 m2 planar face
                // bounded by 2.9 m splines, whose boundary lay on the recorded one to
                // 1.7e-14 m in the reflected copy of the source body itself, measured
                // 447574.658 mm2 there against 447644.528 mm2 recorded (1.6e-4). The
                // area of such a face is a numerical integration; with the boundary
                // exact and the edge count equal, an area within 1e-3 is that face
                // (counted only when no face passes the area test as well).
                double areaOff = Math.Abs(face.GetArea() - support.Area) / Math.Max(1e-12, support.Area);
                if (residual <= (NativeBodyCoincidence67.SurfaceChainResidualAllowed71 ? 5e-6 : 1e-7) && areaOff > 1e-6 && areaOff <= 1e-3 &&
                    (face.GetEdges() as object[] ?? new object[0]).Length == boundaries.Length)
                {
                    areaNoise++; areaNoiseRelative = Math.Max(areaNoiseRelative, areaOff);
                }
                // Accepted micrometre class of a re-attached in-context surface chain: see ConfirmSurfaceChainResidual71.
                if (residual <= (NativeBodyCoincidence67.SurfaceChainResidualAllowed71 ? 5e-6 : 1e-7) &&
                    Math.Abs(face.GetArea() - support.Area) <= Math.Max(1e-12, support.Area * (NativeBodyCoincidence67.SurfaceChainResidualAllowed71 ? 1e-4 : 1e-6)))
                {
                    if ((face.GetEdges() as object[] ?? new object[0]).Length == boundaries.Length) count++;
                    else otherEdgeCount++;
                }
            }
            // In that class the same face can have one of its edges in two pieces
            // (natively after an Unfold: 76 corners against 74, the support of the
            // next cut matched to 1e-13 m and by area, with one edge more). Such a
            // face counts only when no face matches with the edge count as well.
            if (count == 0 && otherEdgeCount > 0 && NativeBodyCoincidence67.SurfaceChainResidualAllowed71)
            {
                CreateMirrorPartPackage.LogDebug("[CUT23][SUPPORT_EDGE_COUNT_DIFFERS] candidates=" + otherEdgeCount + " expectedEdges=" + boundaries.Length +
                    " bestResidual_m=" + best.ToString("E2"));
                return otherEdgeCount;
            }
            if (count == 0 && areaNoise == 0)
            {
                // Nothing matched: say what the nearest face is, so that the reason can be read.
                Face2 nearestFace = null; double nearestResidual = double.PositiveInfinity;
                foreach (Face2 face in body.GetFaces() as object[] ?? new object[0])
                {
                    double residual = BoundaryResidual23(face, boundaries);
                    if (residual < nearestResidual) { nearestResidual = residual; nearestFace = face; }
                }
                if (nearestFace != null)
                    CreateMirrorPartPackage.LogDebug("[CUT23][SUPPORT_NOT_MATCHED] nearestResidual_m=" + nearestResidual.ToString("E2") +
                        " faceArea_mm2=" + (nearestFace.GetArea() * 1e6).ToString("F3") + " recordedArea_mm2=" + (support.Area * 1e6).ToString("F3") +
                        " faceEdges=" + (nearestFace.GetEdges() as object[] ?? new object[0]).Length + " recordedEdges=" + boundaries.Length +
                        " acceptedClass=" + NativeBodyCoincidence67.SurfaceChainResidualAllowed71);
            }
            if (count == 0 && areaNoise > 0)
            {
                CreateMirrorPartPackage.LogDebug("[CUT23][SUPPORT_AREA_WITHIN_1E-3] candidates=" + areaNoise + " areaRelative=" + areaNoiseRelative.ToString("E2") +
                    " bestResidual_m=" + best.ToString("E2"));
                return areaNoise;
            }
            return count;
        }

        // Read-only diagnostic on the staging document. Never treats a nearest face as a mapping.
        public static void CheckCutChain23(ISldWorks app, ModelDoc2 model, Feature feature,
            CutAuditSnapshot21 source, PlaneData plane)
        {
            string stage = "SOURCE_DATA";
            IExtrudeFeatureData2 definition = null;
            bool access = false;
            if (source != null) source.BeforeChainVerified23 = false;
            try
            {
                if (source == null || source.CaptureError != null || source.BeforeBody23 == null ||
                    source.BeforeSupport == null ||
                    (source.BeforeSupport.Boundaries != null &&
                        (double.IsNaN(source.SourceResidual23) || source.SourceResidual23 > 1e-7)))
                    throw new InvalidOperationException(source?.CaptureError ?? "Source self-check failed");
                stage = "REFLECTION_ORACLE";
                var oracle = BodyOperationsHelper.MirrorBodyStrict(app, source.BeforeBody23, plane);
                if (oracle == null || !oracle.Success || oracle.Body == null)
                    throw new InvalidOperationException(oracle?.ErrorMessage ?? "Oracle unavailable");
                double norm2 = plane.Normal.Sum(v => v * v);
                if (norm2 < 1e-20) throw new InvalidOperationException("Invalid reflection normal");
                double[][][] reflected = null;
                if (source.BeforeSupport.Boundaries != null)
                {
                    reflected = source.BeforeSupport.Boundaries.Select(e => e.Select(p =>
                    {
                        double d = Enumerable.Range(0, 3).Sum(i => (p[i] - plane.Origin[i]) * plane.Normal[i]) / norm2;
                        return Enumerable.Range(0, 3).Select(i => p[i] - 2 * d * plane.Normal[i]).ToArray();
                    }).ToArray()).ToArray();
                    double oracleBest;
                    int oracleCount = SupportCandidates23(oracle.Body, source.BeforeSupport, reflected, out oracleBest);
                    Audit21(model, "[CUT23] ORACLE feature=" + feature.Name + " candidates=" + oracleCount + " bestResidual_m=" + oracleBest.ToString("R"));
                    if (oracleCount != 1) throw new InvalidOperationException("Source support does not uniquely match reflected source body");
                }
                else Audit21(model, "[CUT23] ORACLE feature=" + feature.Name + " supportKind=" +
                    source.BeforeSupport.Kind + " faceBoundaryCheck=NOT_APPLICABLE");
                stage = "BEFORE_CUT_STATE";
                definition = feature.GetDefinition() as IExtrudeFeatureData2;
                if (definition == null || !(access = definition.AccessSelections(model, null)))
                    throw new InvalidOperationException("Cannot access before-cut state");
                string error;
                var live = BodyOperationsHelper.GetSolidBodyCopyStrict(model, out error);
                if (live == null) throw new InvalidOperationException(error);
                var missing = BodyOperationsHelper.BooleanCutStrict(oracle.Body, live, "CUT23_BEFORE_MISSING");
                var extra = BodyOperationsHelper.BooleanCutStrict(live, oracle.Body, "CUT23_BEFORE_EXTRA");
                double tolerance = Math.Max(BodyOperationsHelper.ABSOLUTE_GEOMETRY_TOLERANCE,
                    BodyOperationsHelper.GetBodyVolume(oracle.Body) * BodyOperationsHelper.BODY_TRANSFORM_RELATIVE_TOLERANCE);
                // Bodies in the accepted self-contact class cannot be subtracted;
                // the dedicated reduced proof replaces the Boolean pair there.
                bool selfContact71 = (!missing.Success || !extra.Success) &&
                    NativeBodyCoincidence67.ConfirmUnavailableBoolean(oracle.Body, live, missing, extra, "CUT23_BEFORE71");
                if (!selfContact71 && (!missing.Success || !extra.Success || BodyOperationsHelper.SumBodyVolumes(missing.Bodies) > tolerance ||
                    BodyOperationsHelper.SumBodyVolumes(extra.Bodies) > tolerance) &&
                    !NativeBodyCoincidence67.ConfirmSurfaceChainResidual71(oracle.Body, live, "CUT23_BEFORE71"))
                    throw new InvalidOperationException("Live before-cut body differs from reflected source checkpoint");
                if (reflected != null)
                {
                    stage = "LIVE_SUPPORT";
                    double liveBest;
                    int liveCount = SupportCandidates23(live, source.BeforeSupport, reflected, out liveBest);
                    Audit21(model, "[CUT23] LIVE feature=" + feature.Name + " candidates=" + liveCount + " bestResidual_m=" + liveBest.ToString("R"));
                    if (liveCount != 1) throw new InvalidOperationException("Live support missing or ambiguous");
                }
                source.BeforeChainVerified23 = true;
                Audit21(model, "[CUT23] SUMMARY feature=" + feature.Name + " result=PRECHECK_PASS mappingApplied=False cutValidated=False");
            }
            catch (Exception ex)
            {
                Audit21(model, "[CUT23] SUMMARY feature=" + feature.Name + " result=FAIL firstFailedStage=" + stage + " reason=" + ex.Message);
                throw new InvalidOperationException("CUT23 " + stage + ": " + ex.Message, ex);
            }
            finally { if (access) definition.ReleaseSelectionAccess(); }
        }

        private static string SupportLabel21(SketchSupportSnapshot20 support)
        {
            return support == null ? "support=False" : "kind=" + (support.Kind ?? "Unknown") +
                " area_m2=" + support.Area.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                " boundaryCount=" + (support.Boundaries == null ? 0 : support.Boundaries.Length);
        }

        public static void ReportCutAudit21(ModelDoc2 model, Feature profile, PlaneData plane,
            SketchSupportSnapshot20 after, CutAuditSnapshot21 source, string phase)
        {
            Audit21(model, "BEGIN phase=" + phase + " sketch=" + profile.Name + " report=" + model.GetPathName() + ".CutAudit21.txt");
            try
            {
                var sketch = profile.GetSpecificFeature2() as Sketch;
                var frame = sketch == null ? null : sketch.ModelToSketchTransform.ArrayData as double[];
                Audit21(model, "FRAME=" + (frame == null ? "unavailable" : string.Join(",", frame.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))));
                if (source == null || source.CaptureError != null)
                    Audit21(model, "INCOMPLETE source=" + (source == null ? "not captured" : source.CaptureError));
                Func<double[], double[]> reflect = p =>
                {
                    double d = Enumerable.Range(0, 3).Sum(i => (p[i] - plane.Origin[i]) * plane.Normal[i]);
                    return Enumerable.Range(0, 3).Select(i => p[i] - 2 * d * plane.Normal[i]).ToArray();
                };
                foreach (var pair in new[] {
                    Tuple.Create("BEFORE_CUT", source == null ? null : source.BeforeSupport), Tuple.Create("AFTER_CUT", after) })
                {
                    Audit21(model, "EXPECTED phase=" + pair.Item1 + " " + SupportLabel21(pair.Item2));
                    if (pair.Item2 == null || pair.Item2.Boundaries == null)
                    {
                        Audit21(model, "SUMMARY expected=" + pair.Item1 + " result=NON_FACE_SUPPORT diagnosticOnly=True");
                        continue;
                    }
                    var expected = pair.Item2.Boundaries.SelectMany(edge => edge).Select(reflect).ToArray();
                    if (expected.Length == 0) { Audit21(model, "INCOMPLETE empty boundary"); continue; }
                    int count = 0, close = 0, errors = 0;
                    foreach (Body2 body in ((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[] ?? new object[0])
                    foreach (Face2 face in body.GetFaces() as object[] ?? new object[0])
                    {
                        int index = count++;
                        try
                        {
                            double maxDistance = 0;
                            foreach (var p in expected)
                            {
                                var q = face.GetClosestPointOn(p[0], p[1], p[2]) as double[];
                                if (q == null || q.Length < 3) throw new InvalidOperationException("Closest point unavailable");
                                maxDistance = Math.Max(maxDistance, Math.Sqrt(Enumerable.Range(0, 3).Sum(i => (p[i] - q[i]) * (p[i] - q[i]))));
                            }
                            double areaError = Math.Abs(face.GetArea() - pair.Item2.Area);
                            int edgeCount = (face.GetEdges() as object[] ?? new object[0]).Length;
                            bool near = maxDistance <= 1e-7;
                            if (near) close++;
                            Audit21(model, "FACE expected=" + pair.Item1 + " index=" + index + " creator=" +
                                ((face.GetFeature() as Feature)?.Name ?? "unknown") + " edges=" + edgeCount +
                                " areaError_m2=" + areaError.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                                " maxBoundaryToFace_m=" + maxDistance.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " near=" + near);
                        }
                        catch (Exception ex) { errors++; Audit21(model, "FACE_READ_FAILED index=" + index + " reason=" + ex.Message); }
                    }
                    Audit21(model, "SUMMARY expected=" + pair.Item1 + " faces=" + count + " nearCandidates=" + close + " unreadable=" + errors +
                        " result=" + (errors > 0 ? "INCOMPLETE" : close == 0 ? "NO_NEAR_FACE" : close == 1 ? "ONE_NEAR_FACE" : "MULTIPLE_NEAR_FACES") +
                        " diagnosticOnly=True notProofOfExactMatch=True");
                }
            }
            catch (Exception ex) { Audit21(model, "INCOMPLETE phase=" + phase + " reason=" + ex.Message); }
            Audit21(model, "END phase=" + phase + " sketch=" + profile.Name);
        }

        public static SketchSupportSnapshot20 CaptureSupport20(Sketch sketch, ModelDoc2 model = null)
        {
            if (sketch == null) return null;
            int type = 0;
            object reference = sketch.GetReferenceEntity(ref type);
            var face = reference as Face2;
            SketchSupportSnapshot20 result = face != null
                ? CaptureFaceSupport24(face)
                : new SketchSupportSnapshot20();
            result.Kind = face != null ? "Face" : reference is Feature ? "ReferencePlane" :
                reference == null ? "ImplicitPlane" : reference.GetType().Name;
            result.ReferenceType = type;
            try { result.PersistentReference = reference == null || model == null ? null : model.Extension.GetPersistReference3(reference) as byte[]; }
            catch { result.PersistentReference = null; }
            CaptureSketchPlane50(sketch, out result.PlaneOrigin, out result.PlaneNormal);
            CreateMirrorPartPackage.LogDebug("[SUPPORT50][CAPTURE] kind=" + result.Kind +
                " selectionType=" + type + " persistent=" + (result.PersistentReference != null && result.PersistentReference.Length > 0));
            return result;
        }

        private static SketchSupportSnapshot20 CaptureFaceSupport24(Face2 face)
        {
            if (face == null) return null;
            var snapshot = new SketchSupportSnapshot20 { Kind = "Face", Area = face.GetArea(), Boundaries =
                (face.GetEdges() as object[] ?? new object[0]).Cast<Edge>().Select(SampleEdge24).ToArray() };
            double residual = BoundaryResidual23(face, snapshot.Boundaries);
            CreateMirrorPartPackage.LogDebug("[EDGE24] FACE_SELF_CHECK residual_m=" + residual.ToString("R") +
                " result=" + (residual <= 1e-7 ? "PASS" : "FAIL"));
            if (residual > 1e-7) throw new InvalidOperationException("EDGE24 sampled boundary is not on its source face.");
            return snapshot;
        }

        // Set for a run whose base feature is a Lofted Bend, before anything is captured.
        public static bool TolerantSplineEdges71;

        // Shared by source capture and target matching. Keep the public name for callers.
        // EDGE25: never mix ICurve parameter space with the trimmed IEdge evaluator.
        public static double[][] SampleEdge24(Edge edge)
        {
            var curve = edge.GetCurve() as Curve;
            if (curve == null) throw new InvalidOperationException("EDGE25 curve unavailable.");
            var startVertex = edge.GetStartVertex() as Vertex;
            var endVertex = edge.GetEndVertex() as Vertex;
            double[] start = startVertex?.GetPoint() as double[];
            double[] end = endVertex?.GetPoint() as double[];
            bool line = curve.IsLine();
            double[][] samples;
            if (line)
            {
                RequirePoint25(start); RequirePoint25(end);
                samples = Enumerable.Range(0, 9).Select(i => Enumerable.Range(0, 3)
                    .Select(axis => start[axis] + (end[axis] - start[axis]) * i / 8.0).ToArray()).ToArray();
            }
            else
            {
                // GetCurveParams2 and Edge.Evaluate2 are a documented pair. No Sense sign fix.
                var parameters = edge.GetCurveParams2() as double[];
                if (parameters == null || parameters.Length < 8 || parameters.Take(8).Any(v => double.IsNaN(v) || double.IsInfinity(v)) || parameters[6] == parameters[7])
                    throw new InvalidOperationException("EDGE25 invalid edge parameter interval; type=" + curve.Identity());
                // Closed edges can have no topological vertices; retain their seam endpoints.
                if (start == null) start = parameters.Take(3).ToArray();
                if (end == null) end = parameters.Skip(3).Take(3).ToArray();
                samples = Enumerable.Range(0, 9).Select(i =>
                {
                    var value = edge.Evaluate2(parameters[6] + (parameters[7] - parameters[6]) * i / 8.0, 0) as double[];
                    RequirePoint25(value);
                    // Current API appends a packed status; older versions return XYZ only.
                    if (value.Length > 3 && BitConverter.ToInt32(BitConverter.GetBytes(value[3]), 0) != 1)
                        throw new InvalidOperationException("EDGE25 edge evaluation status failed; type=" + curve.Identity());
                    return value.Take(3).ToArray();
                }).ToArray();
            }
            RequirePoint25(start); RequirePoint25(end);
            Func<double[], double[], double> distance = (a, b) => Math.Sqrt(Enumerable.Range(0, 3).Sum(i => (a[i] - b[i]) * (a[i] - b[i])));
            double endpointError = Math.Min(Math.Max(distance(samples[0], start), distance(samples[8], end)),
                Math.Max(distance(samples[0], end), distance(samples[8], start)));
            // A Part with a re-attached in-context surface chain carries the accepted
            // micrometre sliver; its tolerant edges do not meet their vertices to 1e-7.
            // The spline body of a Lofted Bend has such edges in the source itself
            // (natively a B-curve edge ending 4.2e-6 m from its vertex, and one ending
            // 6.3e-6 m from it in another Lofted Bend Part). The limit is the accepted
            // local deviation: this is how far the modeller lets an edge end from
            // its vertex, not something the mirror adds.
            double edgeTolerance71 = NativeBodyCoincidence67.SurfaceChainResidualAllowed71 || TolerantSplineEdges71 ? NativeBodyCoincidence67.LocalDeviationLimit71 : 1e-7;
            if (double.IsNaN(endpointError) || endpointError > edgeTolerance71)
                throw new InvalidOperationException("EDGE25 endpoint mismatch: type=" + curve.Identity() + " residual_m=" + endpointError.ToString("R"));
            if (endpointError > 5e-6)
                CreateMirrorPartPackage.LogDebug("[EDGE25][TOLERANT_EDGE_END] type=" + curve.Identity() + " endpointResidual_m=" + endpointError.ToString("E2") +
                    " limit_m=" + edgeTolerance71.ToString("E0"));
            double edgeError = 0;
            foreach (var point in samples)
            {
                var nearest = edge.GetClosestPointOn(point[0], point[1], point[2]) as double[];
                RequirePoint25(nearest);
                edgeError = Math.Max(edgeError, distance(point, nearest));
            }
            if (edgeError > edgeTolerance71) throw new InvalidOperationException("EDGE25 sample outside trimmed edge; type=" + curve.Identity() + " residual_m=" + edgeError.ToString("R"));
            CreateMirrorPartPackage.LogDebug("[EDGE25] SAMPLE_PASS mode=" + (line ? "LINE_VERTICES" : "EDGE_EVALUATE") +
                " type=" + curve.Identity() + " endpointResidual_m=" + endpointError.ToString("R") + " edgeResidual_m=" + edgeError.ToString("R"));
            return samples;
        }

        private static void RequirePoint25(double[] value)
        {
            if (value == null || value.Length < 3 || value.Take(3).Any(v => double.IsNaN(v) || double.IsInfinity(v)))
                throw new InvalidOperationException("EDGE25 invalid point data.");
        }

        private static void CaptureSketchPlane50(Sketch sketch, out double[] origin, out double[] normal)
        {
            origin = null;
            normal = null;
            try
            {
                var math = SwAddin.InstanceSwApp.GetMathUtility() as IMathUtility;
                MathTransform sketchToModel = sketch.ModelToSketchTransform.IInverse();
                var modelOrigin = ((MathPoint)math.CreatePoint(new[] { 0.0, 0.0, 0.0 }))
                    .MultiplyTransform(sketchToModel) as MathPoint;
                var modelNormal = ((MathVector)math.CreateVector(new[] { 0.0, 0.0, 1.0 }))
                    .MultiplyTransform(sketchToModel) as MathVector;
                origin = modelOrigin.ArrayData as double[];
                normal = modelNormal.ArrayData as double[];
                double length = Math.Sqrt(normal.Take(3).Sum(value => value * value));
                if (length <= 1e-12) throw new InvalidOperationException("zero sketch-plane normal");
                normal = normal.Take(3).Select(value => value / length).ToArray();
            }
            catch
            {
                origin = null;
                normal = null;
            }
        }

        // An oblique mirror has no 2D reflection axis in the original sketch plane.
        // It is safe to use the existing model-space point/primitive replay only after
        // the copied sketch has actually been rehosted on the reflected source plane.
        public static bool VerifyReflectedSketchPlane56(Sketch target,
            SketchSupportSnapshot20 source, List<SketchPointSnapshot> points,
            PlaneData plane, out double normalError, out double planeResidual)
        {
            normalError = double.PositiveInfinity;
            planeResidual = double.PositiveInfinity;
            if (target == null || source?.PlaneOrigin == null || source.PlaneNormal == null ||
                source.PlaneOrigin.Length < 3 || source.PlaneNormal.Length < 3 ||
                points == null || points.Count == 0 || plane == null)
                return false;
            double[] targetOrigin, targetNormal;
            CaptureSketchPlane50(target, out targetOrigin, out targetNormal);
            if (targetOrigin == null || targetNormal == null) return false;
            double[] reflectedOrigin = BodyOperationsHelper.ReflectPointAcrossPlane(source.PlaneOrigin, plane);
            if (reflectedOrigin == null) return false;
            double[] expectedNormal = CutOrientation44.Reflect(source.PlaneNormal, plane.Normal);
            double[] actualNormal = CutOrientation44.Unit(targetNormal);
            double dot = Enumerable.Range(0, 3).Sum(i => expectedNormal[i] * actualNormal[i]);
            normalError = 1.0 - Math.Abs(dot);
            planeResidual = Math.Abs(Enumerable.Range(0, 3).Sum(i =>
                (reflectedOrigin[i] - targetOrigin[i]) * actualNormal[i]));
            foreach (SketchPointSnapshot point in points)
            {
                if (!point.HasModelCoords) return false;
                double[] reflected = BodyOperationsHelper.ReflectPointAcrossPlane(
                    new[] { point.ModelX, point.ModelY, point.ModelZ }, plane);
                if (reflected == null) return false;
                planeResidual = Math.Max(planeResidual, Math.Abs(Enumerable.Range(0, 3).Sum(i =>
                    (reflected[i] - targetOrigin[i]) * actualNormal[i])));
            }
            return !double.IsNaN(normalError) && !double.IsInfinity(normalError) &&
                normalError <= 1e-8 && planeResidual <= 1e-7;
        }

        private static double[] SketchPointWorld50(SketchPoint point)
        {
            if (point == null) return null;
            var owner = point.GetSketch() as Sketch;
            if (owner == null) return null;
            var math = SwAddin.InstanceSwApp.GetMathUtility() as IMathUtility;
            MathTransform transform = owner.ModelToSketchTransform.IInverse();
            var local = math.CreatePoint(new[] { point.X, point.Y, point.Z }) as MathPoint;
            var world = local == null ? null : local.MultiplyTransform(transform) as MathPoint;
            return world == null ? null : (world.ArrayData as double[])?.Take(3).ToArray();
        }

        private static IEnumerable<Feature> EnumerateFeatures50(ModelDoc2 model)
        {
            Feature current = model == null ? null : model.FirstFeature() as Feature;
            while (current != null)
            {
                yield return current;
                Feature child = current.GetFirstSubFeature() as Feature;
                while (child != null)
                {
                    yield return child;
                    child = child.GetNextSubFeature() as Feature;
                }
                current = current.GetNextFeature() as Feature;
            }
        }

        private static SketchPoint FindReflectedSketchPoint50(ModelDoc2 model, double[] sourceWorld, PlaneData plane)
        {
            if (sourceWorld == null || sourceWorld.Length < 3)
                throw new InvalidOperationException("REFERENCE50: SketchPoint world coordinates unavailable.");
            double[] expected = ReflectReference30(sourceWorld, plane);
            var candidates = new List<Tuple<SketchPoint, double>>();
            foreach (Feature feature in EnumerateFeatures50(model))
            {
                Sketch owner = null;
                try { owner = feature.GetSpecificFeature2() as Sketch; } catch { }
                if (owner == null) continue;
                foreach (SketchPoint candidate in (owner.GetSketchPoints2() as object[] ?? new object[0]).OfType<SketchPoint>())
                {
                    double[] actual;
                    try { actual = SketchPointWorld50(candidate); } catch { continue; }
                    if (actual == null) continue;
                    double distance = Math.Sqrt(Enumerable.Range(0, 3).Sum(i => Math.Pow(actual[i] - expected[i], 2)));
                    if (distance <= 1e-7)
                    {
                        bool duplicate = candidates.Any(existing =>
                        {
                            try { return SwAddin.InstanceSwApp.IsSame(existing.Item1, candidate) == (int)swObjectEquality.swObjectSame; }
                            catch { return false; }
                        });
                        if (!duplicate) candidates.Add(Tuple.Create(candidate, distance));
                    }
                }
            }
            var unique = candidates.OrderBy(item => item.Item2).ToArray();
            CreateMirrorPartPackage.LogDebug("[REFERENCE50][SKETCH_POINT_RESOLVE] matches=" + unique.Length +
                " expected_m=" + string.Join(",", expected.Select(value => value.ToString("R"))));
            if (unique.Length != 1)
                throw new InvalidOperationException("REFERENCE50: Reflected SketchPoint is not unique; matches=" + unique.Length);
            return unique[0].Item1;
        }

        public static void EnsureCutSupport52(ISldWorks app, ModelDoc2 model, Feature cut,
            Feature profile, CutAuditSnapshot21 snapshot, PlaneData plane)
        {
            if (snapshot == null || snapshot.CaptureError != null || snapshot.BeforeSupport == null ||
                snapshot.CirclePoints26 == null || snapshot.CirclePoints26.Count == 0)
                throw new InvalidOperationException("CUT52 before-cut support snapshot is incomplete.");
            string cutName = cut.Name, profileName = profile.Name;
            model.ClearSelection2(true);
            if (!model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, cutName))
                throw new InvalidOperationException("CUT52 cannot enter before-cut checkpoint: " + cutName);
            try
            {
                // Reacquire inside this checkpoint; no face from AccessSelections
                // survives ReleaseSelectionAccess or a rollback transition here.
                var liveProfile = ((PartDoc)model).FeatureByName(profileName) as Feature;
                if (liveProfile == null) throw new InvalidOperationException("CUT52 profile unavailable before cut: " + profileName);
                Audit21(model, "[CUT52][SUPPORT_BEGIN] feature=" + cutName +
                    " checkpoint=BEFORE_CUT snapshot=BEFORE_CUT");
                EnsureReflectedSupport20(app, model, liveProfile, snapshot.CirclePoints26,
                    snapshot.BeforeSupport, plane);
                Audit21(model, "[CUT52][SUPPORT_PASS] feature=" + cutName);
            }
            finally
            {
                model.ClearSelection2(true);
                bool restored = model.FeatureManager.EditRollback(
                    (int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, cutName);
                Audit21(model, "[CUT52][CHECKPOINT_RESTORED] feature=" + cutName + " success=" + restored);
                if (!restored) throw new InvalidOperationException("CUT52 cannot restore after-cut checkpoint: " + cutName);
            }
        }

        public static void EnsureReflectedSupport20(ISldWorks app, ModelDoc2 model, Feature feature,
            List<SketchPointSnapshot> points, SketchSupportSnapshot20 support, PlaneData plane)
        {
            if (points == null || points.Count == 0)
                throw new InvalidOperationException("CUTSPACE20: No original sketch points.");
            var math = (IMathUtility)app.GetMathUtility();
            double normSquared52 = plane.Normal.Sum(v => v * v);
            if (double.IsNaN(normSquared52) || double.IsInfinity(normSquared52) || normSquared52 < 1e-20)
                throw new InvalidOperationException("CUT52 invalid reflection normal.");
            Func<double[], double[]> reflect = p =>
            {
                double d = Enumerable.Range(0, 3).Sum(i => (p[i] - plane.Origin[i]) * plane.Normal[i]) / normSquared52;
                return Enumerable.Range(0, 3).Select(i => p[i] - 2 * d * plane.Normal[i]).ToArray();
            };
            var targets = points.Select(p =>
            {
                if (!p.HasModelCoords) throw new InvalidOperationException("CUTSPACE20: Original model coordinates unavailable for point " + p.Id1 + "," + p.Id2);
                return reflect(new[] { p.ModelX, p.ModelY, p.ModelZ });
            }).ToArray();
            Func<double> residual = () =>
            {
                var sketch = (Sketch)feature.GetSpecificFeature2();
                return targets.Max(p =>
                {
                    var point = (MathPoint)math.CreatePoint(p);
                    var local = (MathPoint)point.MultiplyTransform(sketch.ModelToSketchTransform);
                    return Math.Abs(((double[])local.ArrayData)[2]);
                });
            };
            double before = residual();
            CreateMirrorPartPackage.LogDebug("[CUTSPACE20][PLANE_CHECK] sketch=" + feature.Name + " maxResidual_m=" + before.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            if (before <= 1e-7) return;
            // Accepted local-deviation class (re-attached surface chain / Lofted Bend):
            // a plane that holds the reflected points within that deviation is the
            // reflected plane (natively a plane lost with its in-context face, almost
            // normal to the mirror plane: 2.2e-5 m). The body check after the feature
            // measures what this costs.
            double planeLimit71 = NativeBodyCoincidence67.SurfaceChainResidualAllowed71 ? NativeBodyCoincidence67.LocalDeviationLimit71 : 1e-7;
            if (before <= planeLimit71)
            {
                CreateMirrorPartPackage.LogDebug("[CUTSPACE71][PLANE_WITHIN_LOCAL_DEVIATION] sketch=" + feature.Name + " residual_m=" +
                    before.ToString("E2", System.Globalization.CultureInfo.InvariantCulture) + " limit_m=" + planeLimit71.ToString("E0", System.Globalization.CultureInfo.InvariantCulture));
                return;
            }
            if (support == null)
                throw new InvalidOperationException("CUTSPACE50: Source support was not captured: " + feature.Name);
            Face2 mappedFace = null;
            if (support.Boundaries != null)
            {
                var boundaries = support.Boundaries.Select(edges => edges.Select(reflect).ToArray()).ToArray();
                mappedFace = (Face2)ADDIN.Commands.MirrorV7.MirrorInPlace.RollbackReplayEngineV7.FindFace(model, support.Area, boundaries);
            }
            else
            {
                mappedFace = FindPlanarSupportForPoints50(model, targets, planeLimit71);
            }
            if (mappedFace == null)
                throw new InvalidOperationException("CUTSPACE50: No unique reflected planar support for " + support.Kind + ": " + feature.Name);
            model.ClearSelection2(true);
            if (!feature.Select2(false, 0) || !((Entity)mappedFace).Select4(true, null) || !model.ChangeSketchPlane())
                throw new InvalidOperationException("CUTSPACE20: Cannot apply reflected support: " + feature.Name);
            double after = residual();
            CreateMirrorPartPackage.LogDebug("[CUTSPACE20][SUPPORT_REBOUND] sketch=" + feature.Name + " maxResidual_m=" + after.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            if (after > planeLimit71) throw new InvalidOperationException("CUTSPACE20: Mapped support does not contain reflected points.");
        }

        private static Face2 FindPlanarSupportForPoints50(ModelDoc2 model, double[][] targets, double limit = 1e-7)
        {
            var matches = new List<Tuple<Face2, double>>();
            foreach (Body2 body in (object[])((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) ?? new object[0])
                foreach (Face2 face in (object[])body.GetFaces() ?? new object[0])
                {
                    try
                    {
                        Surface surface = face.GetSurface() as Surface;
                        if (surface == null || !surface.IsPlane()) continue;
                        double residual = 0.0;
                        foreach (double[] target in targets)
                        {
                            double[] closest = surface.GetClosestPointOn(target[0], target[1], target[2]) as double[];
                            if (closest == null || closest.Length < 3) { residual = double.PositiveInfinity; break; }
                            residual = Math.Max(residual, Math.Sqrt(Enumerable.Range(0, 3)
                                .Sum(i => Math.Pow(target[i] - closest[i], 2))));
                        }
                        if (residual <= limit) matches.Add(Tuple.Create(face, residual));
                    }
                    catch { }
                }
            CreateMirrorPartPackage.LogDebug("[SUPPORT50][PLANAR_TARGET_SEARCH] candidates=" + matches.Count);
            return matches.Count == 1 ? matches[0].Item1 : null;
        }

        public static List<SketchPointSnapshot> CapturePristineSketchPoints(Sketch swSketch)
        {
            List<SketchPointSnapshot> list = new List<SketchPointSnapshot>();
            if (swSketch == null) return list;

            MathTransform s2m = null;
            try
            {
                MathTransform m2s = swSketch.ModelToSketchTransform;
                if (m2s != null) s2m = m2s.Inverse() as MathTransform;
            }
            catch { }

            object[] sketchPointsObj = EnumerateSketchPoints28(swSketch);
            if (sketchPointsObj.Length == 0) return list;

            ISldWorks swApp = SwAddin.InstanceSwApp;
            IMathUtility mathUtility = swApp != null ? swApp.GetMathUtility() as IMathUtility : null;

            for (int i = 0; i < sketchPointsObj.Length; i++)
            {
                SketchPoint pt = sketchPointsObj[i] as SketchPoint;
                if (pt == null) continue;

                int id1 = 0, id2 = 0;
                try
                {
                    int[] idArr = pt.GetID() as int[];
                    if (idArr != null && idArr.Length >= 2)
                    {
                        id1 = idArr[0];
                        id2 = idArr[1];
                    }
                }
                catch { }

                double mx = 0, my = 0, mz = 0;
                bool hasModel = false;
                if (s2m != null && mathUtility != null)
                {
                    try
                    {
                        MathPoint mp2D = mathUtility.CreatePoint(new double[] { pt.X, pt.Y, swSketch.Is3D() ? pt.Z : 0.0 }) as MathPoint;
                        MathPoint mp3D = mp2D != null ? mp2D.MultiplyTransform(s2m) as MathPoint : null;
                        double[] a = mp3D != null ? mp3D.ArrayData as double[] : null;
                        if (a != null && a.Length >= 3)
                        {
                            mx = a[0]; my = a[1]; mz = a[2];
                            hasModel = true;
                        }
                    }
                    catch { }
                }

                list.Add(new SketchPointSnapshot
                {
                    Id1 = id1,
                    Id2 = id2,
                    Index = i,
                    X = pt.X,
                    Y = pt.Y,
                    ModelX = mx,
                    ModelY = my,
                    ModelZ = mz,
                    HasModelCoords = hasModel
                });
            }

            return list;
        }

        // GetSketchPoints2 omits points owned by some construction/reference segments.
        // Dimensions can reference those points, so the reflection set must include them too.
        private static double[] ReflectReference30(double[] point, PlaneData plane)
        {
            if (plane == null) throw new InvalidOperationException("REFERENCE30: Missing reflection plane.");
            double norm = plane.Normal.Sum(x => x * x);
            if (norm < 1e-20) throw new InvalidOperationException("REFERENCE30: Invalid reflection normal.");
            double d = Enumerable.Range(0, 3).Sum(i => (point[i] - plane.Origin[i]) * plane.Normal[i]) / norm;
            return Enumerable.Range(0, 3).Select(i => point[i] - 2 * d * plane.Normal[i]).ToArray();
        }

        private static bool IsOwnedEntity31(Sketch owner, object entity)
        {
            if (owner == null || entity == null) return false;
            Func<object, object, bool> same = (a, b) =>
            {
                try { return SwAddin.InstanceSwApp.IsSame(a, b) == (int)swObjectEquality.swObjectSame; }
                catch { return false; }
            };
            Func<int[], int[], bool> ids = (a, b) => a != null && b != null && a.SequenceEqual(b);
            var segment = entity as SketchSegment;
            if (segment != null)
            {
                if (!same(segment.GetSketch(), owner)) return false;
                var source = SegmentPoints31(segment);
                var matches = (owner.GetSketchSegments() as object[] ?? new object[0]).Cast<SketchSegment>().Count(candidate =>
                    candidate.GetType() == segment.GetType() && ids(candidate.GetID() as int[], segment.GetID() as int[]) &&
                    GeometryResidual31(source, SegmentPoints31(candidate)) <= 1e-9);
                return matches == 1;
            }
            var point = entity as SketchPoint;
            if (point != null)
            {
                if (!same(point.GetSketch(), owner)) return false;
                var matches = (owner.GetSketchPoints2() as object[] ?? new object[0]).Cast<SketchPoint>().Count(candidate =>
                    ids(candidate.GetID() as int[], point.GetID() as int[]) &&
                    Math.Sqrt(Math.Pow(candidate.X - point.X, 2) + Math.Pow(candidate.Y - point.Y, 2) + Math.Pow(candidate.Z - point.Z, 2)) <= 1e-9);
                return matches == 1;
            }
            return false;
        }

        private static double[][] SegmentPoints31(SketchSegment segment)
        {
            Func<SketchPoint, double[]> p = value => new[] { value.X, value.Y, value.Z };
            var line = segment as SketchLine;
            if (line != null) return new[] { p((SketchPoint)line.GetStartPoint2()), p((SketchPoint)line.GetEndPoint2()) };
            var arc = segment as SketchArc;
            if (arc != null) return new[] { p((SketchPoint)arc.GetStartPoint2()), p((SketchPoint)arc.GetCenterPoint2()), p((SketchPoint)arc.GetEndPoint2()) };
            return null;
        }

        private static double GeometryResidual31(double[][] a, double[][] b)
        {
            if (a == null || b == null || a.Length != b.Length) return double.PositiveInfinity;
            Func<double[], double[], double> distance = (x, y) => Math.Sqrt(Enumerable.Range(0, 3).Sum(i => Math.Pow(x[i] - y[i], 2)));
            double direct = Enumerable.Range(0, a.Length).Max(i => distance(a[i], b[i]));
            double reverse = Enumerable.Range(0, a.Length).Max(i => distance(a[i], b[b.Length - 1 - i]));
            return Math.Min(direct, reverse);
        }

        private static Face2 FindCarrierFace31(ModelDoc2 model, SketchLine line)
        {
            var math = SwAddin.InstanceSwApp.GetMathUtility() as IMathUtility;
            var owner = ((SketchSegment)line).GetSketch();
            var transform = owner.ModelToSketchTransform.IInverse();
            Func<SketchPoint, double[]> world = point => (double[])((MathPoint)((MathPoint)math.CreatePoint(
                new[] { point.X, point.Y, point.Z })).MultiplyTransform(transform)).ArrayData;
            double[] a = world((SketchPoint)line.GetStartPoint2());
            double[] b = world((SketchPoint)line.GetEndPoint2());
            double[][] probes = Enumerable.Range(0, 9).Select(k => Enumerable.Range(0, 3)
                .Select(i => a[i] + (b[i] - a[i]) * k / 8.0).ToArray()).ToArray();
            var matches = new List<Face2>();
            var surfaceCandidates45 = new List<Tuple<Face2, double, double, string>>();
            foreach (Body2 body in (object[])((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) ?? new object[0])
                foreach (Face2 face in (object[])body.GetFaces() ?? new object[0])
                {
                    double trimmedResidual45 = 0.0;
                    bool trimmedReadable45 = true;
                    foreach (double[] probe in probes)
                    {
                        var hit = face.GetClosestPointOn(probe[0], probe[1], probe[2]) as double[];
                        if (hit == null || hit.Length < 3) { trimmedReadable45 = false; break; }
                        trimmedResidual45 = Math.Max(trimmedResidual45, Math.Sqrt(Enumerable.Range(0, 3)
                            .Sum(i => Math.Pow(probe[i] - hit[i], 2))));
                    }
                    if (trimmedReadable45 && trimmedResidual45 <= 1e-7) matches.Add(face);

                    // A solver proxy may lie on the untrimmed support surface but outside
                    // the trimmed boundary of its carrier face. Query ISurface directly so
                    // this also covers cylindrical bends, cones, spheres, tori and NURBS.
                    try
                    {
                        var surface45 = face.GetSurface() as Surface;
                        if (surface45 == null || !trimmedReadable45) continue;
                        double supportResidual45 = 0.0;
                        bool supportReadable45 = true;
                        foreach (double[] probe in probes)
                        {
                            var supportHit45 = surface45.GetClosestPointOn(probe[0], probe[1], probe[2]) as double[];
                            if (supportHit45 == null || supportHit45.Length < 3)
                            {
                                supportReadable45 = false;
                                break;
                            }
                            supportResidual45 = Math.Max(supportResidual45, Math.Sqrt(Enumerable.Range(0, 3)
                                .Sum(i => Math.Pow(probe[i] - supportHit45[i], 2))));
                        }
                        string surfaceKind45 = surface45.IsPlane() ? "Plane" :
                            surface45.IsCylinder() ? "Cylinder" :
                            surface45.IsCone() ? "Cone" :
                            surface45.IsSphere() ? "Sphere" :
                            surface45.IsTorus() ? "Torus" : "General";
                        if (supportReadable45 && supportResidual45 <= 1e-7)
                            surfaceCandidates45.Add(Tuple.Create(face, trimmedResidual45, supportResidual45, surfaceKind45));
                    }
                    catch { }
                }
            if (matches.Count == 1) return matches[0];
            if (matches.Count > 1)
                throw new InvalidOperationException("REFERENCE35: Proxy carrier Face is not unique; matches=" + matches.Count);

            var ranked45 = surfaceCandidates45.OrderBy(candidate => candidate.Item2).ToArray();
            if (ranked45.Length == 0)
                throw new InvalidOperationException("REFERENCE35: Proxy carrier Face is not unique; matches=0; surfaceSupportMatches=0; proxy=" +
                    string.Join(",", a.Select(value => value.ToString("R"))) + "->" +
                    string.Join(",", b.Select(value => value.ToString("R"))));
            double best45 = ranked45[0].Item2;
            double second45 = ranked45.Length > 1 ? ranked45[1].Item2 : double.PositiveInfinity;
            double uniquenessTolerance45 = Math.Max(1e-7, Math.Max(best45, 1e-7) * 1e-6);
            CreateMirrorPartPackage.LogDebug("[REFERENCE45][CARRIER_SURFACE_FALLBACK] candidates=" + ranked45.Length +
                " bestTrimmedResidual_m=" + best45.ToString("R") + " secondTrimmedResidual_m=" +
                second45.ToString("R") + " bestSurfaceResidual_m=" + ranked45[0].Item3.ToString("R") +
                " bestSurfaceKind=" + ranked45[0].Item4 + " uniquenessTolerance_m=" + uniquenessTolerance45.ToString("R"));
            if (ranked45.Length > 1 && second45 - best45 <= uniquenessTolerance45)
                throw new InvalidOperationException("REFERENCE45: Coplanar carrier Face is ambiguous; candidates=" +
                    ranked45.Length + " best=" + best45.ToString("R") + " second=" + second45.ToString("R"));
            return ranked45[0].Item1;
        }

        // Geometry-first replay mirrors a profile by moving its points and gives up
        // the relations to the source side. One kind of segment cannot be mirrored
        // that way: a spline (or any segment other than a line or an arc) made with
        // Convert Entities is a copy of a model edge, and its shape is not carried by
        // sketch points. Such a relation is recorded here and, when the point move is
        // rejected, re-attached to the mirrored edge; the segment then follows it.
        // Offset Entities is left alone on purpose: its relation finds the edge on the
        // mirrored body by itself and on the right side, and re-attached by geometry
        // it took the twin edge across the sheet thickness and came out on the other
        // side (native: OffsetRelationProbe71, 107 mm from the mirror image).
        private static List<ConvertedEdge71> CaptureConvertedEdges71(Sketch sketch)
        {
            var result = new List<ConvertedEdge71>();
            try
            {
                if (sketch == null || sketch.RelationManager == null) return result;
                var own = new HashSet<string>((sketch.GetSketchSegments() as object[] ?? new object[0]).Cast<SketchSegment>()
                    .Select(s => string.Join(":", (int[])s.GetID())), StringComparer.Ordinal);
                object[] relations = sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll) as object[] ?? new object[0];
                for (int r = 0; r < relations.Length; r++)
                {
                    var relation = (SketchRelation)relations[r];
                    int type = relation.GetRelationType();
                    if (type != (int)swConstraintType_e.swConstraintType_USEEDGE) continue;
                    object[] definition = relation.GetDefinitionEntities2() as object[] ?? new object[0];
                    object[] entities = relation.GetEntities() as object[] ?? new object[0];
                    var driven = entities.OfType<SketchSegment>().FirstOrDefault(s => own.Contains(string.Join(":", (int[])s.GetID())));
                    if (driven == null || driven.GetType() == (int)swSketchSegments_e.swSketchLINE || driven.GetType() == (int)swSketchSegments_e.swSketchARC) continue;
                    for (int k = 0; k < definition.Length; k++)
                    {
                        var edge = definition[k] as Edge;
                        if (edge == null) continue;
                        result.Add(new ConvertedEdge71 { Relation = r, Entity = k, RelationCount = relations.Length, RelationType = type, EdgeSamples = SampleEdge24(edge) });
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                CreateMirrorPartPackage.LogDebug("[CONVERTED71][CAPTURE_IGNORED] reason=" + ex.Message);
                result.Clear();
            }
            if (result.Count > 0) CreateMirrorPartPackage.LogDebug("[CONVERTED71][CAPTURE] nonLinearConvertedSegments=" + result.Count);
            return result;
        }

        // The point move stays the first way to mirror a profile. When it is rejected:
        // attempt 1 repeats it with the converted segments re-attached to the mirrored
        // edges (exact, relation kept); attempt 2, only in the accepted local-deviation
        // class, repeats the plain move and lets an end that slides on a fixed curve
        // stay within that deviation. False: not applicable, or rejected again.
        private static int convertedAttempt71;
        public static bool RetryConvertedProfile71(Action mutate, CutAuditSnapshot21 audit, int attempt)
        {
            if (attempt == 1 && (audit == null || audit.ConvertedEdges71 == null || audit.ConvertedEdges71.Count == 0)) return false;
            if (attempt == 2 && !NativeBodyCoincidence67.SurfaceChainResidualAllowed71) return false;
            convertedAttempt71 = attempt;
            try { mutate(); CreateMirrorPartPackage.LogDebug("[CONVERTED71][RETRY] attempt=" + attempt + " result=PASS"); return true; }
            catch (SketchPointMoveRejected58 ex) { CreateMirrorPartPackage.LogDebug("[CONVERTED71][RETRY] attempt=" + attempt + " result=REJECTED reason=" + ex.Message); return false; }
            finally { convertedAttempt71 = 0; }
        }

        // Geometry-first leaves the relations to the source side suppressed. A spline
        // that is a converted or offset copy of a model edge is the exception:
        // SOLIDWORKS finds its edge on the mirrored body by itself, and a consumer
        // needs the relation alive (natively a Curve Pattern with equal spacing used
        // the whole offset curve while the relation was active, and only the trimmed
        // piece once it was suppressed: 330.5 against 307.0 mm between instances).
        // The relation is re-activated when that leaves the sketch where it is,
        // and suppressed again when anything moves.
        private static List<SketchRelation> suppressedInSource71 = new List<SketchRelation>();

        private static void ReactivateEdgeCopies71(Sketch sketch, List<SketchRelation> suspended)
        {
            var own = new Dictionary<string, SketchSegment>(StringComparer.Ordinal);
            foreach (SketchSegment segment in (sketch.GetSketchSegments() as object[] ?? new object[0]).OfType<SketchSegment>())
                own[string.Join(":", (int[])segment.GetID())] = segment;
            var copies = new List<SketchRelation>();
            // Every suppressed one, not only those of this pass: an earlier, rejected pass suspended them too.
            foreach (SketchRelation relation in (sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll) as object[] ?? new object[0]).OfType<SketchRelation>())
            {
                if (!relation.Suppressed || suppressedInSource71.Any(r => SameProbeObject(r, relation))) continue;
                int type = relation.GetRelationType();
                if (type != (int)swConstraintType_e.swConstraintType_USEEDGE && type != (int)swConstraintType_e.swConstraintType_OFFSETEDGE) continue;
                var driven = (relation.GetEntities() as object[] ?? new object[0]).OfType<SketchSegment>()
                    .FirstOrDefault(s => own.ContainsKey(string.Join(":", (int[])s.GetID())));
                if (driven == null || driven.GetType() == (int)swSketchSegments_e.swSketchLINE || driven.GetType() == (int)swSketchSegments_e.swSketchARC) continue;
                copies.Add(relation);
            }
            if (copies.Count == 0) return;
            Func<List<double[]>> shape = () =>
            {
                var all = new List<double[]>();
                foreach (SketchPoint point in (sketch.GetSketchPoints2() as object[] ?? new object[0]).OfType<SketchPoint>())
                    all.Add(new[] { point.X, point.Y, point.Z });
                foreach (SketchSegment segment in own.Values)
                {
                    var curve = segment.GetCurve() as Curve;
                    double a, b; bool closed, periodic;
                    if (curve == null || !curve.GetEndParams(out a, out b, out closed, out periodic)) continue;
                    for (int i = 0; i <= 8; i++) all.Add(((double[])curve.Evaluate2(a + (b - a) * i / 8.0, 0)).Take(3).ToArray());
                }
                return all;
            };
            List<double[]> before = shape();
            foreach (SketchRelation relation in copies) relation.Suppressed = false;
            List<double[]> after = shape();
            double moved = before.Count != after.Count ? double.PositiveInfinity : Enumerable.Range(0, before.Count).Select(i =>
                Math.Sqrt(Enumerable.Range(0, 3).Sum(k => (before[i][k] - after[i][k]) * (before[i][k] - after[i][k])))).DefaultIfEmpty(0).Max();
            bool kept = moved <= 1e-7 && copies.All(r => !r.Suppressed);
            if (kept) suspended.RemoveAll(r => copies.Any(c => SameProbeObject(c, r)));
            else foreach (SketchRelation relation in copies) relation.Suppressed = true;
            CreateMirrorPartPackage.LogDebug("[CONVERTED71][RELATION_ACTIVE] splineCopies=" + copies.Count + " moved_m=" +
                moved.ToString("E2", System.Globalization.CultureInfo.InvariantCulture) + " result=" + (kept ? "KEPT_ACTIVE" : "SUPPRESSED_AGAIN"));
        }

        // A segment re-attached to the mirrored edge lies on the reflected curve, but
        // SOLIDWORKS decides which of its two end points sits at which end, and the
        // mirror image of a curve runs the other way: the ends come out exchanged
        // (natively a converted spline whose start point sat where its end has to
        // be). The edge holds these two points; they cannot be moved. When each is
        // at the reflected position of the other, the figure has a point at both
        // places it needs one, and the two keep where they are.
        private static void ExchangeEndsOfEdgeCopies71(Sketch sketch, List<SketchRelation> following,
            List<Tuple<SketchPoint, double, double>> targets)
        {
            if (following == null || following.Count == 0) return;
            double limit = NativeBodyCoincidence67.SurfaceChainResidualAllowed71 ? NativeBodyCoincidence67.LocalDeviationLimit71 : 1e-7;
            Func<SketchPoint, string> key = p => { var id = p == null ? null : p.GetID() as int[]; return id == null || id.Length < 2 ? null : id[0] + ":" + id[1]; };
            Func<SketchPoint, Tuple<SketchPoint, double, double>, double> off = (p, t) =>
                Math.Sqrt(Math.Pow(p.X - t.Item2, 2) + Math.Pow(p.Y - t.Item3, 2));
            // Only curves are re-attached (CaptureConvertedEdges71); which relation
            // drives which of them is not readable, so every curve of the sketch
            // is looked at and only an exact exchange counts.
            foreach (SketchSegment segment in (sketch.GetSketchSegments() as object[] ?? new object[0]).OfType<SketchSegment>())
            {
                if (segment.GetType() == (int)swSketchSegments_e.swSketchLINE || segment.GetType() == (int)swSketchSegments_e.swSketchARC) continue;
                var spline = segment as SketchSpline;
                object[] through = spline == null ? null : spline.GetPoints2() as object[];
                SketchPoint a = through != null && through.Length >= 2 ? through[0] as SketchPoint : null;
                SketchPoint b = through != null && through.Length >= 2 ? through[through.Length - 1] as SketchPoint : null;
                string ka = key(a), kb = key(b);
                int ia = ka == null ? -1 : targets.FindIndex(t => key(t.Item1) == ka), ib = kb == null ? -1 : targets.FindIndex(t => key(t.Item1) == kb);
                string id = string.Join(":", (int[])segment.GetID());
                if (ia < 0 || ib < 0 || ia == ib)
                {
                    CreateMirrorPartPackage.LogDebug("[CONVERTED71][ENDS] segment=" + id + " type=" + segment.GetType() + " ends=" + (ka ?? "?") + "," + (kb ?? "?") +
                        " throughPoints=" + (through == null ? -1 : through.Length) + " inTargets=" + (ia >= 0) + "," + (ib >= 0) + " exchanged=False");
                    continue;
                }
                double own = Math.Max(off(a, targets[ia]), off(b, targets[ib])), exchanged = Math.Max(off(a, targets[ib]), off(b, targets[ia]));
                bool keep = own > 1e-7 && exchanged <= limit;
                CreateMirrorPartPackage.LogDebug("[CONVERTED71][ENDS] segment=" + id + " type=" + segment.GetType() + " ends=" + ka + "," + kb +
                    " offOwnTargets_m=" + own.ToString("E2", System.Globalization.CultureInfo.InvariantCulture) +
                    " offEachOthers_m=" + exchanged.ToString("E2", System.Globalization.CultureInfo.InvariantCulture) + " exchanged=" + keep);
                if (!keep) continue;
                if (exchanged > 1e-7) NativeBodyCoincidence67.LargestAcceptedDeviation71 = Math.Max(NativeBodyCoincidence67.LargestAcceptedDeviation71, exchanged);
                targets[ia] = Tuple.Create(targets[ia].Item1, a.X, a.Y);
                targets[ib] = Tuple.Create(targets[ib].Item1, b.X, b.Y);
            }
        }

        // In sketch edit mode. Returns the relations SOLIDWORKS accepted on the
        // mirrored edge; they must stay active. A record that cannot be re-attached
        // is left to the point move.
        private static List<SketchRelation> RebindConvertedEdges71(ModelDoc2 model, Sketch sketch, CutAuditSnapshot21 audit, PlaneData plane)
        {
            var bound = new List<SketchRelation>();
            if (audit == null || audit.ConvertedEdges71 == null || audit.ConvertedEdges71.Count == 0) return bound;
            object[] live = sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll) as object[] ?? new object[0];
            foreach (ConvertedEdge71 item in audit.ConvertedEdges71)
            {
                string outcome;
                try
                {
                    var relation = item.RelationCount == live.Length && item.Relation < live.Length ? live[item.Relation] as SketchRelation : null;
                    if (relation == null || relation.GetRelationType() != item.RelationType) outcome = "RELATION_LIST_CHANGED";
                    else
                    {
                        // The rejected first pass left the relation suspended.
                        if (relation.Suppressed) relation.Suppressed = false;
                        object target = ADDIN.Commands.MirrorV7.MirrorInPlace.RollbackReplayEngineV7.FindEdge(
                            model, item.EdgeSamples.Select(x => ReflectReference30(x, plane)).ToArray());
                        object[] standIns = relation.GetEntities() as object[] ?? new object[0];
                        object[] definition = relation.GetDefinitionEntities2() as object[] ?? new object[0];
                        // The sketch-side stand-in at the position of the edge is what
                        // ReplaceEntity accepts; the edge itself is tried second.
                        bool done = (item.Entity < standIns.Length && standIns[item.Entity] != null && relation.ReplaceEntity(standIns[item.Entity], target)) ||
                            (item.Entity < definition.Length && definition[item.Entity] != null && relation.ReplaceEntity(definition[item.Entity], target));
                        if (done) bound.Add(relation);
                        outcome = done ? "REATTACHED" : "REFUSED";
                    }
                }
                catch (InvalidOperationException ex) { outcome = "NOT_RESOLVED " + ex.Message; }
                CreateMirrorPartPackage.LogDebug("[CONVERTED71][REBIND] relation=" + item.Relation + " type=" + (swConstraintType_e)item.RelationType + " outcome=" + outcome);
            }
            return bound;
        }

        private static object[] DefinitionEntities31(SketchRelation relation)
        {
            var actual = relation.GetDefinitionEntities2() as object[];
            if (actual == null) throw new InvalidOperationException("REFERENCE31: Actual relation definition unavailable.");
            // Some dimensions expose their support only through a solver segment.
            // Keep that segment as an external reference, never as movable geometry.
            var internalEntities = relation.GetEntities() as object[];
            var result = (object[])actual.Clone();
            for (int i = 0; i < result.Length; i++)
            {
                if (result[i] != null) continue;
                if (internalEntities == null || internalEntities.Length != result.Length || internalEntities[i] == null)
                    throw new InvalidOperationException("REFERENCE31: Unresolved null relation entity at index " + i);
                result[i] = internalEntities[i];
            }
            return result;
        }

        private static string RelationKey30(SketchRelation relation, Sketch owner)
        {
            var display = relation.GetDisplayDimension() as DisplayDimension;
            var dim = display == null ? null : display.GetDimension() as Dimension;
            var parts = new List<string> { relation.GetRelationType().ToString(), dim == null ? "" : dim.FullName };
            foreach (object entity in DefinitionEntities31(relation))
            {
                var point = entity as SketchPoint;
                var segment = entity as SketchSegment;
                if (point != null && IsOwnedEntity31(owner, point))
                    parts.Add("P:" + string.Join(",", (int[])point.GetID()));
                else if (segment != null && IsOwnedEntity31(owner, segment))
                    parts.Add("S:" + string.Join(",", (int[])segment.GetID()));
                else parts.Add(entity == null ? "implicit" : "external");
            }
            return string.Join("|", parts);
        }

        private static List<RelationReference30> CaptureReferences30(ModelDoc2 model, Sketch sketch)
        {
            var result = new List<RelationReference30>();
            var relations = (sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll)
                as object[] ?? new object[0]).Cast<SketchRelation>().ToArray();
            foreach (var relation in relations)
            {
                var entities = DefinitionEntities31(relation);
                if (entities == null) throw new InvalidOperationException("REFERENCE30: Actual relation definition unavailable.");
                string key = RelationKey30(relation, sketch);
                var display31 = relation.GetDisplayDimension() as DisplayDimension;
                if (display31 != null)
                    CreateMirrorPartPackage.LogDebug("[REFERENCE31][DIMENSION_COVERAGE] relation=" + key +
                        " owned=" + entities.Count(e => IsOwnedEntity31(sketch, e)) +
                        " external=" + entities.Count(e => !IsOwnedEntity31(sketch, e)));
                for (int index = 0; index < entities.Length; index++)
                {
                    object entity = entities[index];
                    if (entity == null) continue; // implicit entity, not a movable proxy
                    var point = entity as SketchPoint;
                    var segment = entity as SketchSegment;
                    if (IsOwnedEntity31(sketch, entity)) continue;
                    Func<ModelDoc2, PlaneData, object> resolve;
                    Func<ModelDoc2, object, object, bool> verify = null;
                    Func<PlaneData, double[]> pickPoint31 = null;
                    Func<PlaneData, double[][]> geometry31 = null;
                    string kind31 = null;
                    var edge = entity as Edge;
                    var face = entity as Face2;
                    var vertex = entity as Vertex;
                    if (edge != null)
                    {
                        kind31 = "Edge";
                        double[][] samples = SampleEdge24(edge);
                        resolve = (m, p) => ADDIN.Commands.MirrorV7.MirrorInPlace.RollbackReplayEngineV7.FindEdge(
                            m, samples.Select(x => ReflectReference30(x, p)).ToArray());
                    }
                    else if (face != null)
                    {
                        kind31 = "Face";
                        var support = CaptureFaceSupport24(face);
                        resolve = (m, p) => ADDIN.Commands.MirrorV7.MirrorInPlace.RollbackReplayEngineV7.FindFace(
                            m, support.Area, support.Boundaries.Select(b => b.Select(x => ReflectReference30(x, p)).ToArray()).ToArray());
                    }
                    else if (vertex != null)
                    {
                        kind31 = "Vertex";
                        double[] position = (double[])vertex.GetPoint();
                        resolve = (m, p) => ADDIN.Commands.MirrorV7.MirrorInPlace.RollbackReplayEngineV7.FindVertex(m, ReflectReference30(position, p));
                    }
                    else if (point != null)
                    {
                        kind31 = "SketchPoint";
                        double[] sourceWorld50 = SketchPointWorld50(point);
                        byte[] persistent50 = null;
                        try { persistent50 = model.Extension.GetPersistReference3(point) as byte[]; } catch { }
                        resolve = (m, p) =>
                        {
                            double[] expected50 = ReflectReference30(sourceWorld50, p);
                            if (persistent50 != null && persistent50.Length > 0)
                            {
                                try
                                {
                                    int state50;
                                    var persisted50 = m.Extension.GetObjectByPersistReference3(persistent50, out state50) as SketchPoint;
                                    double[] persistedWorld50 = SketchPointWorld50(persisted50);
                                    if (persistedWorld50 != null && Math.Sqrt(Enumerable.Range(0, 3)
                                        .Sum(axis50 => Math.Pow(persistedWorld50[axis50] - expected50[axis50], 2))) <= 1e-7)
                                    {
                                        CreateMirrorPartPackage.LogDebug("[REFERENCE50][SKETCH_POINT_RESOLVE] mode=Persistent state=" + state50);
                                        return persisted50;
                                    }
                                }
                                catch { }
                            }
                            return FindReflectedSketchPoint50(m, sourceWorld50, p);
                        };
                    }
                    else if (segment is SketchLine)
                    {
                        var line = (SketchLine)segment;
                        var math = SwAddin.InstanceSwApp.GetMathUtility() as IMathUtility;
                        var transform = segment.GetSketch().ModelToSketchTransform.IInverse();
                        Func<SketchPoint, double[]> world = p => (double[])((MathPoint)((MathPoint)math.CreatePoint(
                            new[] { p.X, p.Y, p.Z })).MultiplyTransform(transform)).ArrayData;
                        double[] a = world((SketchPoint)line.GetStartPoint2());
                        double[] b = world((SketchPoint)line.GetEndPoint2());
                        double[][] samples = Enumerable.Range(0, 9).Select(i => Enumerable.Range(0, 3)
                            .Select(j => a[j] + (b[j] - a[j]) * i / 8.0).ToArray()).ToArray();
                        try
                        {
                            ADDIN.Commands.MirrorV7.MirrorInPlace.RollbackReplayEngineV7.FindEdge(model, samples);
                            kind31 = "ProxyLineToEdge";
                            resolve = (m, p) => ADDIN.Commands.MirrorV7.MirrorInPlace.RollbackReplayEngineV7.FindEdge(
                                m, samples.Select(x => ReflectReference30(x, p)).ToArray());
                        }
                        catch (InvalidOperationException)
                        {
                            Face2 carrier = FindCarrierFace31(model, line);
                            var support = CaptureFaceSupport24(carrier);
                            double[][] reflectedSamples31 = null;
                            kind31 = "ProxyLineToFace";
                            pickPoint31 = p => ReflectReference30(samples[samples.Length / 2], p);
                            geometry31 = p => new[]
                            {
                                ReflectReference30(samples[0], p),
                                ReflectReference30(samples[samples.Length - 1], p)
                            };
                            resolve = (m, p) =>
                            {
                                reflectedSamples31 = samples.Select(x => ReflectReference30(x, p)).ToArray();
                                return ADDIN.Commands.MirrorV7.MirrorInPlace.RollbackReplayEngineV7.FindFace(
                                    m, support.Area, support.Boundaries.Select(boundary => boundary.Select(x => ReflectReference30(x, p)).ToArray()).ToArray());
                            };
                            verify = (m, actual, target) =>
                            {
                                try
                                {
                                    if (actual is Face2 && SwAddin.InstanceSwApp.IsSame(actual, target) == (int)swObjectEquality.swObjectSame)
                                        return true;
                                    var actualLine = actual as SketchLine;
                                    if (actualLine == null || reflectedSamples31 == null) return false;
                                    var actualOwner = ((SketchSegment)actualLine).GetSketch();
                                    var actualTransform = actualOwner.ModelToSketchTransform.IInverse();
                                    Func<SketchPoint, double[]> actualWorld = sketchPoint31 => (double[])((MathPoint)((MathPoint)math.CreatePoint(
                                        new[] { sketchPoint31.X, sketchPoint31.Y, sketchPoint31.Z })).MultiplyTransform(actualTransform)).ArrayData;
                                    double[][] actualEndpoints = new[]
                                    {
                                        actualWorld((SketchPoint)actualLine.GetStartPoint2()),
                                        actualWorld((SketchPoint)actualLine.GetEndPoint2())
                                    };
                                    double[][] expectedEndpoints = new[]
                                    {
                                        reflectedSamples31[0],
                                        reflectedSamples31[reflectedSamples31.Length - 1]
                                    };
                                    double residual = GeometryResidual31(actualEndpoints, expectedEndpoints);
                                    CreateMirrorPartPackage.LogDebug("[REFERENCE36][PROXY_GEOMETRY] residual_m=" + residual.ToString("R") +
                                        " carrierSame=" + (SwAddin.InstanceSwApp.IsSame(FindCarrierFace31(m, actualLine), target) == (int)swObjectEquality.swObjectSame));
                                    return residual <= 1e-7 &&
                                        SwAddin.InstanceSwApp.IsSame(FindCarrierFace31(m, actualLine), target) == (int)swObjectEquality.swObjectSame;
                                }
                                catch { return false; }
                            };
                        }
                        CreateMirrorPartPackage.LogDebug("[REFERENCE35][PROXY_SOURCE_VERIFIED] relation=" + key + " kind=" + kind31);
                    }
                    else
                    {
                        string description = DiagnosticEntity(entity);
                        kind31 = "Unsupported";
                        CreateMirrorPartPackage.LogDebug("[REFERENCE47][UNSUPPORTED_REFERENCE] relation=" + key +
                            " entity=" + index + " description=" + description);
                        // Never move an external sketch or a solver proxy to make a dimension fit.
                        resolve = (m, p) => { throw new InvalidOperationException("REFERENCE30: Unsupported actual reference: " + description); };
                    }
                    if (verify == null) verify = (m, actual, target) =>
                    {
                        try { return SwAddin.InstanceSwApp.IsSame(actual, target) == (int)swObjectEquality.swObjectSame; }
                        catch { return false; }
                    };
                    result.Add(new RelationReference30 { Key = key, SourceRelation31 = relation, EntityIndex = index,
                        EntityCount = entities.Length, Resolve = resolve, Verify31 = verify,
                        PickPoint31 = pickPoint31, Geometry31 = geometry31, Kind31 = kind31 });
                    CreateMirrorPartPackage.LogDebug("[REFERENCE30][CAPTURE] relation=" + key + " entity=" + index + " kind=" +
                        (kind31 ?? "Unsupported"));
                }
            }
            return result;
        }

        private static bool RelationEntityMatchesKind49(object entity, string kind)
        {
            if (string.Equals(kind, "Edge", StringComparison.Ordinal)) return entity is Edge;
            if (string.Equals(kind, "Face", StringComparison.Ordinal)) return entity is Face2;
            if (string.Equals(kind, "Vertex", StringComparison.Ordinal)) return entity is Vertex;
            if (string.Equals(kind, "SketchPoint", StringComparison.Ordinal)) return entity is SketchPoint;
            // A solver proxy is captured as a SketchLine. Replacing that shared proxy in
            // one relation can update the entity exposed by every relation that uses it.
            // Therefore later snapshots must recognize both the pre-rebind proxy and its
            // already-resolved topological target. This is not a feature-name special case;
            // it follows the target type encoded by the captured reference kind.
            if (string.Equals(kind, "ProxyLineToFace", StringComparison.Ordinal))
                return entity is SketchLine || entity is Face2;
            if (string.Equals(kind, "ProxyLineToEdge", StringComparison.Ordinal))
                return entity is SketchLine || entity is Edge;
            if (string.Equals(kind, "Unsupported", StringComparison.Ordinal)) return true;
            return false;
        }

        private static void RebindReferences30(ModelDoc2 model, Sketch sketch, CutAuditSnapshot21 audit, PlaneData plane, bool verifyOnly = false)
        {
            if (audit == null) return; // Base sketch has its separate mutation path.
            if (audit.References30 == null) throw new InvalidOperationException("REFERENCE30: Source references were not captured.");
            // Resolve every target first. No partial replacement on a missing/ambiguous target.
            var resolved = audit.References30.Select(r => Tuple.Create(r, r.Resolve(model, plane))).ToList();
            foreach (var pair in resolved)
            {
                var snapshot = pair.Item1;
                var liveRelations49 = (sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll)
                    as object[] ?? new object[0]).Cast<SketchRelation>().ToArray();
                var identityMatches49 = liveRelations49.Where(r => SameProbeObject(r, snapshot.SourceRelation31)).ToArray();
                string identityMethod49;
                SketchRelation[] matches;
                if (identityMatches49.Length == 1)
                {
                    matches = identityMatches49;
                    identityMethod49 = "ComIdentity";
                }
                else
                {
                    matches = liveRelations49.Where(r => RelationKey30(r, sketch) == snapshot.Key).ToArray();
                    identityMethod49 = "StructuralKey";
                    if (matches.Length > 1)
                    {
                        matches = matches.Where(candidate49 =>
                        {
                            try
                            {
                                object[] entities49 = DefinitionEntities31(candidate49);
                                return entities49 != null && entities49.Length == snapshot.EntityCount &&
                                    snapshot.EntityIndex >= 0 && snapshot.EntityIndex < entities49.Length &&
                                    RelationEntityMatchesKind49(entities49[snapshot.EntityIndex], snapshot.Kind31);
                            }
                            catch { return false; }
                        }).ToArray();
                        identityMethod49 = "StructuralKey+EntityKind";
                    }
                }
                if (matches.Length != 1) throw new InvalidOperationException("REFERENCE30: Live relation identity is not unique: " + snapshot.Key);
                var relation = matches[0];
                CreateMirrorPartPackage.LogDebug("[REFERENCE49][RELATION_IDENTITY] relation=" + snapshot.Key +
                    " method=" + identityMethod49 + " matches=" + matches.Length + " kind=" + snapshot.Kind31);
                if (RelationKey30(relation, sketch) != snapshot.Key)
                    throw new InvalidOperationException("REFERENCE31: Relation structure changed: " + snapshot.Key);
                var entities = DefinitionEntities31(relation);
                if (entities == null || entities.Length != snapshot.EntityCount || pair.Item2 == null)
                    throw new InvalidOperationException("REFERENCE30: Relation definition changed.");
                object oldEntity = entities[snapshot.EntityIndex];
                bool invariant = snapshot.Verify31(model, oldEntity, pair.Item2);
                if (verifyOnly && !invariant)
                    throw new InvalidOperationException("REFERENCE30: Reference changed after solver restoration: " + snapshot.Key);
                if (!invariant && !relation.ReplaceEntity(oldEntity, pair.Item2))
                    throw new InvalidOperationException("REFERENCE30: ReplaceEntity rejected: " + snapshot.Key);
                var readback = DefinitionEntities31(relation);
                if (readback == null || readback.Length != entities.Length || !snapshot.Verify31(model, readback[snapshot.EntityIndex], pair.Item2))
                    throw new InvalidOperationException("REFERENCE30: Reference readback mismatch: " + snapshot.Key);
                CreateMirrorPartPackage.LogDebug("[REFERENCE35][BOUND] relation=" + snapshot.Key + " kind=" + snapshot.Kind31 +
                    " invariant=" + invariant + " verified=True");
            }
        }

        private static bool HasReflectedProxyDimension37(CutAuditSnapshot21 audit, string dimensionFullName)
        {
            return audit != null && audit.References30 != null &&
                audit.References30.Any(reference => reference.Kind31 == "ProxyLineToFace" &&
                    reference.Key != null && reference.Key.IndexOf("|" + dimensionFullName + "|", StringComparison.Ordinal) >= 0);
        }

        private static Dimension RecreateReflectedProxyDimension38(ModelDoc2 model, Sketch sketch,
            CutAuditSnapshot21 audit, PlaneData plane, string oldFullName, double systemValue)
        {
            var snapshot = audit.References30.Single(reference => reference.Kind31 == "ProxyLineToFace" &&
                reference.Key.IndexOf("|" + oldFullName + "|", StringComparison.Ordinal) >= 0);
            var relations = (sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll)
                as object[] ?? new object[0]).Cast<SketchRelation>().ToArray();
            var matches = relations.Where(candidateRelation => RelationKey30(candidateRelation, sketch) == snapshot.Key).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("REFERENCE38: Proxy dimension relation is not unique: " + oldFullName);
            var relation = matches[0];
            var entities = DefinitionEntities31(relation);
            object owned = entities.SingleOrDefault(entity => entity != null && IsOwnedEntity31(sketch, entity));
            if (owned == null) throw new InvalidOperationException("REFERENCE38: Owned dimension entity was not found: " + oldFullName);
            var ownedSegmentBeforeDelete = owned as SketchSegment;
            var ownedPointBeforeDelete = owned as SketchPoint;
            int[] ownedId38 = ownedSegmentBeforeDelete != null
                ? ownedSegmentBeforeDelete.GetID() as int[]
                : ownedPointBeforeDelete == null ? null : ownedPointBeforeDelete.GetID() as int[];
            int ownedSegmentType38 = ownedSegmentBeforeDelete == null ? -1 : ownedSegmentBeforeDelete.GetType();
            double[][] ownedGeometry38 = ownedSegmentBeforeDelete == null ? null : SegmentPoints31(ownedSegmentBeforeDelete);
            double[] ownedPointGeometry38 = ownedPointBeforeDelete == null ? null :
                new[] { ownedPointBeforeDelete.X, ownedPointBeforeDelete.Y, ownedPointBeforeDelete.Z };
            Func<object> reacquireOwned38 = () =>
            {
                if (ownedSegmentBeforeDelete != null)
                {
                    var candidates = (sketch.GetSketchSegments() as object[] ?? new object[0]).Cast<SketchSegment>()
                        .Where(candidate => candidate.GetType() == ownedSegmentType38).ToArray();
                    var byId = candidates.Where(candidate =>
                    {
                        int[] id = candidate.GetID() as int[];
                        return id != null && ownedId38 != null && id.SequenceEqual(ownedId38);
                    }).ToArray();
                    if (byId.Length == 1) return byId[0];
                    var byGeometry = candidates.Where(candidate =>
                        GeometryResidual31(ownedGeometry38, SegmentPoints31(candidate)) <= 1e-9).ToArray();
                    if (byGeometry.Length == 1) return byGeometry[0];
                }
                else if (ownedPointBeforeDelete != null)
                {
                    var candidates = EnumerateSketchPoints28(sketch, false).Cast<SketchPoint>().Where(candidate =>
                    {
                        int[] id = candidate.GetID() as int[];
                        return id != null && ownedId38 != null && id.SequenceEqual(ownedId38);
                    }).ToArray();
                    if (candidates.Length == 1) return candidates[0];
                    var byGeometry = EnumerateSketchPoints28(sketch, false).Cast<SketchPoint>().Where(candidate =>
                        Math.Sqrt(Math.Pow(candidate.X - ownedPointGeometry38[0], 2) +
                            Math.Pow(candidate.Y - ownedPointGeometry38[1], 2) +
                            Math.Pow(candidate.Z - ownedPointGeometry38[2], 2)) <= 1e-9).ToArray();
                    if (byGeometry.Length == 1) return byGeometry[0];
                }
                throw new InvalidOperationException("REFERENCE40: Cannot reacquire owned dimension entity: " + oldFullName);
            };
            object target = snapshot.Resolve(model, plane);
            var targetFace = target as Face2;
            double[] pick = snapshot.PickPoint31 == null ? null : snapshot.PickPoint31(plane);
            if (targetFace == null || pick == null || pick.Length < 3)
                throw new InvalidOperationException("REFERENCE38: Target face or pick point is unavailable: " + oldFullName);

            double[] textPosition = null;
            try
            {
                var display = relation.GetDisplayDimension() as DisplayDimension;
                var annotation = display == null ? null : display.GetAnnotation() as Annotation;
                textPosition = annotation == null ? null : annotation.GetPosition() as double[];
            }
            catch { }
            if (textPosition == null || textPosition.Length < 3) textPosition = pick.ToArray();
            else textPosition = ReflectReference30(textPosition, plane);

            if (!sketch.RelationManager.DeleteRelation(relation))
                throw new InvalidOperationException("REFERENCE38: DeleteRelation rejected: " + oldFullName);
            owned = reacquireOwned38();
            CreateMirrorPartPackage.LogDebug("[REFERENCE40][OWNED_REACQUIRED] dimension=" + oldFullName +
                " id=" + (ownedId38 == null ? "unavailable" : string.Join(",", ownedId38)));
            Func<object, bool, string, bool> selectSketchEntity40 = (value, append, label) =>
            {
                bool selected = false;
                string method = "none";
                SelectData selectionData40 = null;
                try
                {
                    var selectionManager40 = model.SelectionManager as SelectionMgr;
                    selectionData40 = selectionManager40 == null ? null : selectionManager40.CreateSelectData();
                }
                catch { selectionData40 = null; }
                try
                {
                    var entity = value as Entity;
                    if (entity != null) { selected = entity.Select4(append, selectionData40); method = "IEntity.Select4(SelectData)"; }
                }
                catch { selected = false; }
                if (!selected)
                {
                    try { selected = (bool)((dynamic)value).Select4(append, selectionData40); method = "dynamic.Select4(SelectData)"; }
                    catch { selected = false; }
                }
                if (!selected)
                {
                    var segment40 = value as SketchSegment;
                    var points40 = segment40 == null ? null : SegmentPoints31(segment40);
                    if (points40 != null && points40.Length >= 2)
                    {
                        var math40 = SwAddin.InstanceSwApp.GetMathUtility() as IMathUtility;
                        var sketchToModel40 = sketch.ModelToSketchTransform.IInverse();
                        double[] middle40 = Enumerable.Range(0, 3).Select(index =>
                            (points40[0][index] + points40[points40.Length - 1][index]) * 0.5).ToArray();
                        double[] world40 = (double[])((MathPoint)((MathPoint)math40.CreatePoint(middle40))
                            .MultiplyTransform(sketchToModel40)).ArrayData;
                        selected = model.Extension.SelectByID2(string.Empty, "SKETCHSEGMENT",
                            world40[0], world40[1], world40[2], append, 0, null, 0);
                        method = "SelectByID2(SKETCHSEGMENT)";
                    }
                }
                CreateMirrorPartPackage.LogDebug("[REFERENCE40][SELECT] label=" + label + " selected=" + selected +
                    " method=" + method + " activeSketch=" + (model.SketchManager.ActiveSketch != null));
                return selected;
            };

            model.ClearSelection2(true);
            bool selectedOwned = false;
            bool selectedFace = false;
            SketchLine localCarrier42 = null;
            string faceSelectionMethod = "LocalConstructionCarrierRequired";
            // A face can report SelectByID2=True while AddDimension2 still rejects the
            // line/face pair in an active 2D sketch. That API behaviour is intermittent
            // and leaves the dimension transaction non-deterministic. ProxyLineToFace
            // already carries the exact reflected linear datum, so always materialise it
            // as a fixed construction line and create a point-pair linear dimension.
            // This keeps the complete operation inside sketch space and works for every
            // planar face orientation instead of depending on UI face selection state.
            if (!selectedFace)
            {
                double[][] reflectedGeometry = snapshot.Geometry31 == null ? null : snapshot.Geometry31(plane);
                if (reflectedGeometry == null || reflectedGeometry.Length != 2)
                    throw new InvalidOperationException("REFERENCE39: Reflected carrier geometry is unavailable: " + oldFullName);
                var math = SwAddin.InstanceSwApp.GetMathUtility() as IMathUtility;
                var worldToSketch = sketch.ModelToSketchTransform;
                Func<double[], double[]> toSketch = world => (double[])((MathPoint)((MathPoint)math.CreatePoint(world))
                    .MultiplyTransform(worldToSketch)).ArrayData;
                double[] carrierStart = toSketch(reflectedGeometry[0]);
                double[] carrierEnd = toSketch(reflectedGeometry[1]);
                model.ClearSelection2(true);
                var sketchManager44 = model.SketchManager;
                bool previousAddToDb44 = sketchManager44.AddToDB;
                SketchLine carrier = null;
                try
                {
                    // AddToDB suppresses SOLIDWORKS inferencing while the temporary datum
                    // is created. Without it, a carrier only 0.1 mm from the profile is
                    // automatically snapped/coincident to that profile and its distance
                    // dimension becomes zero.
                    sketchManager44.AddToDB = true;
                    carrier = sketchManager44.CreateLine(carrierStart[0], carrierStart[1], carrierStart[2],
                        carrierEnd[0], carrierEnd[1], carrierEnd[2]) as SketchLine;
                    if (carrier != null) ((SketchSegment)carrier).ConstructionGeometry = true;
                }
                finally
                {
                    try { sketchManager44.AddToDB = previousAddToDb44; } catch { }
                }
                if (carrier == null) throw new InvalidOperationException("REFERENCE39: CreateLine returned null: " + oldFullName);
                int[] carrierId41 = ((SketchSegment)carrier).GetID() as int[];
                double[][] carrierGeometry41 = SegmentPoints31((SketchSegment)carrier);
                double carrierCreationResidual44 = GeometryResidual31(
                    new[] { carrierStart, carrierEnd }, carrierGeometry41);
                CreateMirrorPartPackage.LogDebug("[REFERENCE44][CARRIER_CREATED] actualStart=" +
                    string.Join(",", carrierGeometry41[0].Select(value => value.ToString("R"))) + " actualEnd=" +
                    string.Join(",", carrierGeometry41[carrierGeometry41.Length - 1].Select(value => value.ToString("R"))) +
                    " residual=" + carrierCreationResidual44.ToString("R") + " addToDB=True");
                if (carrierCreationResidual44 > 1.0e-9)
                    throw new InvalidOperationException("REFERENCE44: Construction carrier was altered during creation; residual=" +
                        carrierCreationResidual44.ToString("R"));
                Func<SketchLine> reacquireCarrier41 = () =>
                {
                    var candidates = (sketch.GetSketchSegments() as object[] ?? new object[0]).Cast<SketchSegment>()
                        .OfType<SketchLine>().Where(candidate => ((SketchSegment)candidate).ConstructionGeometry).ToArray();
                    var byId = candidates.Where(candidate =>
                    {
                        int[] id = ((SketchSegment)candidate).GetID() as int[];
                        return id != null && carrierId41 != null && id.SequenceEqual(carrierId41);
                    }).ToArray();
                    if (byId.Length == 1) return byId[0];
                    var byGeometry = candidates.Where(candidate =>
                        GeometryResidual31(carrierGeometry41, SegmentPoints31((SketchSegment)candidate)) <= 1e-9).ToArray();
                    if (byGeometry.Length == 1) return byGeometry[0];
                    throw new InvalidOperationException("REFERENCE41: Cannot reacquire local carrier: " + oldFullName);
                };
                if (!selectSketchEntity40(carrier, false, "carrier-for-fix"))
                    throw new InvalidOperationException("REFERENCE39: Cannot select local carrier for fixing: " + oldFullName);
                model.SketchAddConstraints("sgFIXED");
                model.ClearSelection2(true);
                owned = reacquireOwned38();
                carrier = reacquireCarrier41();
                localCarrier42 = carrier;
                CreateMirrorPartPackage.LogDebug("[REFERENCE41][CARRIER_REACQUIRED] id=" +
                    (carrierId41 == null ? "unavailable" : string.Join(",", carrierId41)));
                bool selectedCarrier = selectSketchEntity40(carrier, false, "carrier-for-dimension");
                selectedOwned = selectSketchEntity40(owned, true, "owned-after-carrier");
                selectedFace = selectedOwned && selectedCarrier;
                faceSelectionMethod = "LocalConstructionCarrier";
                CreateMirrorPartPackage.LogDebug("[REFERENCE39][LOCAL_CARRIER] start=" +
                    string.Join(",", carrierStart.Select(value => value.ToString("R"))) + " end=" +
                    string.Join(",", carrierEnd.Select(value => value.ToString("R"))) +
                    " selected=" + selectedFace);
            }
            CreateMirrorPartPackage.LogDebug("[REFERENCE38][TARGET_SELECTION] owned=" + selectedOwned +
                " face=" + selectedFace + " method=" + faceSelectionMethod);
            if (!selectedOwned || !selectedFace)
                throw new InvalidOperationException("REFERENCE38: Target selection failed: owned=" + selectedOwned + " face=" + selectedFace);
            Func<Func<DisplayDimension>, DisplayDimension> createDimensionWithoutPrompt46 = create46 =>
            {
                var app46 = SwAddin.InstanceSwApp;
                bool previousInput46 = false;
                bool capturedInput46 = false;
                try
                {
                    previousInput46 = app46.GetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e.swInputDimValOnCreate);
                    capturedInput46 = true;
                    app46.SetUserPreferenceToggle(
                        (int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
                    CreateMirrorPartPackage.LogDebug("[REFERENCE46][DIMENSION_PROMPT] suppressed=True previous=" + previousInput46);
                    return create46();
                }
                finally
                {
                    if (capturedInput46)
                    {
                        try
                        {
                            app46.SetUserPreferenceToggle(
                                (int)swUserPreferenceToggle_e.swInputDimValOnCreate, previousInput46);
                            CreateMirrorPartPackage.LogDebug("[REFERENCE46][DIMENSION_PROMPT] restored=" + previousInput46);
                        }
                        catch { }
                    }
                }
            };
            DisplayDimension replacementDisplay;
            if (localCarrier42 != null && owned is SketchLine)
            {
                double[][] ownedGeometry42 = SegmentPoints31((SketchSegment)owned);
                double[][] carrierGeometry42 = SegmentPoints31((SketchSegment)localCarrier42);
                double ownedDx42 = ownedGeometry42[ownedGeometry42.Length - 1][0] - ownedGeometry42[0][0];
                double ownedDy42 = ownedGeometry42[ownedGeometry42.Length - 1][1] - ownedGeometry42[0][1];
                double carrierDx42 = carrierGeometry42[carrierGeometry42.Length - 1][0] - carrierGeometry42[0][0];
                double carrierDy42 = carrierGeometry42[carrierGeometry42.Length - 1][1] - carrierGeometry42[0][1];
                double ownedLength42 = Math.Sqrt(ownedDx42 * ownedDx42 + ownedDy42 * ownedDy42);
                double carrierLength42 = Math.Sqrt(carrierDx42 * carrierDx42 + carrierDy42 * carrierDy42);
                if (ownedLength42 <= 1.0e-12 || carrierLength42 <= 1.0e-12)
                    throw new InvalidOperationException("REFERENCE42: Cannot dimension a zero-length line.");

                double normalizedCross42 = Math.Abs(ownedDx42 * carrierDy42 - ownedDy42 * carrierDx42) /
                    (ownedLength42 * carrierLength42);
                if (normalizedCross42 > 1.0e-5)
                    throw new InvalidOperationException("REFERENCE42: Owned line and reflected carrier are not parallel; cross=" +
                        normalizedCross42.ToString("R"));

                // The required linear dimension lies along the normal of the parallel lines.
                // Do not infer it from segment midpoints: unequal line lengths shift their midpoints
                // along the tangent and can incorrectly turn a linear constraint into an angle.
                double normalX42 = -ownedDy42 / ownedLength42;
                double normalY42 = ownedDx42 / ownedLength42;
                bool horizontalDistance42 = Math.Abs(normalX42) >= Math.Abs(normalY42);

                // Selecting two SketchLine objects makes SOLIDWORKS create an angular
                // dimension (0 or PI), even when AddHorizontal/VerticalDimension2 is used.
                // Select endpoints instead so the API creates the intended signed linear
                // coordinate constraint. The fixed construction carrier remains the datum.
                var ownedPoint43 = ((SketchLine)owned).GetStartPoint2() as SketchPoint;
                var carrierPoint43 = localCarrier42.GetStartPoint2() as SketchPoint;
                if (ownedPoint43 == null || carrierPoint43 == null)
                    throw new InvalidOperationException("REFERENCE43: Cannot resolve line endpoints for linear dimension.");
                model.ClearSelection2(true);
                bool selectedCarrierPoint43 = selectSketchEntity40(carrierPoint43, false, "carrier-point-for-dimension");
                bool selectedOwnedPoint43 = selectSketchEntity40(ownedPoint43, true, "owned-point-for-dimension");
                if (!selectedCarrierPoint43 || !selectedOwnedPoint43)
                    throw new InvalidOperationException("REFERENCE43: Endpoint selection failed: owned=" +
                        selectedOwnedPoint43 + " carrier=" + selectedCarrierPoint43);
                replacementDisplay = createDimensionWithoutPrompt46(() => horizontalDistance42
                    ? model.AddHorizontalDimension2(textPosition[0], textPosition[1], textPosition[2]) as DisplayDimension
                    : model.AddVerticalDimension2(textPosition[0], textPosition[1], textPosition[2]) as DisplayDimension);
                CreateMirrorPartPackage.LogDebug("[REFERENCE43][POINT_PAIR] owned=" +
                    ownedPoint43.X.ToString("R") + "," + ownedPoint43.Y.ToString("R") + " carrier=" +
                    carrierPoint43.X.ToString("R") + "," + carrierPoint43.Y.ToString("R") +
                    " selected=True");
                CreateMirrorPartPackage.LogDebug("[REFERENCE42][LINEAR_DIMENSION] orientation=" +
                    (horizontalDistance42 ? "Horizontal" : "Vertical") + " tangent=" +
                    ownedDx42.ToString("R") + "," + ownedDy42.ToString("R") + " normal=" +
                    normalX42.ToString("R") + "," + normalY42.ToString("R") + " parallelCross=" +
                    normalizedCross42.ToString("R"));
            }
            else replacementDisplay = createDimensionWithoutPrompt46(() =>
                model.AddDimension2(textPosition[0], textPosition[1], textPosition[2]) as DisplayDimension);
            var replacement = replacementDisplay == null ? null : replacementDisplay.GetDimension2(0) as Dimension;
            if (replacement == null) throw new InvalidOperationException("REFERENCE38: AddDimension2 returned null: " + oldFullName);
            replacement.DrivenState = (int)swDimensionDrivenState_e.swDimensionDriving;
            replacement.SystemValue = systemValue;
            CreateMirrorPartPackage.LogDebug("[REFERENCE38][DIMENSION_RECREATED] old=" + oldFullName +
                " new=" + replacement.FullName + " value=" + replacement.SystemValue.ToString("R") +
                " pick=" + string.Join(",", pick.Select(value => value.ToString("R"))));
            model.ClearSelection2(true);
            return replacement;
        }

        // Owned points of a sketch (free points, segment ends, arc centres,
        // spline points), without logging.
        internal static List<SketchPoint> OwnedSketchPoints71(Sketch sketch)
        {
            return EnumerateSketchPoints28(sketch, false).Cast<SketchPoint>().ToList();
        }

        private static object[] EnumerateSketchPoints28(Sketch sketch, bool log = true)
        {
            var result = new List<SketchPoint>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            Action<object> add = value =>
            {
                var point = value as SketchPoint;
                if (point == null) return;
                if (!SameProbeObject(point.GetSketch(), sketch)) return;
                var id = point.GetID() as int[];
                if (id == null || id.Length < 2)
                    throw new InvalidOperationException("SKETCH28: Point identity unavailable.");
                string key = id[0] + ":" + id[1];
                if (ids.Add(key)) result.Add(point);
            };
            Action<SketchSegment> addSegment = segment =>
            {
                var line = segment as SketchLine;
                if (line != null) { add(line.GetStartPoint2()); add(line.GetEndPoint2()); return; }
                var arc = segment as SketchArc;
                if (arc != null) { add(arc.GetStartPoint2()); add(arc.GetEndPoint2()); add(arc.GetCenterPoint2()); return; }
                var spline = segment as SketchSpline;
                if (spline != null)
                    foreach (object value in spline.GetPoints2() as object[] ?? new object[0]) add(value);
            };
            foreach (object value in sketch.GetSketchPoints2() as object[] ?? new object[0]) add(value);
            foreach (SketchSegment segment in sketch.GetSketchSegments() as object[] ?? new object[0]) addSegment(segment);
            // GetEntities includes solver proxy geometry, NOT owned sketch geometry.
            if (log) CreateMirrorPartPackage.LogDebug("[REFERENCE30][OWNED_POINTS] count=" + result.Count +
                " relationProxiesIncluded=False");
            return result.Cast<object>().ToArray();
        }

        /// <summary>
        /// Mở Sketch và Xóa toàn bộ Ràng buộc (Relations) + Kích thước (Dimensions)
        /// Trả về đối tượng Sketch để tiếp tục thực hiện Bước 2 (Dịch chuyển Điểm)
        /// </summary>
        private static string DiagnosticEntity(object entity)
        {
            if (entity == null) return "null";
            var point = entity as SketchPoint;
            if (point != null)
            {
                int[] id = point.GetID() as int[];
                return "Point id=" + (id == null ? "unavailable" : string.Join(",", id)) +
                    " xyz_m=" + point.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                    point.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                    point.Z.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            }
            var segment = entity as SketchSegment;
            if (segment != null)
            {
                int[] id = segment.GetID() as int[];
                string result = "Segment type=" + segment.GetType() + " id=" +
                    (id == null ? "unavailable" : string.Join(",", id));
                var line = entity as SketchLine;
                if (line != null) result += " start={" + DiagnosticEntity(line.GetStartPoint2()) +
                    "} end={" + DiagnosticEntity(line.GetEndPoint2()) + "}";
                return result;
            }
            return entity.GetType().FullName;
        }

        private static void LogConstraintSnapshot(Feature feature, Sketch sketch)
        {
            CreateMirrorPartPackage.LogDebug("[SKETCHDIAG14][BEGIN] sketch=" + feature.Name);
            try
            {
                var frame = sketch.ModelToSketchTransform.ArrayData as double[];
                CreateMirrorPartPackage.LogDebug("[SKETCHDIAG14][MODEL_TO_SKETCH] " +
                    (frame == null ? "unavailable" : string.Join(",", Array.ConvertAll(frame,
                        v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))));
                foreach (object point in sketch.GetSketchPoints2() as object[] ?? new object[0])
                    CreateMirrorPartPackage.LogDebug("[SKETCHDIAG14][POINT] " + DiagnosticEntity(point));
                foreach (object segment in sketch.GetSketchSegments() as object[] ?? new object[0])
                    CreateMirrorPartPackage.LogDebug("[SKETCHDIAG14][SEGMENT] " + DiagnosticEntity(segment));
                var dd = feature.GetFirstDisplayDimension() as DisplayDimension;
                while (dd != null)
                {
                    var dim = dd.GetDimension() as Dimension;
                    if (dim != null) CreateMirrorPartPackage.LogDebug("[SKETCHDIAG14][DIMENSION] name=" +
                        dim.FullName + " type=" + dim.GetType() + " value_SI=" +
                        dim.SystemValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    dd = feature.GetNextDisplayDimension(dd) as DisplayDimension;
                }
                var relations = sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll) as object[];
                int index = 0;
                foreach (object value in relations ?? new object[0])
                {
                    var relation = value as SketchRelation;
                    if (relation == null) continue;
                    try
                    {
                        string label = "[SKETCHDIAG14][RELATION] index=" + index++ + " type=" +
                            (swConstraintType_e)relation.GetRelationType();
                        var display = relation.GetDisplayDimension() as DisplayDimension;
                        var dimension = display == null ? null : display.GetDimension() as Dimension;
                        if (dimension != null) label += " dimension=" + dimension.FullName;
                        CreateMirrorPartPackage.LogDebug(label);
                        foreach (object entity in relation.GetEntities() as object[] ?? new object[0])
                            CreateMirrorPartPackage.LogDebug("[SKETCHDIAG14][RELATION_ENTITY] " + DiagnosticEntity(entity));
                    }
                    catch (Exception ex) { CreateMirrorPartPackage.LogDebug("[SKETCHDIAG14][RELATION_READ_FAILED] " + ex.Message); }
                }
            }
            catch (Exception ex) { CreateMirrorPartPackage.LogDebug("[SKETCHDIAG14][SNAPSHOT_INCOMPLETE] " + ex.Message); }
            CreateMirrorPartPackage.LogDebug("[SKETCHDIAG14][END] sketch=" + feature.Name);
        }

        public static Sketch FreeSketchForMutation(ModelDoc2 partDoc, Feature sketchFeat, bool preserveConstraints = true)
        {
            if (partDoc == null || sketchFeat == null) return null;

            Sketch swSketch = sketchFeat.GetSpecificFeature2() as Sketch;
            if (swSketch == null) return null;
            LogConstraintSnapshot(sketchFeat, swSketch);

            // 1. Kích hoạt chế độ Edit Sketch (Bắt buộc phải mở Sketch mới can thiệp được)
            if (!sketchFeat.Select2(false, 0))
                throw new InvalidOperationException("Cannot select sketch for mutation: " + sketchFeat.Name);
            partDoc.EditSketch();

            Sketch active = partDoc.SketchManager.ActiveSketch;
            bool sameSketch = false;
            if (active != null)
            {
                IntPtr expected = IntPtr.Zero, actual = IntPtr.Zero;
                try
                {
                    expected = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(swSketch);
                    actual = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(active);
                    sameSketch = expected == actual;
                }
                finally
                {
                    if (expected != IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(expected);
                    if (actual != IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(actual);
                }
            }
            CreateMirrorPartPackage.LogDebug("[POINTMOVE16][EDIT_STATE] sketch=" + sketchFeat.Name +
                " active=" + (active != null) + " identityMatches=" + sameSketch +
                " addToDB=" + partDoc.SketchManager.AddToDB);
            if (!sameSketch)
                throw new InvalidOperationException("POINTMOVE16: Expected sketch is not active; mutation cancelled.");

            // Default is non-destructive. Solver rejection must be diagnosed, never
            // worked around by deleting the user's dimensions and relations.
            if (preserveConstraints)
            {
                CreateMirrorPartPackage.LogDebug("[PARAMETRIC13][PRESERVE] sketch=" + sketchFeat.Name +
                    " dimensionsDeleted=0 relationsDeleted=0");
                return swSketch;
            }

            // 2. XÓA TOÀN BỘ KÍCH THƯỚC (DIMENSIONS)
            DisplayDimension dispDim = sketchFeat.GetFirstDisplayDimension() as DisplayDimension;
            List<string> dimNames = new List<string>();
            List<DisplayDimension> dispDims = new List<DisplayDimension>();
            
            while (dispDim != null)
            {
                Dimension dim = dispDim.GetDimension() as Dimension;
                if (dim != null)
                {
                    dimNames.Add(dim.Name + "@" + sketchFeat.Name);
                    dispDims.Add(dispDim);
                }
                dispDim = sketchFeat.GetNextDisplayDimension(dispDim) as DisplayDimension;
            }

            CreateMirrorPartPackage.LogDebug($"[MUTATION] Sketch {sketchFeat.Name} has {dimNames.Count} dimensions.");
            
            if (dimNames.Count > 0)
            {
                partDoc.ClearSelection2(true);
                foreach (string dimName in dimNames)
                {
                    bool sel = partDoc.Extension.SelectByID2(dimName, "DIMENSION", 0, 0, 0, true, 0, null, 0);
                    CreateMirrorPartPackage.LogDebug($"[MUTATION] Select {dimName} -> {sel}");
                }
                
                // Thử select qua DisplayDimension.Select
                foreach (DisplayDimension dd in dispDims)
                {
                    Annotation ann = dd.GetAnnotation() as Annotation;
                    if (ann != null)
                    {
                        bool selAnn = ann.Select3(true, null);
                        CreateMirrorPartPackage.LogDebug($"[MUTATION] Select Annotation -> {selAnn}");
                    }
                }

                bool delSuccess = partDoc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                CreateMirrorPartPackage.LogDebug($"[MUTATION] DeleteSelection2 -> {delSuccess}");
            }

            // 3. XÓA TOÀN BỘ RÀNG BUỘC HÌNH HỌC (RELATIONS) - NATIVE API
            ISketchRelationManager relMgr = swSketch.RelationManager;
            if (relMgr != null)
            {
                // Gọi API gốc của SolidWorks để tận diệt mọi Relation (kể cả external, dangling)
                relMgr.DeleteAllRelations();
                CreateMirrorPartPackage.LogDebug($"[MUTATION] Deleted all relations via native API.");
            }

            // CHÚ Ý: CHÚNG TA KHÔNG THOÁT SKETCH Ở ĐÂY!
            // Giữ nguyên trạng thái Edit Sketch để Bước 2 ngay lập tức can thiệp vào tọa độ điểm.
            
            return swSketch;
        }

        /// <summary>
        /// Di chuyển toàn bộ các điểm trong Sketch qua mặt phẳng đối xứng (Bảo toàn Internal ID)
        /// Mặc định: Lật đối xứng qua trục Y của Sketch (newX = -x, newY = y) hoặc theo tọa độ 3D bất biến
        /// </summary>
        public static void MutateSketchPoints(ModelDoc2 partDoc, Sketch swSketch, List<SketchPointSnapshot> pristinePoints = null, PlaneData mirrorPlane = null, CutAuditSnapshot21 audit = null)
        {
            MutateSketchPoints(partDoc, swSketch, 0.0, 0.0, 0.0, 0.0, pristinePoints, mirrorPlane, audit);
        }

        /// <summary>
        /// Di chuyển toàn bộ các điểm trong Sketch phản chiếu qua tọa độ 3D không gian bất biến (hoặc trục 2D ax1, ay1 -> ax2, ay2)
        /// </summary>
        public static void MutateSketchPoints(
            ModelDoc2 partDoc, 
            Sketch swSketch, 
            double ax1, double ay1, 
            double ax2, double ay2, 
            List<SketchPointSnapshot> pristinePoints = null,
            PlaneData mirrorPlane = null, CutAuditSnapshot21 audit = null)
        {
            if (partDoc == null || swSketch == null) return;

            object[] sketchPointsObj = EnumerateSketchPoints28(swSketch);
            if (sketchPointsObj.Length == 0) return;

            MathTransform m2s = null;
            try { m2s = swSketch.ModelToSketchTransform; } catch { }

            ISldWorks swApp = SwAddin.InstanceSwApp;
            if (swApp == null)
            {
                try
                {
                    swApp = System.Runtime.InteropServices.Marshal.GetActiveObject("SldWorks.Application") as ISldWorks;
                }
                catch { }
            }
            IMathUtility mathUtility = swApp != null ? swApp.GetMathUtility() as IMathUtility : null;

            double dx = ax2 - ax1;
            double dy = ay2 - ay1;
            double len = Math.Sqrt(dx * dx + dy * dy);

            bool useGeneralLine = (len > 1e-9);
            double nx = 0, ny = 0;
            if (useGeneralLine)
            {
                double ux = dx / len;
                double uy = dy / len;
                nx = -uy;
                ny = ux;
            }

            bool isParallelPlane = false;
            if (mirrorPlane != null && mirrorPlane.Normal != null && mirrorPlane.Normal.Length >= 3 && m2s != null && mathUtility != null)
            {
                try
                {
                    MathTransform s2m = m2s.Inverse() as MathTransform;
                    if (s2m != null)
                    {
                        MathVector zVec = mathUtility.CreateVector(new double[] { 0, 0, 1 }) as MathVector;
                        MathVector nVec = (zVec != null) ? zVec.MultiplyTransform(s2m) as MathVector : null;
                        double[] n = (nVec != null) ? nVec.ArrayData as double[] : null;
                        if (n != null && n.Length >= 3)
                        {
                            double dot = n[0] * mirrorPlane.Normal[0] + n[1] * mirrorPlane.Normal[1] + n[2] * mirrorPlane.Normal[2];
                            isParallelPlane = (Math.Abs(Math.Abs(dot) - 1.0) < 0.05);
                        }
                    }
                }
                catch { }
            }

            CreateMirrorPartPackage.LogDebug($"[MUTATION_AXIS] axis1=({ax1:F6},{ay1:F6}) axis2=({ax2:F6},{ay2:F6}) len={len:F6} useGeneralLine={useGeneralLine} nx={nx:F6} ny={ny:F6} isParallel={isParallelPlane} pristineCount={pristinePoints?.Count ?? 0} hasMirrorPlane={mirrorPlane != null}");
            int[] sourceForLive47 = null;
            if (pristinePoints != null)
            {
                var live47 = new List<SketchPointSnapshot>();
                MathTransform sketchToModel47 = m2s == null ? null : m2s.Inverse() as MathTransform;
                foreach (object value in sketchPointsObj)
                {
                    var point47 = value as SketchPoint;
                    if (point47 == null) throw new InvalidOperationException("POINT47 owned point enumeration changed.");
                    var id47 = point47.GetID() as int[];
                    if (id47 == null || id47.Length < 2)
                        throw new InvalidOperationException("POINT47 live sketch point ID unavailable.");
                    var entry47 = new SketchPointSnapshot { Id1 = id47[0], Id2 = id47[1],
                        X = point47.X, Y = point47.Y, Index = live47.Count };
                    if (sketchToModel47 != null && mathUtility != null)
                    {
                        var local47 = mathUtility.CreatePoint(new[] { point47.X, point47.Y,
                            swSketch.Is3D() ? point47.Z : 0.0 }) as MathPoint;
                        var world47 = local47 == null ? null : local47.MultiplyTransform(sketchToModel47) as MathPoint;
                        var xyz47 = world47 == null ? null : world47.ArrayData as double[];
                        if (xyz47 != null && xyz47.Length >= 3)
                        { entry47.ModelX = xyz47[0]; entry47.ModelY = xyz47[1];
                          entry47.ModelZ = xyz47[2]; entry47.HasModelCoords = true; }
                    }
                    live47.Add(entry47);
                }
                string evidence47;
                sourceForLive47 = SketchPointMatcher47.Resolve(pristinePoints, live47, mirrorPlane, out evidence47);
                CreateMirrorPartPackage.LogDebug("[POINT47][MAP] mode=" + evidence47 +
                    " count=" + sourceForLive47.Length + " idsChanged=" + (evidence47 != "POINT_ID"));
            }
            int ptIdx = 0;
            var targets = new List<Tuple<SketchPoint, double, double>>();
            int liveIndex47 = 0;
            foreach (object ptObj in sketchPointsObj)
            {
                SketchPoint swPt = ptObj as SketchPoint;
                if (swPt == null) continue;

                SketchPointSnapshot snap = sourceForLive47 == null ? null :
                    pristinePoints[sourceForLive47[liveIndex47]];
                liveIndex47++;

                double x = (snap != null) ? snap.X : swPt.X;
                double y = (snap != null) ? snap.Y : swPt.Y;

                double newX = 0, newY = 0;
                bool mapped3D = false;

                // [3D INVARIANT POINT MAPPING]
                // Nếu có mirrorPlane và tọa độ Model 3D nguyên bản, chiếu điểm 3D qua mirrorPlane rồi chuyển về hệ tọa độ Sketch đang mở.
                // Giải pháp này độc lập 100% với việc SolidWorks có đảo trục UV của mặt phẳng hay không!
                if (mirrorPlane != null && snap != null && snap.HasModelCoords && m2s != null && mathUtility != null)
                {
                    try
                    {
                        double dot = (snap.ModelX - mirrorPlane.Origin[0]) * mirrorPlane.Normal[0]
                                   + (snap.ModelY - mirrorPlane.Origin[1]) * mirrorPlane.Normal[1]
                                   + (snap.ModelZ - mirrorPlane.Origin[2]) * mirrorPlane.Normal[2];
                        double mxMirr = snap.ModelX - 2.0 * dot * mirrorPlane.Normal[0];
                        double myMirr = snap.ModelY - 2.0 * dot * mirrorPlane.Normal[1];
                        double mzMirr = snap.ModelZ - 2.0 * dot * mirrorPlane.Normal[2];

                        MathPoint mpMirr = mathUtility.CreatePoint(new double[] { mxMirr, myMirr, mzMirr }) as MathPoint;
                        MathPoint mp2D = mpMirr != null ? mpMirr.MultiplyTransform(m2s) as MathPoint : null;
                        double[] arr2D = mp2D != null ? mp2D.ArrayData as double[] : null;
                        // Accepted local-deviation class: see EnsureReflectedSupport20 (the plane
                        // holds the reflected points within that deviation and is kept).
                        if (arr2D != null && arr2D.Length >= 3 && Math.Abs(arr2D[2]) <=
                            (NativeBodyCoincidence67.SurfaceChainResidualAllowed71 ? NativeBodyCoincidence67.LocalDeviationLimit71 : 1e-7))
                        {
                            newX = arr2D[0];
                            newY = arr2D[1];
                            mapped3D = true;
                            CreateMirrorPartPackage.LogDebug($"[MUTATION_PT_3D_{ptIdx++}] (modelSrc: {snap.ModelX * 1000.0:F3},{snap.ModelY * 1000.0:F3},{snap.ModelZ * 1000.0:F3} -> modelTgt: {mxMirr * 1000.0:F3},{myMirr * 1000.0:F3},{mzMirr * 1000.0:F3}) -> skTgt: ({newX * 1000.0:F3}, {newY * 1000.0:F3}mm)");
                        }
                    }
                    catch { }
                }

                if (!mapped3D)
                {
                    if (mirrorPlane != null)
                        throw new InvalidOperationException("PARAMETRIC13: Cannot place reflected model point on the current sketch plane; " +
                            "explicit support-plane remapping is required. No 2D projection fallback allowed.");
                    if (useGeneralLine)
                    {
                        double dist = (x - ax1) * nx + (y - ay1) * ny;
                        newX = x - 2.0 * dist * nx;
                        newY = y - 2.0 * dist * ny;
                    }
                    else
                    {
                        if (isParallelPlane)
                        {
                            newX = x;
                            newY = y;
                        }
                        else
                        {
                            newX = -x;
                            newY = y;
                        }
                    }
                    CreateMirrorPartPackage.LogDebug($"[MUTATION_PT_{ptIdx++}] (source: {x * 1000.0:F3}, {y * 1000.0:F3}mm, live: {swPt.X * 1000.0:F3}, {swPt.Y * 1000.0:F3}mm) -> target: ({newX * 1000.0:F3}, {newY * 1000.0:F3}mm)");
                }

                targets.Add(Tuple.Create(swPt, newX, newY));
            }

            var circleSeams71 = ExemptClosedCircleSeams71(swSketch, targets);
            Action verifyDimensions;
            // Where the points are BEFORE any move: a failed first attempt leaves
            // them displaced, and counterparts must be paired on the source layout.
            var before71 = targets.Select(t => new[] { t.Item1.X, t.Item1.Y }).ToList();
            // An arc that straddles the mirror line has to exchange its own two
            // ends (no API reverses an arc). Sending each end to its own
            // reflection makes them cross and the solver drags them back; a
            // failed attempt also left the sketch displaced and once dropped the
            // COM connection natively. For such a sketch the counterpart
            // assignment is therefore applied directly.
            arcEndsExchanged71 = false;
            var direct71 = CounterpartTargets71(swSketch, targets, before71);
            if (direct71 != null && arcEndsExchanged71)
            {
                CreateMirrorPartPackage.LogDebug("[SYMMETRIC71][COUNTERPART_FIRST] reason=ARC_EXCHANGES_ITS_OWN_ENDS");
                targets.Clear();
                targets.AddRange(direct71);
            }
            try { verifyDimensions = ApplyConstraintAwareTargets(partDoc, swSketch, targets, audit, mirrorPlane); }
            catch (SketchPointMoveRejected58)
            {
                var counterpart71 = CounterpartTargets71(swSketch, targets, before71);
                if (counterpart71 == null) throw;
                targets.Clear();
                targets.AddRange(counterpart71);
                verifyDimensions = ApplyConstraintAwareTargets(partDoc, swSketch, targets, audit, mirrorPlane);
            }
            partDoc.InsertSketch2(true);
            verifyDimensions();
            foreach (var seam71 in circleSeams71)
            {
                double radius71 = Math.Sqrt(Math.Pow(seam71.Item1.X - seam71.Item2, 2) + Math.Pow(seam71.Item1.Y - seam71.Item3, 2));
                if (Math.Abs(radius71 - seam71.Item4) > 1e-7)
                    throw new InvalidOperationException("CIRCLE71: Full circle did not keep its radius at the reflected centre. expected=" +
                        seam71.Item4.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " actual=" +
                        radius71.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            // Check after leaving the sketch: the solver may undo a coordinate
            // assignment even when the COM setter itself reports no exception.
            int rejectedPoints = 0;
            foreach (var target in targets)
            {
                double ex = target.Item1.X - target.Item2;
                double ey = target.Item1.Y - target.Item3;
                double error = Math.Sqrt(ex * ex + ey * ey);
                CreateMirrorPartPackage.LogDebug("[SKETCHDIAG14][POINT_RESULT] " + DiagnosticEntity(target.Item1) +
                    " targetXY_m=" + target.Item2.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                    target.Item3.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                    " error_m=" + error.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                if (error > 1e-7) rejectedPoints++;
            }
            if (rejectedPoints > 0)
                throw new InvalidOperationException("PARAMETRIC13: Sketch solver rejected reflected points=" + rejectedPoints +
                    "; dimensions/relations retained. See SKETCHDIAG14 relation/entity and point results.");
            CreateMirrorPartPackage.LogDebug("[REFLECT19][SKETCH_COMMIT_PASS] points=" + targets.Count +
                " dimensionsRestored=True targetGeometryVerified=True");
        }

        private static bool IsReflectionInvariantConstraint47(swConstraintType_e type)
        {
            switch (type)
            {
                case swConstraintType_e.swConstraintType_HORIZONTAL:
                case swConstraintType_e.swConstraintType_VERTICAL:
                case swConstraintType_e.swConstraintType_HORIZPOINTS:
                case swConstraintType_e.swConstraintType_VERTPOINTS:
                case swConstraintType_e.swConstraintType_TANGENT:
                case swConstraintType_e.swConstraintType_PARALLEL:
                case swConstraintType_e.swConstraintType_PERPENDICULAR:
                case swConstraintType_e.swConstraintType_COINCIDENT:
                case swConstraintType_e.swConstraintType_CONCENTRIC:
                case swConstraintType_e.swConstraintType_SYMMETRIC:
                case swConstraintType_e.swConstraintType_ATMIDDLE:
                case swConstraintType_e.swConstraintType_ATINTERSECT:
                case swConstraintType_e.swConstraintType_SAMELENGTH:
                case swConstraintType_e.swConstraintType_COLINEAR:
                case swConstraintType_e.swConstraintType_CORADIAL:
                case swConstraintType_e.swConstraintType_USEEDGE:
                case swConstraintType_e.swConstraintType_ATPIERCE:
                case swConstraintType_e.swConstraintType_MERGEPOINTS:
                case swConstraintType_e.swConstraintType_NORMAL:
                case swConstraintType_e.swConstraintType_NORMALPOINTS:
                case swConstraintType_e.swConstraintType_INTERSECTION:
                case swConstraintType_e.swConstraintType_ISOBYPOINT:
                case swConstraintType_e.swConstraintType_SAMEISOPARAM:
                case swConstraintType_e.swConstraintType_FITSPLINE:
                case swConstraintType_e.swConstraintType_EQUALCURVATURE:
                case swConstraintType_e.swConstraintType_EQUALTANGENT:
                case swConstraintType_e.swConstraintType_TANGENTFACE:
                case swConstraintType_e.swConstraintType_EQUALCURV3DALIGN:
                case swConstraintType_e.swConstraintType_C3TOUCH:
                case swConstraintType_e.swConstraintType_SAMECURVELENGTH:
                case swConstraintType_e.swConstraintType_SAMESLOTS:
                    return true;
                default:
                    return false;
            }
        }

        // Nearly symmetric profile: moving every point to its OWN reflection
        // swaps the two halves across the sketch and the solver rejects it. The
        // reflected profile is the same point set, so let each point take the
        // reflected position of its mirror counterpart instead; it then moves
        // only by the asymmetry. Allowed only when that reassignment is a
        // bijection that maps every segment onto a segment of the same type
        // (lines by end points, arcs by end points and centre), so the result
        // is exactly the set of reflected source segments. Returns null when no
        // such non-trivial reassignment exists. The caller's body oracle still
        // decides.
        // Set by CounterpartTargets71: some arc's start and end map onto each other.
        private static bool arcEndsExchanged71;

        private static List<Tuple<SketchPoint, double, double>> CounterpartTargets71(Sketch sketch,
            List<Tuple<SketchPoint, double, double>> targets, List<double[]> before)
        {
            Func<object, string> key = value =>
            {
                var point = value as SketchPoint;
                var id = point == null ? null : point.GetID() as int[];
                return id == null || id.Length < 2 ? null : id[0] + ":" + id[1];
            };
            var keys = targets.Select(t => key(t.Item1)).ToList();
            if (keys.Any(k => k == null) || keys.Distinct().Count() != keys.Count) return null;
            int n = targets.Count;
            var take = new int[n];
            var used = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                int best = -1; double bestDistance = double.MaxValue;
                // Nearest target not taken yet: coincident points (for example
                // two coradial arc centres) share one location. A wrong greedy
                // choice cannot pass the segment check below.
                for (int j = 0; j < n; j++)
                {
                    if (used.Contains(j)) continue;
                    double dx = before[i][0] - targets[j].Item2, dy = before[i][1] - targets[j].Item3;
                    double distance = Math.Sqrt(dx * dx + dy * dy);
                    if (distance < bestDistance) { bestDistance = distance; best = j; }
                }
                if (best < 0) return null;
                used.Add(best);
                take[i] = best;
            }
            if (Enumerable.Range(0, n).All(i => take[i] == i)) return null;
            var counterpart = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < n; i++) counterpart[keys[i]] = keys[take[i]];
            Func<string, string> map = id => id != null && counterpart.ContainsKey(id) ? counterpart[id] : id;
            var segments = new List<string>(); var mapped = new List<string>();
            foreach (object value in sketch.GetSketchSegments() as object[] ?? new object[0])
            {
                var line = value as SketchLine; var arc = value as SketchArc;
                string a, b, c = "", type;
                if (line != null) { type = "L"; a = key(line.GetStartPoint2()); b = key(line.GetEndPoint2()); }
                else if (arc != null) { type = "A"; a = key(arc.GetStartPoint2()); b = key(arc.GetEndPoint2()); c = key(arc.GetCenterPoint2()); }
                else return null;
                bool construction = ((SketchSegment)value).ConstructionGeometry;
                Func<string, string, string, string> name = (p, q, r) => type + (construction ? "c" : "") + "|" +
                    string.Join("|", new[] { p ?? "", q ?? "" }.OrderBy(x => x, StringComparer.Ordinal)) + "|" + (r ?? "");
                segments.Add(name(a, b, c));
                mapped.Add(name(map(a), map(b), map(c)));
                if (arc != null && a != null && b != null && a != b && map(a) == b && map(b) == a) arcEndsExchanged71 = true;
            }
            if (!segments.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(mapped.OrderBy(x => x, StringComparer.Ordinal)))
            {
                CreateMirrorPartPackage.LogDebug("[SYMMETRIC71][COUNTERPART_TARGETS] points=" + n + " topologyPreserved=False result=NOT_APPLICABLE");
                return null;
            }
            var result = new List<Tuple<SketchPoint, double, double>>();
            double largest = 0;
            for (int i = 0; i < n; i++)
            {
                result.Add(Tuple.Create(targets[i].Item1, targets[take[i]].Item2, targets[take[i]].Item3));
                largest = Math.Max(largest, Math.Sqrt(Math.Pow(before[i][0] - targets[take[i]].Item2, 2) +
                    Math.Pow(before[i][1] - targets[take[i]].Item3, 2)));
            }
            CreateMirrorPartPackage.LogDebug("[SYMMETRIC71][COUNTERPART_TARGETS] points=" + n + " segments=" + segments.Count +
                " topologyPreserved=True largestMove_m=" + largest.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                " reason=OWN_REFLECTION_REJECTED");
            return result;
        }

        // A full circle is defined by its centre and radius. Its seam point
        // (start == end) follows the centre and SOLIDWORKS rejects SetCoords on
        // it, so it is not a reflection target. Exempt it only when it belongs
        // to exactly one closed arc whose centre IS a target; the caller then
        // verifies the radius at the reflected centre. Returns
        // (seam point, centre target X, centre target Y, source radius).
        private static List<Tuple<SketchPoint, double, double, double>> ExemptClosedCircleSeams71(Sketch sketch,
            List<Tuple<SketchPoint, double, double>> targets)
        {
            var result = new List<Tuple<SketchPoint, double, double, double>>();
            Func<object, string> key = value =>
            {
                var point = value as SketchPoint;
                var id = point == null ? null : point.GetID() as int[];
                return id == null || id.Length < 2 ? null : id[0] + ":" + id[1];
            };
            var uses = new Dictionary<string, int>(StringComparer.Ordinal);
            var circles = new List<SketchArc>();
            foreach (object value in sketch.GetSketchSegments() as object[] ?? new object[0])
            {
                var ends = new List<string>();
                var line = value as SketchLine;
                var arc = value as SketchArc;
                var spline = value as SketchSpline;
                if (line != null) { ends.Add(key(line.GetStartPoint2())); ends.Add(key(line.GetEndPoint2())); }
                else if (arc != null)
                {
                    ends.Add(key(arc.GetStartPoint2())); ends.Add(key(arc.GetEndPoint2()));
                    if (ends[0] != null && ends[0] == ends[1]) circles.Add(arc);
                }
                else if (spline != null)
                    foreach (object point in spline.GetPoints2() as object[] ?? new object[0]) ends.Add(key(point));
                foreach (string id in ends.Where(id => id != null).Distinct())
                { int count; uses.TryGetValue(id, out count); uses[id] = count + 1; }
            }
            foreach (SketchArc circle in circles)
            {
                string seam = key(circle.GetStartPoint2()), centre = key(circle.GetCenterPoint2());
                int seamIndex = targets.FindIndex(t => key(t.Item1) == seam);
                var centreTarget = targets.FirstOrDefault(t => key(t.Item1) == centre);
                if (seam == null || centre == null || seam == centre || seamIndex < 0 || centreTarget == null || uses[seam] != 1) continue;
                result.Add(Tuple.Create(targets[seamIndex].Item1, centreTarget.Item2, centreTarget.Item3, circle.GetRadius()));
                targets.RemoveAt(seamIndex);
                CreateMirrorPartPackage.LogDebug("[CIRCLE71][SEAM_EXEMPT] seam=" + seam + " centre=" + centre +
                    " radius_m=" + circle.GetRadius().ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                    " verification=RADIUS_AT_REFLECTED_CENTRE");
            }
            return result;
        }

        private static Action ApplyConstraintAwareTargets(ModelDoc2 model, Sketch sketch,
            List<Tuple<SketchPoint, double, double>> targets, CutAuditSnapshot21 audit, PlaneData plane)
        {
            // Geometry-first replay deliberately keeps the reflected profile and does not
            // attempt to preserve the source sketch's parametric ownership.  A fully
            // constrained source sketch is often tied to source-side faces/edges; restoring
            // those relations after reflection pulls the profile back to its old position.
            // The output remains an editable native sketch, but dimensions are reference
            // dimensions and geometric relations remain suppressed.
            bool geometryFirst53 = true;
            // What the source itself keeps suppressed stays suppressed (first pass only:
            // a rejected pass leaves its own suspensions behind).
            if (convertedAttempt71 == 0)
                suppressedInSource71 = (sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll) as object[] ?? new object[0])
                    .OfType<SketchRelation>().Where(r => r.Suppressed).ToList();
            // Dimensions are handled uniformly by release/restore. Non-dimensional
            // incidence and metric relations below are invariant under any reflection
            // isometry once their external references have been rebound.
            var equations = model.GetEquationMgr() as EquationMgr;
            if (!geometryFirst53 && equations != null && equations.GetCount() > 0)
                throw new InvalidOperationException("CONSTRAINT15: Equation-driven models need dependency-aware handling.");
            var dimensions = new List<Tuple<Dimension, int, double>>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (object item in sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll)
                as object[] ?? new object[0])
            {
                var relation = item as SketchRelation;
                if (relation == null) throw new InvalidOperationException("CONSTRAINT15: Cannot inspect sketch relation.");
                var type = (swConstraintType_e)relation.GetRelationType();
                var display = relation.GetDisplayDimension() as DisplayDimension;
                var dimension = display == null ? null : display.GetDimension() as Dimension;
                if (!geometryFirst53 && dimension == null && !IsReflectionInvariantConstraint47(type))
                    throw new InvalidOperationException("CONSTRAINT47: Directional or locking relation needs a dedicated transformer: " + type);
                if (dimension == null || !names.Add(dimension.FullName)) continue;
                int state = dimension.DrivenState;
                if (state == (int)swDimensionDrivenState_e.swDimensionDrivenUnknown ||
                    dimension.ReadOnly || dimension.IsDesignTableDimension())
                    throw new InvalidOperationException("CONSTRAINT15: Dimension is externally controlled or unavailable: " + dimension.FullName);
                dimensions.Add(Tuple.Create(dimension, state, dimension.SystemValue));
            }

            var changed = new List<Tuple<Dimension, int, double>>();
            var recreatedDimensions38 = new Dictionary<Dimension, Dimension>();
            var suspendedRelations52 = new List<SketchRelation>();
            bool automaticSolveBefore51 = true;
            bool automaticSolveSuspended51 = false;
            var targetIds = new List<int[]>();
            foreach (var target in targets)
            {
                int[] id = target.Item1.GetID() as int[];
                if (id == null || id.Length < 2)
                    throw new InvalidOperationException("REFLECT19: Target point identity unavailable.");
                targetIds.Add(new[] { id[0], id[1] });
            }
            try
            {
                foreach (var entry in dimensions)
                {
                    if (entry.Item2 != (int)swDimensionDrivenState_e.swDimensionDriving) continue;
                    changed.Add(entry); // Restore even when the setter partially succeeds.
                    entry.Item1.DrivenState = (int)swDimensionDrivenState_e.swDimensionDriven;
                    if (entry.Item1.DrivenState != (int)swDimensionDrivenState_e.swDimensionDriven)
                        throw new InvalidOperationException("CONSTRAINT15: Cannot temporarily release " + entry.Item1.FullName);
                    CreateMirrorPartPackage.LogDebug("[CONSTRAINT15][RELEASE] " + entry.Item1.FullName);
                }
                if (changed.Count > 0)
                {
                    if (geometryFirst53)
                    {
                        // Rebuilding here asks SOLIDWORKS to regenerate the dependent cut
                        // while its sketch still has the source-side geometry. That can fail
                        // and close sketch edit mode before any reflected point is assigned.
                        // In geometry-first mode the released dimensions stay driven, so the
                        // point IDs can be rebound without rebuilding until sketch commit.
                        Sketch activeSketch57 = model.SketchManager.ActiveSketch;
                        if (activeSketch57 == null || !SameProbeObject(activeSketch57, sketch))
                            throw new InvalidOperationException("REFLECT57: Sketch edit mode was lost after releasing dimensions.");
                        CreateMirrorPartPackage.LogDebug("[GEOMETRY_FIRST57][REFRESH_DEFERRED] releasedDimensions=" +
                            changed.Count + " activeSketch=True rebuildAfterSketchCommit=True");
                    }
                    else
                    {
                        sketch = RefreshReleasedSketch(model, sketch, changed);
                    }
                }
                else
                {
                    // Rebuilding before reference rebinding is invalid after an upstream
                    // feature has already been reflected: downstream sketches still point
                    // at the source-side topology and their dependent feature can fail
                    // before mutation even starts. No solver refresh is needed when no
                    // driving dimension was released.
                    CreateMirrorPartPackage.LogDebug("[REFLECT48][REFRESH_SKIPPED] reason=NoReleasedDimensions referencesWillRebindFirst=True");
                }
                if (!geometryFirst53)
                    RebindReferences30(model, sketch, audit, plane);
                else
                    CreateMirrorPartPackage.LogDebug("[GEOMETRY_FIRST53][REFERENCE_REBIND_SKIPPED] reason=IndependentProfile");
                List<SketchRelation> followingModel71 = convertedAttempt71 == 1 ? RebindConvertedEdges71(model, sketch, audit, plane) : new List<SketchRelation>();
                for (int i = 0; i < targets.Count; i++)
                    targets[i] = Tuple.Create(ResolveProbePoint(sketch, targetIds[i]), targets[i].Item2, targets[i].Item3);
                ExchangeEndsOfEdgeCopies71(sketch, followingModel71, targets);
                // A point constrained Coincident/Collinear to an external entity cannot be
                // moved one-at-a-time while automatic solve is active.  After all external
                // references have been rebound, move the complete point set atomically and
                // let the solver validate the reflected, internally consistent state once.
                automaticSolveBefore51 = sketch.GetAutomaticSolve();
                bool needsMove52 = targets.Any(target =>
                    Math.Abs(target.Item1.X - target.Item2) > 1e-7 ||
                    Math.Abs(target.Item1.Y - target.Item3) > 1e-7);
                if (needsMove52)
                {
                    // SetCoords can still reject an endpoint when a non-dimensional
                    // Coincident/Collinear/etc. relation owns it. Suppress relations while
                    // automatic solve is still active so SOLIDWORKS actually releases their
                    // degrees of freedom. Disabling solve first only changes the Suppressed
                    // property; the active constraint graph may continue locking the point.
                    // Keep the relation objects and definitions, then restore them after all
                    // points and dimensions have been updated.
                    foreach (SketchRelation relation52 in
                        (sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll)
                            as object[] ?? new object[0]).OfType<SketchRelation>())
                    {
                        DisplayDimension display52 = relation52.GetDisplayDimension() as DisplayDimension;
                        swConstraintType_e type52 = (swConstraintType_e)relation52.GetRelationType();
                        // An Offset Entities relation keeps coupling its curves even
                        // after its distance dimension is released to driven (native
                        // observation: every re-placed end point dragged another one
                        // back). In geometry-first mode it is suspended like the
                        // other geometric relations.
                        bool dimensionedOffset71 = geometryFirst53 && display52 != null &&
                            type52 == swConstraintType_e.swConstraintType_SKETCHOFFSET;
                        if ((display52 != null && !dimensionedOffset71) || relation52.Suppressed) continue;
                        if (followingModel71.Any(r => SameProbeObject(r, relation52))) continue;
                        if (!geometryFirst53 && !IsReflectionInvariantConstraint47(type52))
                            throw new InvalidOperationException("CONSTRAINT52: Relation cannot be suspended generically: " + type52);
                        relation52.Suppressed = true;
                        if (!relation52.Suppressed)
                            throw new InvalidOperationException("CONSTRAINT52: Cannot temporarily suppress relation: " + type52);
                        suspendedRelations52.Add(relation52);
                        CreateMirrorPartPackage.LogDebug("[CONSTRAINT52][SUPPRESS] type=" + type52);
                    }
                    CreateMirrorPartPackage.LogDebug("[CONSTRAINT52][SUPPRESS_SUMMARY] count=" + suspendedRelations52.Count +
                        " solverActiveDuringSuppression=" + automaticSolveBefore51);
                }
                if (automaticSolveBefore51 && needsMove52)
                {
                    sketch.SetAutomaticSolve(false);
                    if (sketch.GetAutomaticSolve())
                        throw new InvalidOperationException("REFLECT51: Cannot suspend automatic sketch solve for atomic reflection.");
                    automaticSolveSuspended51 = true;
                    CreateMirrorPartPackage.LogDebug("[REFLECT51][SOLVER_SUSPEND] automaticSolve=False points=" + targets.Count +
                        " constraintsAlreadySuppressed=" + suspendedRelations52.Count);
                }
                CreateMirrorPartPackage.LogDebug("[REFLECT19][APPLY_BEGIN] points=" + targets.Count);
                foreach (var target in targets)
                {
                    CreateMirrorPartPackage.LogDebug("[POINTMOVE16][BEFORE] " + DiagnosticEntity(target.Item1) +
                        " targetXY_m=" + target.Item2.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," +
                        target.Item3.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    if (Math.Abs(target.Item1.X - target.Item2) <= 1e-7 &&
                        Math.Abs(target.Item1.Y - target.Item3) <= 1e-7)
                    {
                        CreateMirrorPartPackage.LogDebug("[POINTMOVE16][SKIP] alreadyAtTarget=True");
                        continue;
                    }
                    bool moved = target.Item1.SetCoords(target.Item2, target.Item3, target.Item1.Z);
                    CreateMirrorPartPackage.LogDebug("[POINTMOVE16][AFTER] apiReturned=" + moved + " " + DiagnosticEntity(target.Item1));
                    if (!moved)
                    {
                        throw new SketchPointMoveRejected58("REFLECT19: SetCoords rejected target after solver synchronization; " +
                            "segment-level geometry replay required. point=" + DiagnosticEntity(target.Item1));
                    }
                }
                // Moving one end of a segment can carry its other end along, so a
                // point that was already at its target (or was placed earlier)
                // may have drifted. Re-place only the drifted points, a bounded
                // number of times; the exact check below still decides.
                for (int pass71 = 0; pass71 < 3; pass71++)
                {
                    var drifted71 = targets.Where(t => Math.Abs(t.Item1.X - t.Item2) > 1e-7 ||
                        Math.Abs(t.Item1.Y - t.Item3) > 1e-7).ToList();
                    if (drifted71.Count == 0) break;
                    CreateMirrorPartPackage.LogDebug("[REFLECT71][REPLACE_DRIFTED] pass=" + (pass71 + 1) + " points=" + drifted71.Count);
                    foreach (var target in drifted71)
                        target.Item1.SetCoords(target.Item2, target.Item3, target.Item1.Z);
                }
                // The end of a converted or offset spline only slides along that curve:
                // the curve is a fixed copy of a model edge and does not follow its points.
                // In the accepted local-deviation class such an end may stay off its target
                // by up to that deviation (natively 2.6e-5 m on the offset of a 2.9 m flat
                // pattern edge); the body check after the feature measures the result.
                // Which point is an end of such a curve is not readable for every kind of
                // copy, so the rule is by sketch: it holds a segment that is neither a
                // line nor an arc, and the point stayed within the local deviation.
                bool fixedCurve71 = convertedAttempt71 == 2 && NativeBodyCoincidence67.SurfaceChainResidualAllowed71 &&
                    (sketch.GetSketchSegments() as object[] ?? new object[0]).OfType<SketchSegment>().Any(s =>
                        s.GetType() != (int)swSketchSegments_e.swSketchLINE && s.GetType() != (int)swSketchSegments_e.swSketchARC);
                double worstOnFixedCurve71 = 0;
                foreach (var target in targets.ToList())
                {
                    double off71 = Math.Sqrt(Math.Pow(target.Item1.X - target.Item2, 2) + Math.Pow(target.Item1.Y - target.Item3, 2));
                    if (!fixedCurve71 || off71 <= 1e-7 || off71 > NativeBodyCoincidence67.LocalDeviationLimit71) continue;
                    worstOnFixedCurve71 = Math.Max(worstOnFixedCurve71, off71);
                    targets.Remove(target);
                }
                if (worstOnFixedCurve71 > 0)
                    CreateMirrorPartPackage.LogDebug("[REFLECT71][END_ON_FIXED_CURVE] worstResidual_m=" + worstOnFixedCurve71.ToString("E2", System.Globalization.CultureInfo.InvariantCulture) +
                        " limit_m=" + NativeBodyCoincidence67.LocalDeviationLimit71.ToString("E0", System.Globalization.CultureInfo.InvariantCulture));
                foreach (var target in targets)
                    if (Math.Abs(target.Item1.X - target.Item2) > 1e-7 || Math.Abs(target.Item1.Y - target.Item3) > 1e-7)
                        // Same class as a rejected SetCoords: a point cannot take its own
                        // reflection. Native case: a single arc, whose mirror image needs its
                        // start and end exchanged (no API reverses an arc), so each end drags
                        // the other back. The caller then retries with counterpart targets.
                        throw new SketchPointMoveRejected58("REFLECT52: Coordinate assignment failed while constraints were suspended.");
                CreateMirrorPartPackage.LogDebug("[REFLECT52][TARGETS_ASSIGNED] verified=True constraintsPendingRestore=" +
                    suspendedRelations52.Count);
            }
            finally
            {
                var failures = new List<string>();
                if (!geometryFirst53)
                {
                    foreach (var entry in changed)
                    {
                        string originalFullName;
                        try { originalFullName = entry.Item1.FullName; }
                        catch { originalFullName = "unavailable-dimension"; }
                        try
                        {
                            bool recreateProxyDimension = HasReflectedProxyDimension37(audit, originalFullName);
                            Dimension activeDimension = entry.Item1;
                            if (recreateProxyDimension)
                            {
                                activeDimension = RecreateReflectedProxyDimension38(model, sketch, audit, plane,
                                    originalFullName, entry.Item3);
                                recreatedDimensions38[entry.Item1] = activeDimension;
                            }
                            else
                            {
                                activeDimension.DrivenState = entry.Item2;
                                if (activeDimension.DrivenState != entry.Item2) throw new InvalidOperationException("state mismatch");
                            }
                            if (entry.Item2 == (int)swDimensionDrivenState_e.swDimensionDriving)
                            {
                                double commandedValue = entry.Item3;
                                activeDimension.SystemValue = commandedValue;
                                double tolerance = Math.Max(1e-9, Math.Abs(entry.Item3) * 1e-7);
                                double restoredValue = activeDimension.SystemValue;
                                CreateMirrorPartPackage.LogDebug("[CONSTRAINT37][VALUE_RESTORE] name=" + activeDimension.FullName +
                                    " expected=" + entry.Item3.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                                    " commanded=" + commandedValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                                    " actual=" + restoredValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                                    " recreatedProxyDimension=" + recreateProxyDimension +
                                    " tolerance=" + tolerance.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                                double valueResidual = Math.Abs(restoredValue - entry.Item3);
                                if (valueResidual > tolerance)
                                    throw new InvalidOperationException("value mismatch");
                            }
                            CreateMirrorPartPackage.LogDebug("[CONSTRAINT15][RESTORE] " + activeDimension.FullName);
                        }
                        catch (Exception ex) { failures.Add(originalFullName + ": " + ex.Message); }
                    }
                }
                else
                {
                    CreateMirrorPartPackage.LogDebug("[GEOMETRY_FIRST53][DIMENSIONS_LEFT_DRIVEN] count=" + changed.Count);
                }
                if (!geometryFirst53)
                {
                    foreach (SketchRelation relation52 in suspendedRelations52)
                    {
                        try
                        {
                            relation52.Suppressed = false;
                            if (relation52.Suppressed) throw new InvalidOperationException("suppression state mismatch");
                            CreateMirrorPartPackage.LogDebug("[CONSTRAINT52][RESTORE] type=" +
                                (swConstraintType_e)relation52.GetRelationType());
                        }
                        catch (Exception ex) { failures.Add("RelationRestore: " + ex.Message); }
                    }
                }
                else
                {
                    CreateMirrorPartPackage.LogDebug("[GEOMETRY_FIRST53][RELATIONS_LEFT_SUPPRESSED] count=" +
                        suspendedRelations52.Count);
                }
                if (automaticSolveSuspended51)
                {
                    try
                    {
                        sketch.SetAutomaticSolve(automaticSolveBefore51);
                        automaticSolveSuspended51 = false;
                        if (sketch.GetAutomaticSolve() != automaticSolveBefore51)
                            throw new InvalidOperationException("automatic solve state mismatch");
                        CreateMirrorPartPackage.LogDebug("[REFLECT51][SOLVER_RESTORE] automaticSolve=" +
                            automaticSolveBefore51 + " editRebuild=DEFERRED_UNTIL_SKETCH_COMMIT");
                    }
                    catch (Exception ex) { failures.Add("AutomaticSolve: " + ex.Message); }
                }
                if (failures.Count > 0)
                    throw new InvalidOperationException("CONSTRAINT15: Cannot restore driving dimensions; output forbidden: " + string.Join("; ", failures));
            }
            if (geometryFirst53) ReactivateEdgeCopies71(sketch, suspendedRelations52);
            for (int i = 0; i < targets.Count; i++)
                targets[i] = Tuple.Create(ResolveProbePoint(sketch, targetIds[i]), targets[i].Item2, targets[i].Item3);
            foreach (var target in targets)
                if (Math.Abs(target.Item1.X - target.Item2) > 1e-7 || Math.Abs(target.Item1.Y - target.Item3) > 1e-7)
                    throw new InvalidOperationException("REFLECT52: Reflected sketch geometry moved after solver resume.");
            CreateMirrorPartPackage.LogDebug("[REFLECT19][TARGETS_REACHED] verified=True constraintsRestored=" +
                (geometryFirst53 ? 0 : suspendedRelations52.Count) + " dimensionsRestored=" +
                (geometryFirst53 ? 0 : changed.Count) + " geometryFirst=" + geometryFirst53);
            // Verify while still editing the profile; exiting may split its support edges.
            if (!geometryFirst53 && recreatedDimensions38.Count == 0)
                RebindReferences30(model, sketch, audit, plane, true);
            else if (!geometryFirst53)
                CreateMirrorPartPackage.LogDebug("[REFERENCE38][VERIFY_BY_RECREATION] count=" + recreatedDimensions38.Count);
            else
                CreateMirrorPartPackage.LogDebug("[GEOMETRY_FIRST53][REFERENCE_VERIFY_SKIPPED] reason=IndependentProfile");
            Action verify = () =>
            {
                if (geometryFirst53)
                {
                    CreateMirrorPartPackage.LogDebug("[GEOMETRY_FIRST53][VERIFY] dimensionsIgnored=" + dimensions.Count +
                        " relationsSuppressed=" + suspendedRelations52.Count);
                    return;
                }
                foreach (var entry in dimensions)
                {
                    Dimension activeDimension;
                    if (!recreatedDimensions38.TryGetValue(entry.Item1, out activeDimension)) activeDimension = entry.Item1;
                    int actualState = activeDimension.DrivenState;
                    double actualValue = activeDimension.SystemValue;
                    double tolerance = Math.Max(1e-9, Math.Abs(entry.Item3) * 1e-7);
                    double valueResidual = Math.Abs(actualValue - entry.Item3);
                    CreateMirrorPartPackage.LogDebug("[SKETCH28][DIMENSION_READBACK] name=" + activeDimension.FullName +
                        " expectedState=" + entry.Item2 + " actualState=" + actualState +
                        " expectedValue=" + entry.Item3.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                        " actualValue=" + actualValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                        " recreatedProxyDimension=" + recreatedDimensions38.ContainsKey(entry.Item1) +
                        " tolerance=" + tolerance.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    if (actualState != entry.Item2 || valueResidual > tolerance)
                        throw new InvalidOperationException("CONSTRAINT15: Dimension state/value changed: " + activeDimension.FullName);
                }
                CreateMirrorPartPackage.LogDebug("[CONSTRAINT15][DIMENSIONS_VERIFIED] count=" + dimensions.Count +
                    " deleted=0 relationsDeleted=0");
            };
            verify();
            return verify;
        }

        private static SketchPoint ResolveProbePoint(Sketch sketch, int[] expectedId)
        {
            SketchPoint match = null;
            foreach (object value in EnumerateSketchPoints28(sketch))
            {
                var point = value as SketchPoint;
                int[] id = point == null ? null : point.GetID() as int[];
                if (id == null || id.Length < 2 || id[0] != expectedId[0] || id[1] != expectedId[1]) continue;
                if (match != null) throw new InvalidOperationException("POINTPROBE17: Point identity ambiguous.");
                match = point;
            }
            if (match == null) throw new InvalidOperationException("POINTPROBE17: Point identity missing.");
            return match;
        }

        private static bool SameProbeObject(object left, object right)
        {
            if (left == null || right == null) return false;
            IntPtr a = IntPtr.Zero, b = IntPtr.Zero;
            try
            {
                a = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(left);
                b = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(right);
                return a == b;
            }
            finally
            {
                if (a != IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(a);
                if (b != IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(b);
            }
        }

        private static Sketch RefreshReleasedSketch(ModelDoc2 model, Sketch sketch,
            List<Tuple<Dimension, int, double>> releasedDimensions)
        {
            // Synchronize solver state after releasing dimensions, then reacquire entities.
            Feature owner = null;
            foreach (var node in ADDIN.Commands.MirrorV7.FeatureTreeScannerV7.Scan(model).Nodes)
            {
                var candidate = node.Feature == null ? null : node.Feature.GetSpecificFeature2() as Sketch;
                if (SameProbeObject(candidate, sketch)) { owner = node.Feature; break; }
            }
            if (owner == null) throw new InvalidOperationException("REFLECT19: Cannot identify the sketch owner before rebuild.");
            var baseline = new List<Tuple<int[], double, double, double>>();
            foreach (object value in EnumerateSketchPoints28(sketch))
            {
                var point = value as SketchPoint;
                if (point == null) continue;
                int[] id = point.GetID() as int[];
                if (id == null || id.Length < 2) throw new InvalidOperationException("REFLECT19: Missing baseline point ID.");
                baseline.Add(Tuple.Create(id, point.X, point.Y, point.Z));
            }
            bool rebuild = model.EditRebuild3();
            CreateMirrorPartPackage.LogDebug("[REFLECT19][REFRESH] rebuild=" + rebuild +
                " active=" + (model.SketchManager.ActiveSketch != null) + " automaticSolve=" + sketch.GetAutomaticSolve());
            if (!rebuild)
            {
                bool ownerWarning;
                int ownerError = owner.GetErrorCode2(out ownerWarning);
                CreateMirrorPartPackage.LogDebug("[SKETCH26][REBUILD_FAILED] sketch=" + owner.Name +
                    " sketchError=" + ownerError + " warning=" + ownerWarning);
                foreach (Feature child in owner.GetChildren() as object[] ?? new object[0])
                {
                    bool childWarning;
                    int childError = child.GetErrorCode2(out childWarning);
                    CreateMirrorPartPackage.LogDebug("[SKETCH26][DEPENDENT_HEALTH] feature=" + child.Name +
                        " error=" + childError + " warning=" + childWarning);
                }
                throw new InvalidOperationException("REFLECT19: Rebuild failed; reflection cancelled. See SKETCH26 feature diagnostics.");
            }
            if (model.SketchManager.ActiveSketch == null)
            {
                model.ClearSelection2(true);
                if (!owner.Select2(false, 0))
                    throw new InvalidOperationException("REFLECT19: Cannot select original sketch for re-entry.");
                model.EditSketch();
            }
            Sketch reopened = model.SketchManager.ActiveSketch;
            bool identityMatches = SameProbeObject(reopened, owner.GetSpecificFeature2() as Sketch);
            CreateMirrorPartPackage.LogDebug("[REFLECT19][REENTER] sketch=" + owner.Name +
                " active=" + (reopened != null) + " identityMatches=" + identityMatches);
            if (!identityMatches) throw new InvalidOperationException("REFLECT19: Wrong or missing active sketch after rebuild.");
            sketch = reopened;
            foreach (var entry in releasedDimensions)
            {
                int state = entry.Item1.DrivenState;
                CreateMirrorPartPackage.LogDebug("[REFLECT19][DIMENSION_STATE] name=" + entry.Item1.FullName + " state=" + state);
                if (state != (int)swDimensionDrivenState_e.swDimensionDriven)
                    throw new InvalidOperationException("REFLECT19: Dimension did not remain driven after rebuild; reflection cancelled.");
            }
            foreach (var entry in baseline)
            {
                var point = ResolveProbePoint(sketch, entry.Item1);
                if (Math.Abs(point.X - entry.Item2) > 1e-7 || Math.Abs(point.Y - entry.Item3) > 1e-7 ||
                    Math.Abs(point.Z - entry.Item4) > 1e-7)
                    throw new InvalidOperationException("REFLECT19: Rebuild changed baseline coordinates; reflection cancelled.");
            }
            return sketch;
        }

        /// <summary>
        /// Phục hồi chính xác tọa độ các điểm Sketch về snapshot đã lưu (nếu bị xô lệch do Rebuild feature thất bại)
        /// </summary>
        public static bool RestoreSketchPoints(
            ModelDoc2 partDoc,
            Feature sketchFeat,
            List<SketchPointSnapshot> snapshots, bool preserveConstraints = true)
        {
            if (partDoc == null || sketchFeat == null || snapshots == null || snapshots.Count == 0) return false;
            Sketch swSketch = sketchFeat.GetSpecificFeature2() as Sketch;
            if (swSketch == null) return false;

            object[] sketchPointsObj = EnumerateSketchPoints28(swSketch);
            if (sketchPointsObj.Length == 0) return false;

            bool anyShifted = false;
            for (int i = 0; i < sketchPointsObj.Length; i++)
            {
                SketchPoint swPt = sketchPointsObj[i] as SketchPoint;
                if (swPt == null) continue;
                SketchPointSnapshot snap = null;
                try
                {
                    int[] idArr = swPt.GetID() as int[];
                    if (idArr != null && idArr.Length >= 2)
                    {
                        snap = snapshots.Find(p => p.Id1 == idArr[0] && p.Id2 == idArr[1]);
                    }
                }
                catch { }
                if (snap == null && i < snapshots.Count) snap = snapshots[i];
                if (snap != null)
                {
                    if (Math.Abs(swPt.X - snap.X) > 1e-6 || Math.Abs(swPt.Y - snap.Y) > 1e-6)
                    {
                        anyShifted = true;
                        break;
                    }
                }
            }

            if (!anyShifted) return true;

            CreateMirrorPartPackage.LogDebug($"[RESTORE_SKETCH] Sketch points in {sketchFeat.Name} shifted during candidate evaluation. Restoring pristine coordinates...");
            sketchFeat.Select2(false, 0);
            partDoc.EditSketch();

            Sketch activeSk = sketchFeat.GetSpecificFeature2() as Sketch;
            if (!preserveConstraints && activeSk != null && activeSk.RelationManager != null)
            {
                try { activeSk.RelationManager.DeleteAllRelations(); } catch { }
            }

            object[] activePts = activeSk != null ? activeSk.GetSketchPoints2() as object[] : sketchPointsObj;
            if (activePts != null)
            {
                for (int i = 0; i < activePts.Length; i++)
                {
                    SketchPoint swPt = activePts[i] as SketchPoint;
                    if (swPt == null) continue;
                    SketchPointSnapshot snap = null;
                    try
                    {
                        int[] idArr = swPt.GetID() as int[];
                        if (idArr != null && idArr.Length >= 2)
                        {
                            snap = snapshots.Find(p => p.Id1 == idArr[0] && p.Id2 == idArr[1]);
                        }
                    }
                    catch { }
                    if (snap == null && i < snapshots.Count) snap = snapshots[i];
                    if (snap != null)
                    {
                        swPt.X = snap.X;
                        swPt.Y = snap.Y;
                    }
                }
            }

            partDoc.InsertSketch2(true);
            partDoc.ForceRebuild3(false);
            return true;
        }

        /// <summary>
        /// Tái tạo các rãnh Slot đối xứng hoàn chỉnh với đầy đủ ràng buộc hình học và kích thước nguyên bản
        /// </summary>
        private sealed class MirroredPrimitive58
        {
            public SketchPrimitiveSnapshot58 Source;
            public double[] Start, End, Center, Middle;
            // A spline: its recorded points, reflected, in the sketch; and its control
            // points likewise when its definition was recorded.
            public double[][] Samples;
            public double[][] Control;
        }

        private static double[] PrimitiveReflectedLocal58(IMathUtility math, MathTransform modelToSketch,
            PlaneData plane, double[] originalModel, double planeOffset, bool alreadyReflected = false)
        {
            if (originalModel == null) return null;
            double[] reflected = alreadyReflected ? originalModel : ReflectReference30(originalModel, plane);
            MathPoint point = math.CreatePoint(reflected) as MathPoint;
            MathPoint local = point == null ? null : point.MultiplyTransform(modelToSketch) as MathPoint;
            double[] values = local == null ? null : local.ArrayData as double[];
            // Accepted local-deviation class, as where the points are placed one by one
            // (MutateSketchPoints): the plane of a sketch made in an assembly is not
            // exactly square to the mirror plane, and a point 1.2 m from it then lies
            // a little off the plane after reflection. The profile is drawn in the
            // plane; the body check measures what the cut becomes.
            double offPlane58 = values == null || values.Length < 3 ? double.NaN : Math.Abs(values[2] - planeOffset);
            if (!(offPlane58 <= (NativeBodyCoincidence67.SurfaceChainResidualAllowed71 ? NativeBodyCoincidence67.LocalDeviationLimit71 : 1e-7)))
                throw new InvalidOperationException("PRIMITIVE58: Reflected segment does not lie on the active sketch plane. offPlane_m=" +
                    offPlane58.ToString("E2", System.Globalization.CultureInfo.InvariantCulture));
            if (offPlane58 > 1e-7)
            {
                CreateMirrorPartPackage.LogDebug("[PRIMITIVE58][OFF_PLANE_ACCEPTED] offPlane_m=" + offPlane58.ToString("E2", System.Globalization.CultureInfo.InvariantCulture) +
                    " limit_m=" + NativeBodyCoincidence67.LocalDeviationLimit71.ToString("E0", System.Globalization.CultureInfo.InvariantCulture));
                NativeBodyCoincidence67.LargestAcceptedDeviation71 = Math.Max(NativeBodyCoincidence67.LargestAcceptedDeviation71, offPlane58);
            }
            return new[] { values[0], values[1], 0.0 };
        }

        private static double PositiveAngle58(double angle)
        {
            const double full = 2.0 * Math.PI;
            angle %= full;
            return angle < 0 ? angle + full : angle;
        }

        private static short ArcDirection58(double[] center, double[] start, double[] middle, double[] end)
        {
            double a = Math.Atan2(start[1] - center[1], start[0] - center[0]);
            double m = Math.Atan2(middle[1] - center[1], middle[0] - center[0]);
            double b = Math.Atan2(end[1] - center[1], end[0] - center[0]);
            double midCcw = PositiveAngle58(m - a);
            double endCcw = PositiveAngle58(b - a);
            if (Math.Abs(midCcw - endCcw) < 1e-10 || midCcw < 1e-10)
                throw new InvalidOperationException("PRIMITIVE58: Arc midpoint is degenerate.");
            return (short)(midCcw < endCcw ? 1 : -1);
        }

        private static double PrimitiveDistance58(double[] left, double[] right)
        {
            return Math.Sqrt(Math.Pow(left[0] - right[0], 2) + Math.Pow(left[1] - right[1], 2));
        }

        private static string SegmentId71(SketchSegment segment)
        {
            int[] id = segment == null ? null : segment.GetID() as int[];
            return id == null ? "" : string.Join(":", id);
        }

        // Whether a live line or arc (not a full circle, not half a turn) already
        // lies where the reflected original has to be: same kind, same ends in
        // either order, and for an arc the same centre and the same one of the two
        // arcs between those ends.
        private static bool IsAlready71(SketchSegment live, MirroredPrimitive58 entry)
        {
            const double same = 1e-8;
            if (live == null || live.GetType() != entry.Source.Type || live.ConstructionGeometry != entry.Source.Construction) return false;
            if (entry.Source.Type == (int)swSketchSegments_e.swSketchLINE)
            {
                ISketchLine line = live as ISketchLine;
                SketchPoint a = line == null ? null : line.GetStartPoint2() as SketchPoint, b = line == null ? null : line.GetEndPoint2() as SketchPoint;
                if (a == null || b == null) return false;
                double[] p = { a.X, a.Y }, q = { b.X, b.Y };
                return Math.Min(Math.Max(PrimitiveDistance58(p, entry.Start), PrimitiveDistance58(q, entry.End)),
                    Math.Max(PrimitiveDistance58(p, entry.End), PrimitiveDistance58(q, entry.Start))) <= same;
            }
            if (entry.Source.Type != (int)swSketchSegments_e.swSketchARC || entry.Source.Circle) return false;
            ISketchArc arc = live as ISketchArc;
            if (arc == null) return false;
            SketchPoint c = arc.GetCenterPoint2() as SketchPoint, s = arc.GetStartPoint2() as SketchPoint, e = arc.GetEndPoint2() as SketchPoint;
            if (c == null || s == null || e == null) return false;
            double[] centre = { c.X, c.Y }, start = { s.X, s.Y }, end = { e.X, e.Y };
            if (PrimitiveDistance58(centre, entry.Center) > same) return false;
            if (Math.Min(Math.Max(PrimitiveDistance58(start, entry.Start), PrimitiveDistance58(end, entry.End)),
                Math.Max(PrimitiveDistance58(start, entry.End), PrimitiveDistance58(end, entry.Start))) > same) return false;
            double radius = arc.GetRadius();
            double[] chord = { (start[0] + end[0]) / 2 - centre[0], (start[1] + end[1]) / 2 - centre[1] };
            double reach = Math.Sqrt(chord[0] * chord[0] + chord[1] * chord[1]);
            if (reach < 1e-6 * radius) return false;
            double side = live.GetLength() < Math.PI * radius ? 1 : -1;
            double[] middle = { centre[0] + side * chord[0] / reach * radius, centre[1] + side * chord[1] / reach * radius };
            return PrimitiveDistance58(middle, entry.Middle) <= same;
        }

        // Used only when a point setter rejects a valid reflected target. Recreate the
        // complete line/arc sketch from the original model-space snapshot, never from
        // the partly-mutated staging sketch. This remains a native driving sketch/cut.
        public static void RecreateMirroredPrimitives58(ModelDoc2 model, Sketch sketch,
            List<SketchPrimitiveSnapshot58> pristine, PlaneData plane, bool replaceGeneratedProfile = false,
            double planeOffset = 0, bool alreadyReflected = false, bool deferOwnerRebuild65 = false, bool followBodyEdges71 = false,
            bool keepOwnMirrorImage71 = false)
        {
            if (model == null || sketch == null || pristine == null || pristine.Count == 0 || plane == null)
                throw new InvalidOperationException("PRIMITIVE58: Original primitive snapshot is unavailable.");
            if (!SameProbeObject(model.SketchManager.ActiveSketch, sketch))
                throw new InvalidOperationException("PRIMITIVE58: Expected sketch is not being edited.");
            ISldWorks app = SwAddin.InstanceSwApp;
            IMathUtility math = app == null ? null : app.GetMathUtility() as IMathUtility;
            MathTransform modelToSketch = sketch.ModelToSketchTransform as MathTransform;
            if (math == null || modelToSketch == null)
                throw new InvalidOperationException("PRIMITIVE58: Sketch transform unavailable.");

            var mapped = new List<MirroredPrimitive58>();
            foreach (SketchPrimitiveSnapshot58 source in pristine)
            {
                // A spline is drawn again through its recorded points; any other
                // curve (ellipse, parabola, text) still has no handler.
                bool spline58 = source.Type == (int)swSketchSegments_e.swSketchSPLINE && source.SamplesModel != null && source.SamplesModel.Length >= 5;
                if (source.Type != (int)swSketchSegments_e.swSketchLINE &&
                    source.Type != (int)swSketchSegments_e.swSketchARC && !spline58)
                    throw new InvalidOperationException("PRIMITIVE58: Segment type " + source.Type +
                        " needs a dedicated geometry handler; original sketch retained.");
                var target = new MirroredPrimitive58
                {
                    Source = source,
                    Samples = !spline58 ? null : source.SamplesModel.Select(p =>
                        PrimitiveReflectedLocal58(math, modelToSketch, plane, p, planeOffset, alreadyReflected)).ToArray(),
                    Control = !spline58 || source.SplineControlModel == null ? null : source.SplineControlModel.Select(p =>
                        PrimitiveReflectedLocal58(math, modelToSketch, plane, p, planeOffset, alreadyReflected)).ToArray(),
                    Start = PrimitiveReflectedLocal58(math, modelToSketch, plane, source.StartModel, planeOffset, alreadyReflected),
                    End = PrimitiveReflectedLocal58(math, modelToSketch, plane, source.EndModel, planeOffset, alreadyReflected),
                    Center = PrimitiveReflectedLocal58(math, modelToSketch, plane, source.CenterModel, planeOffset, alreadyReflected),
                    Middle = PrimitiveReflectedLocal58(math, modelToSketch, plane, source.MiddleModel, planeOffset, alreadyReflected)
                };
                if (target.Start == null || (!source.Circle && target.End == null) ||
                    (source.Type == (int)swSketchSegments_e.swSketchARC && target.Center == null) ||
                    (source.Type == (int)swSketchSegments_e.swSketchARC && !source.Circle && target.Middle == null))
                    throw new InvalidOperationException("PRIMITIVE58: Incomplete primitive geometry; original sketch retained.");
                mapped.Add(target);
            }
            object[] existing = sketch.GetSketchSegments() as object[] ?? new object[0];
            if (!replaceGeneratedProfile && existing.Length != pristine.Count)
                throw new InvalidOperationException("PRIMITIVE58: Segment count changed before fallback; original sketch retained. " +
                    "pristine=" + pristine.Count + " live=" + existing.Length);

            // A segment that already is the mirror image of one of the originals
            // (natively the 44 m arc of a base flange, lying across the mirror
            // plane) is left as it is, so that what was built on the faces and
            // edges it generates keeps its references: an Edge Flange on that
            // curved edge lost its hinge when the arc was drawn again, and
            // SOLIDWORKS 2024 gives no way to hand the hinge back (Edge / Edges
            // are refused by ModifyDefinition, and CreateFeature returns nothing
            // for a flange on a curved edge, with a 3D and with a 2D profile).
            var kept71 = new Dictionary<int, string>();
            if (keepOwnMirrorImage71)
                foreach (SketchSegment live71 in existing.OfType<SketchSegment>())
                    for (int i = 0; i < mapped.Count; i++)
                    {
                        if (kept71.ContainsKey(i) || !IsAlready71(live71, mapped[i])) continue;
                        kept71.Add(i, SegmentId71(live71));
                        break;
                    }
            if (kept71.Count > 0)
                CreateMirrorPartPackage.LogDebug("[PRIMITIVE71][OWN_MIRROR_IMAGE_KEPT] segments=" + kept71.Count + " of=" + mapped.Count +
                    " indices=" + string.Join(",", kept71.Keys));
            Func<object[]> replaceable71 = () => (sketch.GetSketchSegments() as object[] ?? new object[0])
                .Where(s => !kept71.Values.Contains(SegmentId71(s as SketchSegment))).ToArray();

            bool oldAddToDb = model.SketchManager.AddToDB;
            // Attached, regenerated profiles are edited with their owner rolled back.
            // Do not toggle the native solver there: restoring it can trigger native
            // regeneration while the other profiles are still being replaced. Keep
            // the user's solver setting unchanged in this transaction mode.
            bool oldAutoSolve = !deferOwnerRebuild65 && sketch.GetAutomaticSolve();
            bool oldAutoRelations = app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations);
            bool oldInference = app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference);
            try
            {
                app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations, false);
                app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference, false);
                if (!deferOwnerRebuild65) sketch.SetAutomaticSolve(false);
                model.SketchManager.AddToDB = true;
                model.ClearSelection2(true);
                object[] replaced71 = replaceable71();
                foreach (object item in replaced71)
                {
                    SketchSegment segment = item as SketchSegment;
                    if (segment == null || !segment.Select4(true, null))
                        throw new InvalidOperationException("PRIMITIVE58: Cannot select every original segment for replacement.");
                }
                int deletionOptions65 = deferOwnerRebuild65 ? 0 : (int)swDeleteSelectionOptions_e.swDelete_Absorbed;
                // The same deletion was accepted in one native run and refused in the
                // next (a Boss profile with two converted splines). It is asked for
                // again on what is still there, and what was selected is logged.
                bool deleted58 = replaced71.Length == 0;
                for (int attempt58 = 0; attempt58 < 3 && !deleted58; attempt58++)
                {
                    object[] remaining58 = replaceable71();
                    if (attempt58 == 2)
                    {
                        // One by one, to learn which of them SOLIDWORKS keeps.
                        foreach (SketchSegment single58 in remaining58.OfType<SketchSegment>())
                        {
                            string what58 = "type=" + single58.GetType() + " id=" + string.Join(":", (int[])single58.GetID()) +
                                " construction=" + single58.ConstructionGeometry + " relations=" + (single58.GetRelations() as object[] ?? new object[0]).Length;
                            model.ClearSelection2(true);
                            bool picked58 = single58.Select4(false, null);
                            bool gone58 = picked58 && model.Extension.DeleteSelection2(0);
                            CreateMirrorPartPackage.LogDebug("[PRIMITIVE58][DELETE_ONE] " + what58 + " selected=" + picked58 + " accepted=" + gone58);
                        }
                        remaining58 = replaceable71();
                    }
                    if (attempt58 == 1)
                    {
                        // The edit this sketch is in has been through refused point moves
                        // and suspended relations; the sketch is left without a rebuild
                        // and entered again before the deletion is asked for once more.
                        var owner58 = sketch as Feature;
                        model.SketchManager.AddToDB = oldAddToDb;
                        model.SketchManager.InsertSketch(false);
                        model.ClearSelection2(true);
                        bool entered58 = owner58 != null && owner58.Select2(false, 0);
                        if (entered58)
                        {
                            model.EditSketch();
                            entered58 = SameProbeObject(model.SketchManager.ActiveSketch, sketch);
                        }
                        CreateMirrorPartPackage.LogDebug("[PRIMITIVE58][SKETCH_ENTERED_AGAIN] entered=" + entered58);
                        if (!entered58) throw new InvalidOperationException("PRIMITIVE58: Cannot enter the sketch again to delete its segments.");
                        if (!deferOwnerRebuild65) sketch.SetAutomaticSolve(false);
                        model.SketchManager.AddToDB = true;
                        remaining58 = replaceable71();
                    }
                    if (attempt58 > 0)
                    {
                        model.ClearSelection2(true);
                        foreach (SketchSegment again58 in remaining58.OfType<SketchSegment>()) again58.Select4(true, null);
                    }
                    int selected58 = ((SelectionMgr)model.SelectionManager).GetSelectedObjectCount2(-1);
                    bool accepted58 = model.Extension.DeleteSelection2(deletionOptions65);
                    int left58 = replaceable71().Length;
                    deleted58 = left58 == 0;
                    if (!accepted58 || attempt58 > 0)
                        CreateMirrorPartPackage.LogDebug("[PRIMITIVE58][DELETE] attempt=" + attempt58 + " segments=" + remaining58.Length + " selected=" + selected58 +
                            " accepted=" + accepted58 + " left=" + left58);
                }
                if (!deleted58)
                    throw new InvalidOperationException("PRIMITIVE58: Original segment deletion was rejected.");
                if ((sketch.GetSketchSegments() as object[] ?? new object[0]).Length != kept71.Count)
                    throw new InvalidOperationException("PRIMITIVE58: Original sketch segments remain after deletion.");

                const double tolerance = 1e-7;
                var anchors58 = new List<SketchPoint>(); var freeEnds58 = new List<SketchPoint>();
                for (int i = 0; i < mapped.Count; i++)
                {
                    MirroredPrimitive58 entry = mapped[i];
                    if (kept71.ContainsKey(i))
                    {
                        CreateMirrorPartPackage.LogDebug("[GEOMETRY_FIRST58][PRIMITIVE_PASS] index=" + i +
                            " type=" + entry.Source.Type + " construction=" + entry.Source.Construction + " keptAsOwnMirrorImage=True");
                        continue;
                    }
                    SketchSegment created;
                    bool exact58 = false, onBodyEdge58 = false;
                    if (entry.Source.Type == (int)swSketchSegments_e.swSketchLINE)
                    {
                        created = model.SketchManager.CreateLine(entry.Start[0], entry.Start[1], 0,
                            entry.End[0], entry.End[1], 0) as SketchSegment;
                    }
                    else if (entry.Samples != null)
                    {
                        // The same B-curve with reflected control points is the mirror
                        // image itself. Only a spline whose definition was not handed out
                        // is drawn through every second recorded point instead. Either
                        // way the new curve is measured below against all recorded points.
                        created = null;
                        double[][] control58 = entry.Control; double[] knots58 = entry.Source.SplineKnots, weights58 = entry.Source.SplineWeights;
                        int order58 = entry.Source.SplineOrder; bool homogeneous58 = entry.Source.SplineHomogeneous;
                        // On the edge of the body as it is, when that is asked for and the
                        // spline ran along one (SplineOnBodyEdge71).
                        EdgeSpline71 onEdge58 = followBodyEdges71 ? SplineOnBodyEdge71(model, math, modelToSketch, entry.Source, plane, alreadyReflected) : null;
                        if (onEdge58 != null)
                        {
                            control58 = onEdge58.Control; knots58 = onEdge58.Knots; weights58 = onEdge58.Weights;
                            order58 = onEdge58.Order; homogeneous58 = onEdge58.Homogeneous; onBodyEdge58 = true;
                        }
                        if (control58 != null)
                        {
                            var definition58 = model.SketchManager.CreateSplineParamData() as SplineParamData;
                            if (definition58 != null)
                            {
                                definition58.Dimension = weights58 == null ? 3 : 4; definition58.Order = order58; definition58.Periodic = 0;
                                definition58.ControlPointsCount = control58.Length;
                                if (definition58.SetControlPoints(control58.SelectMany((p, k) => weights58 == null ? new[] { p[0], p[1], 0.0 }
                                        : homogeneous58 ? new[] { p[0] * weights58[k], p[1] * weights58[k], 0.0, weights58[k] }
                                        : new[] { p[0], p[1], 0.0, weights58[k] }).ToArray()) &&
                                    definition58.SetKnotPoints(knots58))
                                {
                                    object made58 = model.SketchManager.CreateSplinesByEqnParams2(definition58);
                                    var list58 = made58 as object[];
                                    created = list58 != null ? (list58.Length == 1 ? list58[0] as SketchSegment : null) : made58 as SketchSegment;
                                    if (list58 != null && list58.Length > 1)
                                        throw new InvalidOperationException("PRIMITIVE58: Spline definition gave " + list58.Length + " curves at " + i);
                                }
                            }
                        }
                        exact58 = created != null;
                        if (created == null)
                            created = model.SketchManager.CreateSpline2(
                                entry.Samples.Where((p, k) => k % 2 == 0).SelectMany(p => new[] { p[0], p[1], 0.0 }).ToArray(), false) as SketchSegment;
                    }
                    else if (entry.Source.Circle)
                    {
                        created = model.SketchManager.CreateCircle(entry.Center[0], entry.Center[1], 0,
                            entry.Start[0], entry.Start[1], 0) as SketchSegment;
                    }
                    else
                    {
                        short direction = ArcDirection58(entry.Center, entry.Start, entry.Middle, entry.End);
                        created = model.SketchManager.CreateArc(entry.Center[0], entry.Center[1], 0,
                            entry.Start[0], entry.Start[1], 0, entry.End[0], entry.End[1], 0,
                            direction) as SketchSegment;
                    }
                    if (created == null) throw new InvalidOperationException("PRIMITIVE58: Segment creation failed at " + i);
                    created.ConstructionGeometry = entry.Source.Construction;
                    if (created.ConstructionGeometry != entry.Source.Construction ||
                        (entry.Samples == null && Math.Abs(created.GetLength() - entry.Source.Length) > tolerance))
                        throw new InvalidOperationException("PRIMITIVE58: Segment length/construction mismatch at " + i);
                    if (entry.Samples != null)
                    {
                        // The spline through the points is the mirror image only as far as it
                        // is measured to be: every recorded point against the new curve (its
                        // ends must be the recorded ends), and its length against the source.
                        // What is found is the accepted local deviation or the profile is refused.
                        var curve58 = created.GetCurve() as Curve;
                        double deviation58 = 0;
                        foreach (double[] p in entry.Samples)
                        {
                            var q = curve58 == null ? null : curve58.GetClosestPointOn(p[0], p[1], 0) as double[];
                            deviation58 = Math.Max(deviation58, q == null || q.Length < 3 ? double.PositiveInfinity
                                : Math.Sqrt((q[0] - p[0]) * (q[0] - p[0]) + (q[1] - p[1]) * (q[1] - p[1]) + q[2] * q[2]));
                        }
                        var ends58 = (created as SketchSpline) == null ? null : ((SketchSpline)created).GetPoints2() as object[];
                        double endResidual58 = ends58 == null || ends58.Length < 2 ? double.PositiveInfinity : Math.Min(
                            Math.Max(PrimitiveDistance58(new[] { ((SketchPoint)ends58[0]).X, ((SketchPoint)ends58[0]).Y }, entry.Start),
                                PrimitiveDistance58(new[] { ((SketchPoint)ends58[ends58.Length - 1]).X, ((SketchPoint)ends58[ends58.Length - 1]).Y }, entry.End)),
                            Math.Max(PrimitiveDistance58(new[] { ((SketchPoint)ends58[0]).X, ((SketchPoint)ends58[0]).Y }, entry.End),
                                PrimitiveDistance58(new[] { ((SketchPoint)ends58[ends58.Length - 1]).X, ((SketchPoint)ends58[ends58.Length - 1]).Y }, entry.Start)));
                        double lengthOff58 = Math.Abs(created.GetLength() - entry.Source.Length);
                        // A spline on the body edge ends where the edge does, within the
                        // deviation of the body; the other ends of the profile are joined to it below.
                        double endLimit58 = onBodyEdge58 ? NativeBodyCoincidence67.LocalDeviationLimit71 : tolerance;
                        if (ends58 != null && ends58.Length >= 2)
                            (onBodyEdge58 ? anchors58 : freeEnds58).AddRange(new[] { (SketchPoint)ends58[0], (SketchPoint)ends58[ends58.Length - 1] });
                        CreateMirrorPartPackage.LogDebug("[SPLINE58][DRAWN_AGAIN] index=" + i + " onBodyEdge=" + onBodyEdge58 + " exactDefinition=" + exact58 + " through=" +
                            (exact58 ? 0 : (entry.Samples.Length + 1) / 2) + " measuredAt=" + entry.Samples.Length +
                            " deviation_m=" + deviation58.ToString("E2", System.Globalization.CultureInfo.InvariantCulture) +
                            " ends_m=" + endResidual58.ToString("E2", System.Globalization.CultureInfo.InvariantCulture) +
                            " lengthDifference_m=" + lengthOff58.ToString("E2", System.Globalization.CultureInfo.InvariantCulture) +
                            " limit_m=" + NativeBodyCoincidence67.LocalDeviationLimit71.ToString("E0", System.Globalization.CultureInfo.InvariantCulture));
                        // The length as well: what is placed along the curve by distance (a
                        // pattern with equal spacing) moves by the difference.
                        if (!(deviation58 <= NativeBodyCoincidence67.LocalDeviationLimit71) || endResidual58 > endLimit58 ||
                            lengthOff58 > NativeBodyCoincidence67.LocalDeviationLimit71)
                            throw new InvalidOperationException("PRIMITIVE58: Spline drawn again is not the mirror image at " + i +
                                ". deviation_m=" + deviation58.ToString("E2", System.Globalization.CultureInfo.InvariantCulture));
                        if (deviation58 > 1e-7)
                        {
                            NativeBodyCoincidence67.LargestAcceptedDeviation71 = Math.Max(NativeBodyCoincidence67.LargestAcceptedDeviation71, deviation58);
                            NativeBodyCoincidence67.SurfaceChainResidualAllowed71 = true;
                        }
                    }
                    else if (entry.Source.Type == (int)swSketchSegments_e.swSketchLINE)
                    {
                        ISketchLine line = created as ISketchLine;
                        freeEnds58.AddRange(new[] { (SketchPoint)line.GetStartPoint2(), (SketchPoint)line.GetEndPoint2() });
                        double[] a = { ((SketchPoint)line.GetStartPoint2()).X, ((SketchPoint)line.GetStartPoint2()).Y };
                        double[] b = { ((SketchPoint)line.GetEndPoint2()).X, ((SketchPoint)line.GetEndPoint2()).Y };
                        double residual = Math.Min(Math.Max(PrimitiveDistance58(a, entry.Start), PrimitiveDistance58(b, entry.End)),
                            Math.Max(PrimitiveDistance58(a, entry.End), PrimitiveDistance58(b, entry.Start)));
                        if (residual > tolerance) throw new InvalidOperationException("PRIMITIVE58: Line geometry mismatch at " + i);
                    }
                    else
                    {
                        ISketchArc arc = created as ISketchArc;
                        SketchPoint center = arc.GetCenterPoint2() as SketchPoint;
                        if (center == null || PrimitiveDistance58(new[] { center.X, center.Y }, entry.Center) > tolerance)
                            throw new InvalidOperationException("PRIMITIVE58: Arc center mismatch at " + i);
                        if (!entry.Source.Circle)
                        {
                            SketchPoint start = arc.GetStartPoint2() as SketchPoint;
                            SketchPoint end = arc.GetEndPoint2() as SketchPoint;
                            double residual = Math.Min(Math.Max(PrimitiveDistance58(new[] { start.X, start.Y }, entry.Start),
                                PrimitiveDistance58(new[] { end.X, end.Y }, entry.End)),
                                Math.Max(PrimitiveDistance58(new[] { start.X, start.Y }, entry.End),
                                PrimitiveDistance58(new[] { end.X, end.Y }, entry.Start)));
                            if (residual > tolerance) throw new InvalidOperationException("PRIMITIVE58: Arc endpoints mismatch at " + i);
                        }
                    }
                    CreateMirrorPartPackage.LogDebug("[GEOMETRY_FIRST58][PRIMITIVE_PASS] index=" + i +
                        " type=" + entry.Source.Type + " construction=" + entry.Source.Construction);
                }
                // The other ends of the profile that met a spline now lying on the body
                // edge are put on its ends, so that the profile stays closed.
                if (anchors58.Count > 0)
                {
                    int joined58 = 0; double farthest58 = 0;
                    foreach (SketchPoint free58 in freeEnds58)
                        foreach (SketchPoint anchor58 in anchors58)
                        {
                            double gap58 = PrimitiveDistance58(new[] { free58.X, free58.Y }, new[] { anchor58.X, anchor58.Y });
                            if (gap58 <= 1e-9 || gap58 > NativeBodyCoincidence67.LocalDeviationLimit71) continue;
                            free58.SetCoords(anchor58.X, anchor58.Y, 0);
                            if (PrimitiveDistance58(new[] { free58.X, free58.Y }, new[] { anchor58.X, anchor58.Y }) > 1e-9)
                                throw new InvalidOperationException("PRIMITIVE58: An end of the profile could not be joined to the spline on the body edge.");
                            joined58++; farthest58 = Math.Max(farthest58, gap58);
                            break;
                        }
                    CreateMirrorPartPackage.LogDebug("[SPLINE71][ENDS_JOINED] splinesOnBodyEdge=" + anchors58.Count / 2 + " endsMoved=" + joined58 +
                        " farthest_m=" + farthest58.ToString("E2", System.Globalization.CultureInfo.InvariantCulture));
                    NativeBodyCoincidence67.LargestAcceptedDeviation71 = Math.Max(NativeBodyCoincidence67.LargestAcceptedDeviation71, farthest58);
                }
                CreateMirrorPartPackage.LogDebug("[SKETCH65][COUNT_BEGIN] deferOwnerRebuild=" + deferOwnerRebuild65);
                var active65 = model.SketchManager.ActiveSketch;
                if (active65 == null || !SameProbeObject(active65, sketch))
                    throw new InvalidOperationException("PRIMITIVE65: Active sketch changed during primitive replacement.");
                int count65 = (active65.GetSketchSegments() as object[] ?? new object[0]).Length;
                CreateMirrorPartPackage.LogDebug("[SKETCH65][COUNT_END] count=" + count65);
                if (count65 != pristine.Count)
                    throw new InvalidOperationException("PRIMITIVE58: Recreated segment count mismatch.");
            }
            finally
            {
                CreateMirrorPartPackage.LogDebug("[SKETCH65][RESTORE_SETTINGS_BEGIN] solverToggled=" + !deferOwnerRebuild65);
                try { model.SketchManager.AddToDB = oldAddToDb; } catch { }
                if (!deferOwnerRebuild65)
                    try { sketch.SetAutomaticSolve(oldAutoSolve); } catch { }
                try { app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations, oldAutoRelations); } catch { }
                try { app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference, oldInference); } catch { }
                CreateMirrorPartPackage.LogDebug("[SKETCH65][RESTORE_SETTINGS_END]");
            }
            // What SOLIDWORKS makes of the re-drawn profile as a cut profile: for the
            // log, and for the message when it refuses to leave the sketch.
            int open65 = 0, closed65 = 0, use65 = -2;
            try { use65 = sketch.CheckFeatureUse((int)swSketchCheckFeatureProfileUsage_e.swSketchCheckFeature_CUTEXTRUDE, ref open65, ref closed65); } catch { }
            string featureUse65 = "cutProfileStatus=" + use65 + " openContours=" + open65 + " closedContours=" + closed65;
            CreateMirrorPartPackage.LogDebug("[SKETCH65][FEATURE_USE] " + featureUse65);
            CreateMirrorPartPackage.LogDebug("[SKETCH65][EXIT_BEGIN] updateEditRebuild=" + !deferOwnerRebuild65);
            model.SketchManager.InsertSketch(!deferOwnerRebuild65);
            if (model.SketchManager.ActiveSketch != null)
                throw new InvalidOperationException("PRIMITIVE65: Sketch exit did not close the active edit. " + featureUse65);
            CreateMirrorPartPackage.LogDebug("[SKETCH65][EXIT_END]");
            CreateMirrorPartPackage.LogDebug("[GEOMETRY_FIRST58][PRIMITIVE_RECREATION_PASS] segments=" + pristine.Count +
                " nativeSketch=True rebuildPending=True");
        }

        public static void RecreateMirroredSlots(
            ModelDoc2 partDoc, 
            Sketch swSketch, 
            double ax1, double ay1, 
            double ax2, double ay2, 
            List<SketchSlotSnapshot> pristineSlots,
            PlaneData mirrorPlane = null)
        {
            if (partDoc == null || swSketch == null || pristineSlots == null || pristineSlots.Count == 0) return;

            // 1. Xóa các đoạn vẽ cũ và điểm cũ trong Sketch đang mở
            object[] segs = swSketch.GetSketchSegments() as object[];
            if (segs != null && segs.Length > 0)
            {
                partDoc.ClearSelection2(true);
                foreach (object sObj in segs)
                {
                    SketchSegment s = sObj as SketchSegment;
                    if (s != null)
                    {
                        s.Select4(true, null);
                    }
                }
                partDoc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }

            object[] remainingPts = swSketch.GetSketchPoints2() as object[];
            if (remainingPts != null && remainingPts.Length > 0)
            {
                partDoc.ClearSelection2(true);
                foreach (object pObj in remainingPts)
                {
                    SketchPoint p = pObj as SketchPoint;
                    if (p != null)
                    {
                        p.Select4(true, null);
                    }
                }
                partDoc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }

            // 2. Tắt tạm thời Automatic Relations và Inference để SolidWorks không bắt dính/lệch tâm vào các cạnh lân cận
            ISldWorks swApp = SwAddin.InstanceSwApp;
            if (swApp == null)
            {
                try
                {
                    swApp = System.Runtime.InteropServices.Marshal.GetActiveObject("SldWorks.Application") as ISldWorks;
                }
                catch { }
            }
            bool oldAutoRel = true;
            bool oldInference = true;
            if (swApp != null)
            {
                try
                {
                    oldAutoRel = swApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations);
                    oldInference = swApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference);
                    swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations, false);
                    swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference, false);
                }
                catch { }
            }

            try
            {
                MathTransform m2s = null;
                try { m2s = swSketch.ModelToSketchTransform; } catch { }
                IMathUtility mathUtility = swApp != null ? swApp.GetMathUtility() as IMathUtility : null;

                // 3. Chuẩn bị trục đối xứng 2D
                double dx = ax2 - ax1;
                double dy = ay2 - ay1;
                double len = Math.Sqrt(dx * dx + dy * dy);

                bool useGeneralLine = (len > 1e-9);
                double nx = 0, ny = 0;
                if (useGeneralLine)
                {
                    double ux = dx / len;
                    double uy = dy / len;
                    nx = -uy;
                    ny = ux;
                }

                // 4. Tái tạo từng Slot bằng API chuẩn CreateSketchSlot
                for (int i = 0; i < pristineSlots.Count; i++)
                {
                    SketchSlotSnapshot slotSnap = pristineSlots[i];

                    double newX1 = 0, newY1 = 0;
                    double newX2 = 0, newY2 = 0;
                    double newX3 = 0, newY3 = 0;
                    bool mapped3D = false;

                    if (mirrorPlane != null && slotSnap.HasModelCoords && m2s != null && mathUtility != null)
                    {
                        try
                        {
                            // Map P1
                            double dot1 = (slotSnap.ModelX1 - mirrorPlane.Origin[0]) * mirrorPlane.Normal[0]
                                        + (slotSnap.ModelY1 - mirrorPlane.Origin[1]) * mirrorPlane.Normal[1]
                                        + (slotSnap.ModelZ1 - mirrorPlane.Origin[2]) * mirrorPlane.Normal[2];
                            double mx1 = slotSnap.ModelX1 - 2.0 * dot1 * mirrorPlane.Normal[0];
                            double my1 = slotSnap.ModelY1 - 2.0 * dot1 * mirrorPlane.Normal[1];
                            double mz1 = slotSnap.ModelZ1 - 2.0 * dot1 * mirrorPlane.Normal[2];
                            MathPoint mp1 = mathUtility.CreatePoint(new double[] { mx1, my1, mz1 }) as MathPoint;
                            MathPoint p2D1 = mp1 != null ? mp1.MultiplyTransform(m2s) as MathPoint : null;
                            double[] a1 = p2D1 != null ? p2D1.ArrayData as double[] : null;

                            // Map P2
                            double dot2 = (slotSnap.ModelX2 - mirrorPlane.Origin[0]) * mirrorPlane.Normal[0]
                                        + (slotSnap.ModelY2 - mirrorPlane.Origin[1]) * mirrorPlane.Normal[1]
                                        + (slotSnap.ModelZ2 - mirrorPlane.Origin[2]) * mirrorPlane.Normal[2];
                            double mx2 = slotSnap.ModelX2 - 2.0 * dot2 * mirrorPlane.Normal[0];
                            double my2 = slotSnap.ModelY2 - 2.0 * dot2 * mirrorPlane.Normal[1];
                            double mz2 = slotSnap.ModelZ2 - 2.0 * dot2 * mirrorPlane.Normal[2];
                            MathPoint mp2 = mathUtility.CreatePoint(new double[] { mx2, my2, mz2 }) as MathPoint;
                            MathPoint p2D2 = mp2 != null ? mp2.MultiplyTransform(m2s) as MathPoint : null;
                            double[] a2 = p2D2 != null ? p2D2.ArrayData as double[] : null;

                            if (a1 != null && a1.Length >= 2 && a2 != null && a2.Length >= 2)
                            {
                                newX1 = a1[0]; newY1 = a1[1];
                                newX2 = a2[0]; newY2 = a2[1];

                                if (slotSnap.CreationType == (int)swSketchSlotCreationType_e.swSketchSlotCreationType_3pointarc)
                                {
                                    double dot3 = (slotSnap.ModelX3 - mirrorPlane.Origin[0]) * mirrorPlane.Normal[0]
                                                + (slotSnap.ModelY3 - mirrorPlane.Origin[1]) * mirrorPlane.Normal[1]
                                                + (slotSnap.ModelZ3 - mirrorPlane.Origin[2]) * mirrorPlane.Normal[2];
                                    double mx3 = slotSnap.ModelX3 - 2.0 * dot3 * mirrorPlane.Normal[0];
                                    double my3 = slotSnap.ModelY3 - 2.0 * dot3 * mirrorPlane.Normal[1];
                                    double mz3 = slotSnap.ModelZ3 - 2.0 * dot3 * mirrorPlane.Normal[2];
                                    MathPoint mp3 = mathUtility.CreatePoint(new double[] { mx3, my3, mz3 }) as MathPoint;
                                    MathPoint p2D3 = mp3 != null ? mp3.MultiplyTransform(m2s) as MathPoint : null;
                                    double[] a3 = p2D3 != null ? p2D3.ArrayData as double[] : null;
                                    if (a3 != null && a3.Length >= 2) { newX3 = a3[0]; newY3 = a3[1]; }
                                }
                                mapped3D = true;
                            }
                        }
                        catch { }
                    }

                    if (!mapped3D)
                    {
                        if (useGeneralLine)
                        {
                            double dist1 = (slotSnap.X1 - ax1) * nx + (slotSnap.Y1 - ay1) * ny;
                            newX1 = slotSnap.X1 - 2.0 * dist1 * nx;
                            newY1 = slotSnap.Y1 - 2.0 * dist1 * ny;

                            double dist2 = (slotSnap.X2 - ax1) * nx + (slotSnap.Y2 - ay1) * ny;
                            newX2 = slotSnap.X2 - 2.0 * dist2 * nx;
                            newY2 = slotSnap.Y2 - 2.0 * dist2 * ny;

                            if (slotSnap.CreationType == (int)swSketchSlotCreationType_e.swSketchSlotCreationType_3pointarc)
                            {
                                double dist3 = (slotSnap.X3 - ax1) * nx + (slotSnap.Y3 - ay1) * ny;
                                newX3 = slotSnap.X3 - 2.0 * dist3 * nx;
                                newY3 = slotSnap.Y3 - 2.0 * dist3 * ny;
                            }
                        }
                        else
                        {
                            newX1 = -slotSnap.X1;
                            newY1 = slotSnap.Y1;

                            newX2 = -slotSnap.X2;
                            newY2 = slotSnap.Y2;

                            newX3 = -slotSnap.X3;
                            newY3 = slotSnap.Y3;
                        }
                    }

                    // Lưu ý: slotSnap.X1/Y1 và X2/Y2 từ GetSlotPoints() luôn là 2 tâm cung tròn (arc centers).
                    // Do đó với straight slot, bắt buộc phải dùng CenterCenter để SolidWorks không tự offset thêm Width/2.
                    bool straightSlot56 =
                        slotSnap.CreationType == (int)swSketchSlotCreationType_e.swSketchSlotCreationType_line ||
                        slotSnap.CreationType == (int)swSketchSlotCreationType_e.swSketchSlotCreationType_center_line;
                    int slotLenType = straightSlot56
                        ? (int)swSketchSlotLengthType_e.swSketchSlotLengthType_CenterCenter
                        : slotSnap.LengthType;

                    SketchSlot newSlot = partDoc.SketchManager.CreateSketchSlot(
                        slotSnap.CreationType,
                        slotLenType,
                        slotSnap.Width,
                        newX1, newY1, 0.0,
                        newX2, newY2, 0.0,
                        newX3, newY3, 0.0,
                        slotSnap.CenterArcDirection,
                        false);

                    if (newSlot == null)
                        throw new InvalidOperationException("GEOMETRY_FIRST56: CreateSketchSlot returned null at index " + i);

                    object[] createdPoints56 = newSlot.GetSlotPoints() as object[];
                    if (createdPoints56 == null || createdPoints56.Length < 2)
                        throw new InvalidOperationException("GEOMETRY_FIRST56: Recreated slot points unavailable at index " + i);
                    SketchPoint createdP1_56 = createdPoints56[0] as SketchPoint;
                    SketchPoint createdP2_56 = createdPoints56[1] as SketchPoint;
                    if (createdP1_56 == null || createdP2_56 == null)
                        throw new InvalidOperationException("GEOMETRY_FIRST56: Recreated slot center points invalid at index " + i);

                    double direct56 = Math.Max(
                        Math.Sqrt(Math.Pow(createdP1_56.X - newX1, 2) + Math.Pow(createdP1_56.Y - newY1, 2)),
                        Math.Sqrt(Math.Pow(createdP2_56.X - newX2, 2) + Math.Pow(createdP2_56.Y - newY2, 2)));
                    double reversed56 = Math.Max(
                        Math.Sqrt(Math.Pow(createdP1_56.X - newX2, 2) + Math.Pow(createdP1_56.Y - newY2, 2)),
                        Math.Sqrt(Math.Pow(createdP2_56.X - newX1, 2) + Math.Pow(createdP2_56.Y - newY1, 2)));
                    double pointResidual56 = Math.Min(direct56, reversed56);
                    double widthResidual56 = Math.Abs(newSlot.Width - slotSnap.Width);
                    double slotTolerance56 = Math.Max(1e-7, Math.Abs(slotSnap.Width) * 1e-6);
                    if (pointResidual56 > slotTolerance56 || widthResidual56 > slotTolerance56)
                        throw new InvalidOperationException("GEOMETRY_FIRST56: Recreated slot geometry mismatch at index " + i +
                            " pointResidual_m=" + pointResidual56.ToString("R") +
                            " widthResidual_m=" + widthResidual56.ToString("R") +
                            " tolerance_m=" + slotTolerance56.ToString("R"));

                    // Constraints and dimensions are intentionally not recreated. The exact
                    // slot geometry is certified above and the downstream cut/body oracle is
                    // still mandatory before publication.
                    CreateMirrorPartPackage.LogDebug($"[GEOMETRY_FIRST56][SLOT_GEOMETRY_PASS] index={i} type={slotSnap.CreationType} lengthType={slotLenType} W={slotSnap.Width * 1000.0:F3}mm P1=({newX1 * 1000.0:F3},{newY1 * 1000.0:F3}) P2=({newX2 * 1000.0:F3},{newY2 * 1000.0:F3}) pointResidual_m={pointResidual56:R} widthResidual_m={widthResidual56:R} mapped3D={mapped3D}");
                }
            }
            finally
            {
                if (swApp != null)
                {
                    try
                    {
                        swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations, oldAutoRel);
                        swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference, oldInference);
                    }
                    catch { }
                }
            }

            partDoc.InsertSketch2(true);
        }

        /// <summary>
        /// Lật hướng mũi tên lệnh Extrude Cut
        /// </summary>
        public static bool ReverseExtrudeDirection(ModelDoc2 partDoc, Feature cutFeature)
        {
            if (partDoc == null || cutFeature == null) return false;

            IExtrudeFeatureData2 def = cutFeature.GetDefinition() as IExtrudeFeatureData2;
            if (def != null)
            {
                bool access = def.AccessSelections(partDoc, null);
                CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] Feature {cutFeature.Name} AccessSelections={access}");
                if (access)
                {
                    // Chỉ đảo duy nhất hướng đùn, giữ nguyên mọi thông số khác
                    def.ReverseDirection = !def.ReverseDirection;
                    
                    // Thử check EndCondition
                    int endCond = def.GetEndCondition(true);
                    CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] EndCondition={endCond} ReverseDirection={def.ReverseDirection}");
                    
                    bool success = cutFeature.ModifyDefinition(def, partDoc, null);
                    if (!success && endCond == (int)swEndConditions_e.swEndCondUpToNext)
                    {
                        CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] UpToNext failed with reversed direction. Trying ThroughAll fallback...");
                        try
                        {
                            def.SetEndCondition(true, (int)swEndConditions_e.swEndCondThroughAll);
                            success = cutFeature.ModifyDefinition(def, partDoc, null);
                            CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] ThroughAll fallback result={success}");
                        }
                        catch (Exception ex)
                        {
                            CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] ThroughAll fallback exception: {ex.Message}");
                        }
                    }

                    if (!success)
                    {
                        CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] ModifyDefinition failed with reversed direction. Trying BothDirections fallback...");
                        try
                        {
                            def.BothDirections = true;
                            def.SetEndCondition(false, (int)swEndConditions_e.swEndCondThroughAll);
                            success = cutFeature.ModifyDefinition(def, partDoc, null);
                            CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] BothDirections fallback result={success}");
                        }
                        catch (Exception ex)
                        {
                            CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] BothDirections fallback exception: {ex.Message}");
                        }
                    }

                    def.ReleaseSelectionAccess();
                    
                    CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] ModifyDefinition={success}");
                    return success;
                }
            }
            return false;
        }

        /// <summary>
        /// Kiểm tra danh sách sketch segment có phải là biên dạng mở (open profile) hay không.
        /// Một biên dạng mở có ít nhất 1 đỉnh bậc 1 (chỉ nối với 1 đoạn thẳng, ví dụ đường cắt đơn hoặc polyline hở).
        /// </summary>
        public static bool IsOpenProfileSegments(List<SketchSegment> profileSegments)
        {
            if (profileSegments == null || profileSegments.Count == 0) return false;

            // Trường hợp phổ biến nhất: 1 đoạn sketch đơn (thường là SketchLine cắt mở)
            if (profileSegments.Count == 1 && profileSegments[0] is SketchLine)
            {
                return true;
            }

            try
            {
                Dictionary<SketchPoint, int> pointUsage = new Dictionary<SketchPoint, int>();
                foreach (var seg in profileSegments)
                {
                    SketchPoint sp = null, ep = null;
                    if (seg is SketchLine line)
                    {
                        sp = line.GetStartPoint2() as SketchPoint;
                        ep = line.GetEndPoint2() as SketchPoint;
                    }
                    else if (seg is SketchArc arc)
                    {
                        if (arc.IsCircle() == 1)
                        {
                            // Đường tròn hoàn chỉnh là biên dạng kín tuyệt đối, không có điểm mút mở
                            continue;
                        }
                        sp = arc.GetStartPoint2() as SketchPoint;
                        ep = arc.GetEndPoint2() as SketchPoint;
                    }
                    else if (seg is SketchSpline spl)
                    {
                        object[] splPts = spl.GetPoints2() as object[];
                        if (splPts != null && splPts.Length >= 2)
                        {
                            sp = splPts[0] as SketchPoint;
                            ep = splPts[splPts.Length - 1] as SketchPoint;
                        }
                    }

                    if (sp != null)
                    {
                        pointUsage[sp] = pointUsage.ContainsKey(sp) ? pointUsage[sp] + 1 : 1;
                    }
                    if (ep != null)
                    {
                        pointUsage[ep] = pointUsage.ContainsKey(ep) ? pointUsage[ep] + 1 : 1;
                    }
                }

                foreach (var kvp in pointUsage)
                {
                    if (kvp.Value == 1) return true;
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Kiểm tra sketch có phải là biên dạng mở (open profile) hay không.
        /// </summary>
        public static bool IsOpenProfileSketch(Sketch sk)
        {
            if (sk == null) return false;
            try
            {
                object[] contours = sk.GetSketchContours() as object[];
                if (contours != null && contours.Length > 0)
                {
                    bool hasClosed = false;
                    bool hasOpen = false;
                    foreach (object item in contours)
                    {
                        SketchContour contour = item as SketchContour;
                        if (contour != null)
                        {
                            if (contour.IsClosed()) hasClosed = true;
                            else hasOpen = true;
                        }
                    }
                    if (hasClosed && !hasOpen) return false;
                    if (hasOpen && !hasClosed) return true;
                }

                object[] segs = sk.GetSketchSegments() as object[];
                if (segs == null || segs.Length == 0) return false;

                List<SketchSegment> activeSegs = new List<SketchSegment>();
                foreach (object s in segs)
                {
                    SketchSegment seg = s as SketchSegment;
                    if (seg != null && !seg.ConstructionGeometry)
                    {
                        activeSegs.Add(seg);
                    }
                }

                return IsOpenProfileSegments(activeSegs);
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Đảo chiều vùng cắt (Flip Side to Cut) của Extrude Cut
        /// </summary>
        public static bool ToggleFlipSideToCut(ModelDoc2 partDoc, Feature cutFeature)
        {
            if (partDoc == null || cutFeature == null) return false;

            IExtrudeFeatureData2 ext = cutFeature.GetDefinition() as IExtrudeFeatureData2;
            if (ext != null)
            {
                bool access = ext.AccessSelections(partDoc, null);
                CreateMirrorPartPackage.LogDebug($"[FLIP_SIDE_TO_CUT] Feature {cutFeature.Name} AccessSelections={access}");
                if (access)
                {
                    ext.FlipSideToCut = !ext.FlipSideToCut;
                    CreateMirrorPartPackage.LogDebug($"[FLIP_SIDE_TO_CUT] Feature {cutFeature.Name} new FlipSideToCut={ext.FlipSideToCut}");
                    bool success = cutFeature.ModifyDefinition(ext, partDoc, null);
                    ext.ReleaseSelectionAccess();
                    CreateMirrorPartPackage.LogDebug($"[FLIP_SIDE_TO_CUT] ModifyDefinition={success}");
                    return success;
                }
            }
            return false;
        }
    }
}
