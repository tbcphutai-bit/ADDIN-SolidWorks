using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendEnvelope
{
    internal static class PrismaticEnvelopeCompiler
    {
        internal const double SourceToleranceMm = 1e-5;
        private const double Joint = 1e-10;
        internal static SheetEnvelopeCapture Read(PlacementModelCapture model, PlacementBendSnapshot bend, CancellationToken token)
        {
            var body = model.Bodies.FirstOrDefault(b => b.Id == bend.BodyId);
            string source = body?.EnvelopeTopology?.SourceSha256 ?? ToolEvidenceCanonical.Hash("LegacyManagedBody", model.SnapshotId, bend.BodyId);
            var binding = new EnvelopeBinding(model, bend, source);
            try
            {
                token.ThrowIfCancellationRequested();
                if (!model.CompleteBodyCoverage || model.Bodies.Count != 1 || body == null)
                    throw new InvalidDataException("AMBIGUOUS_MULTIBODY_OR_INCOMPLETE_SCOPE");
                var uniform = model.Sections.Single(s => s.FeatureId == bend.FeatureId);
                if (uniform.Coverage != SheetCoverage.Unsupported2DEnvelope)
                {
                    var positions = body.Edges.SelectMany(e => new[] { bend.Frame.AxialDistance(e.A), bend.Frame.AxialDistance(e.B) }).ToArray();
                    var material = new CollisionMaterial2D(binding.FrameId, new[] { new CollisionComponent2D(new CollisionContour2D(
                        "UNIFORM", uniform.Boundary, SourceToleranceMm, uniform.Region)) }, source, model.SimulationOnly, () => binding.Current, true);
                    return new SheetEnvelopeCapture(binding, new[] { new AxialPrismaticSweep(new AxialInterval(positions.Min(), positions.Max()), material) },
                        EnvelopeCoverageTier.Uniform2DProven, "ExistingCompleteUniformExtrusionProofV1", model.SimulationOnly, SourceToleranceMm, new[] { "FINAL_MODEL_ONLY" });
                }
                var shell = body.EnvelopeTopology;
                if (shell == null) throw new InvalidDataException("INSUFFICIENT_NATIVE_BREP_CAPTURE");
                if (shell.Error != null) throw new InvalidDataException(shell.Error);
                ValidateShell(shell, token);
                var edges = shell.Edges.ToDictionary(e => e.Id);
                var frame = binding.WorkFrame;
                var events = new List<double>();
                foreach (var edge in shell.Edges)
                {
                    token.ThrowIfCancellationRequested();
                    double a = frame.AxialDistance(edge.A), b = frame.AxialDistance(edge.B);
                    if (edge.Kind != 1 && edge.Kind != 2) throw new InvalidDataException("UNSUPPORTED_NATIVE_TRIM_CURVE");
                    bool horizontal = Math.Abs(a - b) <= Joint;
                    bool axial = edge.Kind == 1 && ToolPoint.Distance(frame.Project(edge.A), frame.Project(edge.B)) <= Joint;
                    if (!horizontal && !axial) throw new InvalidDataException("UNSUPPORTED_NONPRISMATIC_TRIM");
                    if (edge.Kind == 2 && (edge.Arc == null || !horizontal || Math.Abs(frame.AxialDistance(edge.Arc.MidMm) - a) > Joint ||
                        Math.Abs(frame.AxialDistance(edge.Arc.CenterMm) - a) > Joint)) throw new InvalidDataException("UNSUPPORTED_NONPLANAR_NATIVE_ARC");
                    events.Add(a); events.Add(b);
                }
                foreach (var face in shell.Faces)
                {
                    var faceEdges=face.Loops.SelectMany(l=>l.Edges).Select(e=>edges[e.EdgeId]).Distinct().ToArray();
                    if (face.Kind == 1)
                    {
                        if(Math.Abs(face.OutwardNormal.Length-1)>1e-12||faceEdges.SelectMany(e=>new[]{e.A,e.B}.Concat(e.Arc==null?new PlacementVector3[0]:new[]{e.Arc.MidMm,e.Arc.CenterMm})).Any(p=>
                            Math.Abs(PlacementVector3.Dot(p-face.PointMm,face.OutwardNormal))>SourceToleranceMm))
                            throw new InvalidDataException("INCONSISTENT_NATIVE_PLANE_TRIM");
                        double dot = Math.Abs(PlacementVector3.Dot(face.OutwardNormal, frame.BendAxis));
                        if (dot > 1e-12 && Math.Abs(dot - 1) > 1e-12) throw new InvalidDataException("UNSUPPORTED_SLOPED_NATIVE_FACE");
                        if (dot > .5)
                        {
                            double z = frame.AxialDistance(face.PointMm);
                            if (face.Loops.SelectMany(l => l.Edges).Any(e => Math.Abs(frame.AxialDistance(edges[e.EdgeId].A) - z) > Joint ||
                                Math.Abs(frame.AxialDistance(edges[e.EdgeId].B) - z) > Joint)) throw new InvalidDataException("UNSUPPORTED_NATIVE_CAP_ORIENTATION");
                            foreach(var loop in face.Loops)
                            {
                                var directed=loop.Edges.Select(e=> {
                                    var edge=edges[e.EdgeId];
                                    if(edge.Arc?.FullCircle==true)throw new InvalidDataException("UNSUPPORTED_PERIODIC_CAP_ORIENTATION");
                                    var curve=BendSheetSection.Project(edge,frame);return e.Forward?curve:Reverse(curve);
                                }).ToArray();
                                double sign=PlacementVector3.Dot(face.OutwardNormal,frame.BendAxis)*(loop.IsOuter?1:-1);
                                if(SignedTwiceArea(directed)*sign<=SourceToleranceMm*directed.Sum(CollisionGeometry.Length))
                                    throw new InvalidDataException("INCONSISTENT_NATIVE_CAP_LOOP_ORIENTATION");
                            }
                        }
                    }
                    else if (face.Kind == 2)
                    {
                        if (Math.Abs(Math.Abs(PlacementVector3.Dot(face.Axis, frame.BendAxis)) - 1) > 1e-12)
                            throw new InvalidDataException("UNSUPPORTED_CROSS_AXIS_NATIVE_CYLINDER");
                        var rim = face.Loops.SelectMany(l => l.Edges).Select(e => edges[e.EdgeId]).Distinct().ToArray();
                        var arcs = rim.Where(e => e.Kind == 2).ToArray();
                        if (arcs.Length != 2 || rim.Length != 2 && rim.Length != 4 || arcs.Any(e => e.Arc == null) ||
                            Math.Abs(arcs[0].Arc.RadiusMm - arcs[1].Arc.RadiusMm) > Joint ||
                            ToolPoint.Distance(frame.Project(arcs[0].A), frame.Project(arcs[1].A)) > Joint &&
                            ToolPoint.Distance(frame.Project(arcs[0].A), frame.Project(arcs[1].B)) > Joint)
                            throw new InvalidDataException("UNSUPPORTED_TRIMMED_PERIODIC_FACE");
                        if(Math.Abs(arcs[0].Arc.RadiusMm-face.RadiusMm)>SourceToleranceMm||
                            ToolPoint.Distance(frame.Project(arcs[0].Arc.CenterMm),frame.Project(arcs[1].Arc.CenterMm))>Joint||
                            ToolPoint.Distance(frame.Project(arcs[0].Arc.CenterMm),frame.Project(face.PointMm))>SourceToleranceMm||
                            !(arcs[0].Arc.FullCircle&&arcs[1].Arc.FullCircle)&&ToolPoint.Distance(frame.Project(arcs[0].Arc.MidMm),frame.Project(arcs[1].Arc.MidMm))>Joint)
                            throw new InvalidDataException("NONMATCHING_NATIVE_CYLINDER_CAPS");
                    }
                    else throw new InvalidDataException("UNSUPPORTED_NATIVE_SURFACE");
                }
                // Small numerical duplicates may represent one station. Never
                // merge distinct close features under a loose geometry tol.
                var stations = new List<double>();
                foreach (double z in events.OrderBy(x => x))
                    if (stations.Count == 0 || z - stations.Last() > Joint) stations.Add(z);
                if (stations.Count < 2 || stations.Count > 4096) throw new InvalidDataException("INVALID_AXIAL_PARTITION");
                var solids = new List<AxialSolidPrimitive>();
                for (int i = 1; i < stations.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var interval = new AxialInterval(stations[i - 1], stations[i]);
                    if (interval.LengthMm < 4 * SourceToleranceMm) throw new InvalidDataException("UNRESOLVED_THIN_AXIAL_FEATURE");
                    var curves = SliceShell(shell, edges, frame, interval.MidMm);
                    if (curves.Count == 0) continue; // Complete shell proves a void slab.
                    var material = OrientedMaterial(curves, binding);
                    if (!material.Valid) throw new InvalidDataException("UNSUPPORTED_SLICE_TOPOLOGY");
                    solids.Add(new AxialPrismaticSweep(interval, material));
                }
                if (solids.Count == 0) throw new InvalidDataException("EMPTY_OR_INVERTED_NATIVE_SHELL");
                return new SheetEnvelopeCapture(binding, solids, EnvelopeCoverageTier.AxialSlicesWithCertifiedCoverage,
                    "CompleteClosedPrismaticShellPartitionV1", model.SimulationOnly, SourceToleranceMm, new[] { "FINAL_MODEL_ONLY" });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentException || ex is InvalidOperationException)
            {
                if(body?.EnvelopeTopology!=null && (ex.Message=="UNSUPPORTED_NONPRISMATIC_TRIM"||ex.Message=="UNSUPPORTED_CROSS_AXIS_NATIVE_CYLINDER"||ex.Message=="UNSUPPORTED_PERIODIC_CAP_ORIENTATION"))
                    return OrthogonalNativeEnvelopeCompiler.Read(binding,body.EnvelopeTopology,token);
                return new SheetEnvelopeCapture(binding, null, EnvelopeCoverageTier.UnknownCoverage, "NativeProofUnavailable", model.SimulationOnly,
                    SourceToleranceMm, new[] { "AXIAL_COVERAGE_UNKNOWN", ex is InvalidDataException ? ex.Message : "INVALID_NATIVE_ENVELOPE_INPUT" }
                    .Concat(body?.EnvelopeTopology?.Faces.Where(f=>f.Kind!=1&&f.Kind!=2).Select(f=>"UNSUPPORTED_NATIVE_SURFACE_FACE:"+f.Id)??new string[0])
                    .Concat(body?.EnvelopeTopology?.Edges.Where(e=>e.Kind!=1&&e.Kind!=2).Select(e=>"UNSUPPORTED_NATIVE_TRIM_EDGE:"+e.Id)??new string[0]));
            }
        }
        internal static void ValidateShell(NativeEnvelopeBody shell, CancellationToken token)
        {
            if (!shell.SolidBody || shell.NativeEdgeCount != shell.Edges.Count || shell.NativeFaceCount != shell.Faces.Count ||
                shell.Faces.Count == 0 || shell.Faces.Count > 12000 || shell.Edges.Count > 24000 || shell.Edges.Select(e => e.Id).Distinct().Count() != shell.Edges.Count)
                throw new InvalidDataException("INCOMPLETE_NATIVE_SOLID_TOPOLOGY");
            var edges = shell.Edges.ToDictionary(e => e.Id);
            var uses = new Dictionary<int, List<Tuple<int,bool>>>();
            foreach (var face in shell.Faces)
            {
                token.ThrowIfCancellationRequested();
                if (face.Loops.Count == 0 || face.Loops.Any(l => l.Singular || l.Edges.Count == 0)) throw new InvalidDataException("UNSUPPORTED_SINGULAR_NATIVE_LOOP");
                foreach (var loop in face.Loops)
                {
                    for (int i = 0; i < loop.Edges.Count; i++)
                    {
                        var a = loop.Edges[i]; var b = loop.Edges[(i + 1) % loop.Edges.Count];
                        if (!edges.ContainsKey(a.EdgeId) || !edges.ContainsKey(b.EdgeId)) throw new InvalidDataException("MISSING_NATIVE_COEDGE");
                        var ae = edges[a.EdgeId]; var be = edges[b.EdgeId];
                        if (!ae.Faces.Contains(face.Id)) throw new InvalidDataException("INCONSISTENT_NATIVE_FACE_ADJACENCY");
                        if (((a.Forward ? ae.B : ae.A) - (b.Forward ? be.A : be.B)).Length > Joint) throw new InvalidDataException("OPEN_OR_MISORIENTED_NATIVE_LOOP");
                        if (!uses.ContainsKey(a.EdgeId)) uses[a.EdgeId] = new List<Tuple<int,bool>>();
                        uses[a.EdgeId].Add(Tuple.Create(face.Id,a.Forward));
                    }
                }
            }
            if (shell.Edges.Any(e => !uses.ContainsKey(e.Id) || uses[e.Id].Count != 2 || uses[e.Id][0].Item2 == uses[e.Id][1].Item2 ||
                uses[e.Id][0].Item1==uses[e.Id][1].Item1||e.Faces.Count != 2))
                throw new InvalidDataException("NONMANIFOLD_OR_INCONSISTENT_SHELL_ORIENTATION");
        }
        private static List<ToolCurve> SliceShell(NativeEnvelopeBody shell, IDictionary<int, PlacementEdgeSnapshot> edges, BendWorkFrame frame, double z)
        {
            var result = new List<ToolCurve>();
            foreach (var face in shell.Faces)
            {
                var rim = face.Loops.SelectMany(l => l.Edges).Select(e => edges[e.EdgeId]).Distinct().ToArray();
                if (face.Kind == 1)
                {
                    if (Math.Abs(PlacementVector3.Dot(face.OutwardNormal, frame.BendAxis)) > .5) continue;
                    var points = rim.Where(e => Math.Min(frame.AxialDistance(e.A), frame.AxialDistance(e.B)) < z &&
                        Math.Max(frame.AxialDistance(e.A), frame.AxialDistance(e.B)) > z).Select(e => {
                            double t = (z - frame.AxialDistance(e.A)) / (frame.AxialDistance(e.B) - frame.AxialDistance(e.A));
                            return frame.Project(e.A + (e.B - e.A) * t);
                        }).ToArray();
                    var tangent = PlacementVector3.Cross(frame.BendAxis, face.OutwardNormal);
                    var xy = new ToolPoint(PlacementVector3.Dot(tangent, frame.X), PlacementVector3.Dot(tangent, frame.Y));
                    points = points.OrderBy(p => p.X * xy.X + p.Y * xy.Y).ToArray();
                    if ((points.Length & 1) != 0) throw new InvalidDataException("UNSUPPORTED_SLICE_TOPOLOGY");
                    for (int i = 0; i < points.Length; i += 2) result.Add(ToolCurve.Line(points[i], points[i + 1], new ToolTransform()));
                }
                else
                {
                    double lo = rim.Min(e => Math.Min(frame.AxialDistance(e.A), frame.AxialDistance(e.B)));
                    double hi = rim.Max(e => Math.Max(frame.AxialDistance(e.A), frame.AxialDistance(e.B)));
                    if (z <= lo || z >= hi) continue;
                    var edge = rim.First(e => e.Kind == 2); var arc = BendSheetSection.Project(edge, frame);
                    bool ccw = !face.ReversedSurfaceSense;
                    if ((arc.SweepRadians > 0) != ccw) arc = Reverse(arc);
                    result.Add(arc);
                }
            }
            return result;
        }
        private static ToolCurve Reverse(ToolCurve c) => c.Kind == "LINE" ? ToolCurve.Line(c.End, c.Start, new ToolTransform()) :
            ToolCurve.Arc(c.End, c.Start, c.Center, c.RadiusMm, -c.SweepRadians, new ToolTransform());
        private static double SignedTwiceArea(IEnumerable<ToolCurve> curves)
        {
            var loop=curves.ToArray();var origin=loop[0].Start;
            return loop.Sum(e=>e.Kind=="LINE"?(e.Start.X-origin.X)*(e.End.Y-origin.Y)-(e.Start.Y-origin.Y)*(e.End.X-origin.X):
                (e.Center.X-origin.X)*(e.End.Y-e.Start.Y)-(e.Center.Y-origin.Y)*(e.End.X-e.Start.X)+e.RadiusMm*e.RadiusMm*e.SweepRadians);
        }
        internal static CollisionMaterial2D OrientedMaterial(List<ToolCurve> source, EnvelopeBinding binding)
        {
            var remaining = new List<ToolCurve>(source); var contours = new List<Tuple<CollisionContour2D, double>>();
            while (remaining.Count > 0)
            {
                var loop = new List<ToolCurve> { remaining[0] }; remaining.RemoveAt(0);
                while (ToolPoint.Distance(loop.Last().End, loop[0].Start) > Joint)
                {
                    var next = remaining.Where(e => ToolPoint.Distance(e.Start, loop.Last().End) <= Joint).ToArray();
                    if (next.Length != 1) throw new InvalidDataException("OPEN_OR_AMBIGUOUS_NATIVE_SLICE");
                    loop.Add(next[0]); remaining.Remove(next[0]);
                }
                var contour = new CollisionContour2D("NATIVE_LOOP_" + contours.Count, loop, SourceToleranceMm);
                if (!contour.Valid) throw new InvalidDataException("UNSUPPORTED_SLICE_TOPOLOGY");
                double twice = SignedTwiceArea(loop);
                contours.Add(Tuple.Create(contour, twice));
            }
            var outers = contours.Where(c => c.Item2 > 0).ToArray(); var holes = contours.Where(c => c.Item2 < 0).ToArray();
            if (outers.Length == 0 || outers.Length + holes.Length != contours.Count) throw new InvalidDataException("INVERTED_OR_ZERO_AREA_NATIVE_SLICE");
            var assigned = new Dictionary<CollisionContour2D, List<CollisionContour2D>>();
            foreach (var o in outers) assigned[o.Item1] = new List<CollisionContour2D>();
            foreach (var h in holes)
            {
                var parent = outers.Where(o => ToolRegionValidator.ContainsPoint(o.Item1.Boundary, h.Item1.Boundary[0].Start)).OrderBy(o => o.Item2).FirstOrDefault();
                if (parent == null) throw new InvalidDataException("NATIVE_VOID_WITHOUT_MATERIAL_PARENT");
                assigned[parent.Item1].Add(h.Item1);
            }
            return new CollisionMaterial2D(binding.FrameId, outers.Select(o => new CollisionComponent2D(o.Item1, assigned[o.Item1])),
                binding.SourceSha256, binding.Model.SimulationOnly, () => binding.Current);
        }
    }
}
