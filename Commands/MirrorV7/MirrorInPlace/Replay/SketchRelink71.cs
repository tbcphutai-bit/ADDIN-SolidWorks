using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.MirrorV7.MirrorInPlace
{
    // A sketch whose geometry is driven by model edges/vertices (On Edge,
    // Offset Edge, Coincident, Distance ...) cannot be mirrored by moving its
    // points: its shape follows what it is attached to. It is mirrored by
    // re-attaching every such relation to the matching entity of a fixed,
    // reflected copy of the body it belongs to, inserted just before the
    // sketch. The sketch, its entities and its relations stay the same
    // objects, so a consumer such as a Lofted Bend keeps its profiles.
    // Native evidence (OnEdgeRelinkProbe71, four Lofted Bend Parts): sketch
    // geometry on the mirror image to 1e-11 m, Lofted Bend body to 1e-15 m.
    public static partial class RollbackReplayEngineV7
    {
        private static double[] Reflect71(ADDIN.Commands.PlaneData plane, double[] p)
        {
            double[] o = plane.Origin, n = plane.Normal;
            double norm = n[0] * n[0] + n[1] * n[1] + n[2] * n[2];
            double d = ((p[0] - o[0]) * n[0] + (p[1] - o[1]) * n[1] + (p[2] - o[2]) * n[2]) / norm;
            return new[] { p[0] - 2 * d * n[0], p[1] - 2 * d * n[1], p[2] - 2 * d * n[2] };
        }

        private static MathTransform ReflectionTransform71(ISldWorks app, ADDIN.Commands.PlaneData plane)
        {
            double[] o = plane.Origin, raw = plane.Normal;
            double length = Math.Sqrt(raw[0] * raw[0] + raw[1] * raw[1] + raw[2] * raw[2]);
            var n = new[] { raw[0] / length, raw[1] / length, raw[2] / length };
            double d = 2 * (n[0] * o[0] + n[1] * o[1] + n[2] * o[2]);
            return (MathTransform)((MathUtility)app.GetMathUtility()).CreateTransform(new[]
            {
                1 - 2 * n[0] * n[0], -2 * n[0] * n[1], -2 * n[0] * n[2],
                -2 * n[1] * n[0], 1 - 2 * n[1] * n[1], -2 * n[1] * n[2],
                -2 * n[2] * n[0], -2 * n[2] * n[1], 1 - 2 * n[2] * n[2],
                d * n[0], d * n[1], d * n[2], 1.0, 0, 0, 0
            });
        }

        private static double[][] CurveSamples71(Curve curve, double a, double b, int count)
        {
            return Enumerable.Range(0, count).Select(i =>
            {
                var p = (double[])curve.Evaluate2(a + (b - a) * i / (count - 1), 0);
                return new[] { p[0], p[1], p[2] };
            }).ToArray();
        }

        // Points along a sketch arc from its own start, centre, end and length. The
        // curve of a sketch arc is the whole circle and its end parameters need not
        // be those of the arc (natively half the circle of a 44 m radius arc: the
        // samples ran 139 m round the far side and every check against them failed
        // by the diameter). Null when the arc is a full circle or its plane cannot
        // be told from its three points.
        private static double[][] ArcSamples71(SketchSegment segment, int count)
        {
            // 3D sketches only: their point coordinates are model coordinates, as the samples are.
            var owner = segment.GetSketch() as Sketch;
            var arc = owner == null || !owner.Is3D() ? null : segment as SketchArc;
            var s = arc == null ? null : arc.GetStartPoint2() as SketchPoint;
            var e = arc == null ? null : arc.GetEndPoint2() as SketchPoint;
            var c = arc == null ? null : arc.GetCenterPoint2() as SketchPoint;
            if (s == null || e == null || c == null) return null;
            double[] u = { s.X - c.X, s.Y - c.Y, s.Z - c.Z }, v = { e.X - c.X, e.Y - c.Y, e.Z - c.Z };
            double radius = Math.Sqrt(u[0] * u[0] + u[1] * u[1] + u[2] * u[2]);
            double[] n = { u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0] };
            double cross = Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
            if (radius <= 0 || cross <= radius * radius * 1e-9) return null;
            double theta = segment.GetLength() / radius;
            double shortWay = Math.Atan2(cross, u[0] * v[0] + u[1] * v[1] + u[2] * v[2]);
            double sign = Math.Abs(theta - shortWay) <= Math.Abs(theta - (2 * Math.PI - shortWay)) ? 1 : -1;
            // In-plane direction a quarter turn from the start, on the side the arc runs to.
            double[] w = { sign * (n[1] * u[2] - n[2] * u[1]) / cross, sign * (n[2] * u[0] - n[0] * u[2]) / cross, sign * (n[0] * u[1] - n[1] * u[0]) / cross };
            return Enumerable.Range(0, count).Select(k =>
            {
                double t = theta * k / (count - 1);
                return new[] { c.X + u[0] * Math.Cos(t) + w[0] * Math.Sin(t), c.Y + u[1] * Math.Cos(t) + w[1] * Math.Sin(t), c.Z + u[2] * Math.Cos(t) + w[2] * Math.Sin(t) };
            }).ToArray();
        }

        private static double[][] SegmentSamples71(SketchSegment segment)
        {
            double[][] onArc = ArcSamples71(segment, 33);
            if (onArc != null) return onArc;
            var curve = (Curve)segment.GetCurve();
            double a, b; bool closed, periodic;
            if (curve == null || !curve.GetEndParams(out a, out b, out closed, out periodic))
                throw new InvalidOperationException("RELINK71 sketch segment has no curve.");
            return CurveSamples71(curve, a, b, 33);
        }

        private static Body2 OwnerBody71(object entity)
        {
            var edge = entity as Edge;
            if (edge != null) return edge.GetBody() as Body2;
            var vertex = entity as Vertex;
            var first = vertex == null ? null : ((vertex.GetEdges() as object[]) ?? new object[0]).OfType<Edge>().FirstOrDefault();
            return first == null ? null : first.GetBody() as Body2;
        }

        private sealed class ModelReference71 { internal int Relation, Entity; internal bool IsEdge; internal double[][] Geometry; }

        // What a sketch is attached to, read in the source state: every model
        // edge/vertex a relation points at (as reflected geometry), where every
        // sketch point and segment has to end up, and - when asked - a reflected
        // copy of every body those edges belong to.
        private sealed class SketchLink71
        {
            internal int Relations;
            internal readonly List<double[][]> Segments = new List<double[][]>();
            internal readonly List<ModelReference71> References = new List<ModelReference71>();
            internal readonly Dictionary<string, double[]> Points = new Dictionary<string, double[]>(StringComparer.Ordinal);
            internal readonly Dictionary<string, Body2> FixedBodies = new Dictionary<string, Body2>(StringComparer.Ordinal);
            internal readonly List<Body2> FixedEdgeSheets = new List<Body2>();
        }

        // An On Edge relation whose edge is in another document (in-context) has
        // nothing local to copy: the sketch segment itself is the only record of
        // that edge. A narrow fixed sheet is built on the reflected curve of the
        // segment (extruded 1 mm along the principal axis least along the curve);
        // one of its edges is that curve. Native evidence (InContextEdgeProbe71,
        // a 2918 mm spline): edge on the mirror image to 0, relation re-attached,
        // segment on the mirror image to 8e-12 m, external reference gone.
        private static Body2 FixedEdgeSheet71(ISldWorks app, SketchSegment segment, MathTransform reflection, string sketchName, out double[][] geometry)
        {
            var source = (Curve)segment.GetCurve();
            var curve = source == null ? null : source.Copy() as Curve;
            if (curve == null) throw new InvalidOperationException("RELINK71 in-context segment has no curve: " + sketchName);
            curve.ApplyTransform(reflection);
            double a, b; bool closed, periodic;
            if (!curve.GetEndParams(out a, out b, out closed, out periodic) || closed)
                throw new InvalidOperationException("RELINK71 in-context segment is closed or unbounded: " + sketchName);
            double[][] samples = CurveSamples71(curve, a, b, 33);
            geometry = Enumerable.Range(0, 9).Select(i => samples[i * 4]).ToArray();
            var along = new double[3];
            for (int i = 0; i < 32; i++)
            {
                double[] t = { samples[i + 1][0] - samples[i][0], samples[i + 1][1] - samples[i][1], samples[i + 1][2] - samples[i][2] };
                double length = Math.Sqrt(t[0] * t[0] + t[1] * t[1] + t[2] * t[2]);
                if (length <= 0) continue;
                for (int axis = 0; axis < 3; axis++) along[axis] = Math.Max(along[axis], Math.Abs(t[axis]) / length);
            }
            int least = Enumerable.Range(0, 3).OrderBy(axis => along[axis]).First();
            if (along[least] > 0.9) throw new InvalidOperationException("RELINK71 no extrusion direction for the in-context edge of " + sketchName);
            var direction = new double[3]; direction[least] = 1;
            var modeler = (Modeler)app.GetModeler();
            var surface = modeler.CreateExtrusionSurface(curve, direction) as Surface;
            double[] range = surface == null ? null : surface.Parameterization() as double[];
            Body2 sheet = range == null || range.Length < 4 ? null : modeler.CreateSheetFromSurface(surface, new[] { range[0], range[1], 0.0, 0.001 }) as Body2;
            double[][] wanted = geometry;
            if (sheet == null || !((sheet.GetEdges() as object[]) ?? new object[0]).Cast<Edge>().Any(e => EdgeResidual71(e, wanted) <= 1e-7))
                throw new InvalidOperationException("RELINK71 cannot build the fixed edge for the in-context relation of " + sketchName);
            return sheet;
        }

        // Throws for what cannot be re-attached: a face reference, an Offset Edge
        // relation whose edge is in another document, and - when no fixed copies
        // are asked for - an On Edge relation of that kind.
        private static SketchLink71 ReadSketchLink71(ISldWorks app, Sketch sketch, string sketchName, Func<double[], double[]> reflect, MathTransform fixedCopyReflection)
        {
            var link = new SketchLink71();
            var own = new HashSet<string>(((sketch.GetSketchSegments() as object[]) ?? new object[0]).Cast<SketchSegment>()
                .Select(s => string.Join(":", (int[])s.GetID())), StringComparer.Ordinal);
            link.Segments.AddRange(((sketch.GetSketchSegments() as object[]) ?? new object[0]).Cast<SketchSegment>()
                .Select(s => SegmentSamples71(s).Select(reflect).ToArray()));
            object[] relations = sketch.RelationManager == null ? new object[0]
                : sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll) as object[] ?? new object[0];
            link.Relations = relations.Length;
            for (int r = 0; r < relations.Length; r++)
            {
                var relation = (SketchRelation)relations[r];
                object[] definition = relation.GetDefinitionEntities2() as object[] ?? new object[0];
                int relationType = relation.GetRelationType();
                // The distance of an offset is a relation of its own without any entity.
                if (relationType == (int)swConstraintType_e.swConstraintType_OFFSETEDGE && definition.Length == 0) continue;

                for (int k = 0; k < definition.Length; k++)
                {
                    var edge = definition[k] as Edge; var vertex = definition[k] as Vertex;
                    if (definition[k] is Face2) throw new InvalidOperationException("RELINK71 face reference not supported yet: " + sketchName);
                    if (edge == null && vertex == null) continue;
                    if (fixedCopyReflection != null)
                    {
                        Body2 body = OwnerBody71(definition[k]);
                        if (body == null) throw new InvalidOperationException("RELINK71 reference without a body: " + sketchName);
                        if (!link.FixedBodies.ContainsKey(body.Name))
                        {
                            var copy = (Body2)body.Copy();
                            // Negate(): a reflection turns the body inside out and reverses
                            // its edge directions; without it a trimmed converted edge kept
                            // the wrong end (native: 12 mm left of a 2931 mm spline).
                            if (copy == null || !copy.ApplyTransform(fixedCopyReflection) || !copy.Negate())
                                throw new InvalidOperationException("RELINK71 cannot reflect the reference body of " + sketchName);
                            link.FixedBodies[body.Name] = copy;
                        }
                    }
                    double[][] geometry;
                    if (edge != null)
                    {
                        // Through the edge's own evaluator: on a circular edge the parameters
                        // of the edge and of its curve are not one space (natively samples
                        // taken on the curve lay 1.45 m off the arc).
                        geometry = ADDIN.Helpers.SketchOperationsHelper.SampleEdge24(edge).Select(reflect).ToArray();
                    }
                    else geometry = new[] { reflect((double[])vertex.GetPoint()) };
                    link.References.Add(new ModelReference71 { Relation = r, Entity = k, IsEdge = edge != null, Geometry = geometry });
                }
                // An On Edge / Offset Edge relation whose edge cannot be read points
                // outside this document (in-context): there is nothing local to copy.
                int type = relation.GetRelationType();
                if ((type != (int)swConstraintType_e.swConstraintType_USEEDGE && type != (int)swConstraintType_e.swConstraintType_OFFSETEDGE) ||
                    definition.Any(e => e is Edge)) continue;
                // The entity list then holds the sketch's own segment and a stand-in for the foreign edge.
                object[] entities = relation.GetEntities() as object[] ?? new object[0];
                Func<object, bool> isOwn = e => e is SketchSegment && own.Contains(string.Join(":", (int[])((SketchSegment)e).GetID()));
                int standIn = Enumerable.Range(0, entities.Length).Where(i => entities[i] is SketchSegment && !isOwn(entities[i])).DefaultIfEmpty(-1).First();
                var driven = entities.Where(isOwn).Cast<SketchSegment>().FirstOrDefault();
                if (type != (int)swConstraintType_e.swConstraintType_USEEDGE || fixedCopyReflection == null || entities.Length != 2 || standIn < 0 || driven == null)
                    throw new InvalidOperationException("RELINK71 the edge of an On Edge relation is in another document (in-context): " + sketchName);
                double[][] foreign;
                link.FixedEdgeSheets.Add(FixedEdgeSheet71(app, driven, fixedCopyReflection, sketchName, out foreign));
                link.References.Add(new ModelReference71 { Relation = r, Entity = standIn, IsEdge = true, Geometry = foreign });
            }
            foreach (SketchPoint point in ((sketch.GetSketchPoints2() as object[]) ?? new object[0]).Cast<SketchPoint>())
            {
                var id = (int[])point.GetID();
                link.Points[id[0] + ":" + id[1]] = reflect(new[] { point.X, point.Y, point.Z });
            }
            return link;
        }

        // How far an edge is from recorded edge geometry, whatever its
        // parameterisation: both ends (either order) and every sample on the edge.
        private static double EdgeResidual71(Edge edge, double[][] geometry)
        {
            var range = edge.GetCurveParams2() as double[];
            if (range == null || range.Length < 8) return double.PositiveInfinity;
            double[] a = { range[0], range[1], range[2] }, b = { range[3], range[4], range[5] };
            double[] first = geometry[0], last = geometry[geometry.Length - 1];
            double worst = Math.Min(Math.Max(Distance(a, first), Distance(b, last)), Math.Max(Distance(a, last), Distance(b, first)));
            foreach (double[] p in geometry)
            {
                var near = edge.GetClosestPointOn(p[0], p[1], p[2]) as double[];
                if (near == null || near.Length < 3) return double.PositiveInfinity;
                worst = Math.Max(worst, Distance(p, new[] { near[0], near[1], near[2] }));
            }
            return worst;
        }

        // Edits the sketch: every recorded reference is re-attached to the entity
        // with the same reflected geometry among 'candidates' (null: the bodies of
        // the model as the sketch sees them), then every point is put on its
        // reflected position. The sketch, its entities and its relations stay the
        // same objects.
        private static void ReattachSketchLink71(ModelDoc2 model, string sketchName, SketchLink71 link, List<Body2> candidates, double tolerance,
            out int replaced, out int moved, out int dimensionsReleased)
        {
            var part = (PartDoc)model;
            var feature = part.FeatureByName(sketchName) as Feature;
            model.ClearSelection2(true);
            if (feature == null || !feature.Select2(false, 0)) throw new InvalidOperationException("RELINK71 cannot select " + sketchName);
            model.EditSketch();
            var sketch = model.SketchManager.ActiveSketch as Sketch;
            if (sketch == null) throw new InvalidOperationException("RELINK71 cannot edit " + sketchName);
            replaced = 0; moved = 0;
            var released = new List<Dimension>();
            // Re-attachment relies on SOLIDWORKS solving the sketch as each reference
            // is exchanged. Both settings belong to the session, not to this Part:
            // natively, after another Part had been left in sketch edit, the points
            // stayed behind and were then put 24 mm off (2026-10-09).
            bool autoSolve = model.SketchManager.AutoSolve, addToDb = model.SketchManager.AddToDB;
            MirrorV7Diagnostics.Log("[RELINK71][EDIT_STATE] sketch=" + sketchName + " autoSolve=" + autoSolve + " addToDB=" + addToDb +
                " sketchAutomaticSolve=" + sketch.GetAutomaticSolve());
            try
            {
                if (!autoSolve) model.SketchManager.AutoSolve = true;
                if (addToDb) model.SketchManager.AddToDB = false;
                if (!sketch.GetAutomaticSolve()) sketch.SetAutomaticSolve(true);
                if (candidates == null)
                    candidates = (part.GetBodies2((int)swBodyType_e.swAllBodies, false) as object[] ?? new object[0]).Cast<Body2>().ToList();
                object[] live = sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll) as object[] ?? new object[0];
                if (live.Length != link.Relations) throw new InvalidOperationException("RELINK71 relation list changed while editing " + sketchName);
                // A driving dimension to a vertex has two solutions along an edge and
                // takes the nearest while its references are being exchanged.
                foreach (SketchRelation relation in live.Cast<SketchRelation>())
                {
                    var display = relation.GetDisplayDimension() as DisplayDimension;
                    var dimension = display == null ? null : display.GetDimension2(0) as Dimension;
                    if (dimension == null || dimension.DrivenState == (int)swDimensionDrivenState_e.swDimensionDriven) continue;
                    dimension.DrivenState = (int)swDimensionDrivenState_e.swDimensionDriven;
                    released.Add(dimension);
                }
                foreach (ModelReference71 item in link.References)
                {
                    object target = null; double best = double.PositiveInfinity;
                    foreach (Body2 body in candidates)
                    {
                        if (item.IsEdge)
                            foreach (Edge edge in ((body.GetEdges() as object[]) ?? new object[0]).Cast<Edge>())
                            {
                                double d = EdgeResidual71(edge, item.Geometry);
                                if (d < best) { best = d; target = edge; }
                            }
                        else
                            foreach (Vertex vertex in ((body.GetVertices() as object[]) ?? new object[0]).Cast<Vertex>())
                            {
                                double d = Distance((double[])vertex.GetPoint(), item.Geometry[0]);
                                if (d < best) { best = d; target = vertex; }
                            }
                    }
                    if (target == null || best > tolerance)
                        throw new InvalidOperationException("RELINK71 no reflected counterpart for a reference of " + sketchName + " residual_m=" + best.ToString("E2"));
                    var relation = (SketchRelation)live[item.Relation];
                    // The entity list holds a sketch-side stand-in at the position of
                    // the model entity in the definition list; that stand-in is what
                    // ReplaceEntity accepts (edge-to-edge is refused natively).
                    object[] standIns = relation.GetEntities() as object[] ?? new object[0];
                    if (item.Entity >= standIns.Length || !relation.ReplaceEntity(standIns[item.Entity], target))
                        throw new InvalidOperationException("RELINK71 SOLIDWORKS refused to re-attach a relation of " + sketchName);
                    replaced++;
                }
                int forced = 0;
                for (int pass = 0; pass < 5; pass++)
                {
                    double worstPoint = 0, worstBefore = 0; int refused = 0, movedBefore = moved;
                    foreach (SketchPoint point in ((sketch.GetSketchPoints2() as object[]) ?? new object[0]).Cast<SketchPoint>())
                    {
                        var id = (int[])point.GetID(); double[] goal;
                        if (!link.Points.TryGetValue(id[0] + ":" + id[1], out goal)) continue;
                        double off = Distance(new[] { point.X, point.Y, point.Z }, goal);
                        worstBefore = Math.Max(worstBefore, off);
                        if (off > 1e-9)
                        {
                            if (!point.SetCoords(goal[0], goal[1], goal[2])) refused++;
                            moved++; off = Distance(new[] { point.X, point.Y, point.Z }, goal);
                        }
                        worstPoint = Math.Max(worstPoint, off);
                    }
                    if (moved > movedBefore)
                        MirrorV7Diagnostics.Log("[RELINK71][POINTS] sketch=" + sketchName + " pass=" + pass + " moved=" + (moved - movedBefore) + " refused=" + refused +
                            " farthestBefore_m=" + worstBefore.ToString("E2") + " farthestAfter_m=" + worstPoint.ToString("E2"));
                    if (worstPoint <= 1e-9) break;
                    // A point SOLIDWORKS will not move and that is not where its
                    // re-attached reference puts it: the sketch has not been solved
                    // since the references were exchanged. Natively this happens in
                    // a Part whose profiles lie 44 m from the origin and in the Part
                    // mirrored after it in the same session (2026-10-09: two points
                    // stayed at their unreflected place, the free end was then put on
                    // the stale curve and ended 24 mm off). Leaving the sketch solves
                    // it; the points are placed again after that. Points that still do
                    // not move then are not the sketch's to place (the far point of a
                    // converted 44 m arc); the geometry check that follows decides.
                    if (forced >= 2 && refused == moved - movedBefore) break;
                    if (refused == 0 || forced >= 2) continue;
                    if (forced == 0) { sketch.SetAutomaticSolve(false); sketch.SetAutomaticSolve(true); }
                    else
                    {
                        model.SketchManager.InsertSketch(true);
                        model.ClearSelection2(true);
                        feature = part.FeatureByName(sketchName) as Feature;
                        if (feature == null || !feature.Select2(false, 0)) throw new InvalidOperationException("RELINK71 cannot select " + sketchName + " again");
                        model.EditSketch();
                        sketch = model.SketchManager.ActiveSketch as Sketch;
                        if (sketch == null) throw new InvalidOperationException("RELINK71 cannot edit " + sketchName + " again");
                    }
                    MirrorV7Diagnostics.Log("[RELINK71][SOLVE_FORCED] sketch=" + sketchName + " how=" + (forced == 0 ? "SOLVER_OFF_ON" : "SKETCH_LEFT_AND_ENTERED") +
                        " pointsNotMoved=" + refused);
                    forced++;
                }
            }
            finally
            {
                foreach (Dimension dimension in released) dimension.DrivenState = (int)swDimensionDrivenState_e.swDimensionDriving;
                model.SketchManager.InsertSketch(true);
                if (addToDb) model.SketchManager.AddToDB = true;
                if (!autoSolve) model.SketchManager.AutoSolve = false;
            }
            dimensionsReleased = released.Count;
        }

        // Farthest point of the recorded mirror image from the sketch as it is now.
        private static double SketchDeviation71(Sketch sketch, SketchLink71 link, string sketchName)
        {
            var after = ((sketch.GetSketchSegments() as object[]) ?? new object[0]).Cast<SketchSegment>().ToList();
            if (after.Count != link.Segments.Count) throw new InvalidOperationException("RELINK71 segment count changed in " + sketchName);
            double deviation = 0;
            for (int k = 0; k < after.Count; k++)
            {
                var curve = (Curve)after[k].GetCurve();
                foreach (double[] p in link.Segments[k])
                {
                    var q = (double[])curve.GetClosestPointOn(p[0], p[1], p[2]);
                    deviation = Math.Max(deviation, Distance(p, new[] { q[0], q[1], q[2] }));
                }
                // Both ends as well: a trimmed curve can lie on the right curve and still be the wrong piece.
                double[][] now = SegmentSamples71(after[k]);
                deviation = Math.Max(deviation, Math.Min(
                    Math.Max(Distance(now[0], link.Segments[k][0]), Distance(now[32], link.Segments[k][32])),
                    Math.Max(Distance(now[0], link.Segments[k][32]), Distance(now[32], link.Segments[k][0]))));
            }
            return deviation;
        }

        // Mirrors one sketch by re-attaching its model references to fixed,
        // reflected copies of the bodies they belong to, inserted just before the
        // sketch: for a sketch that hangs from geometry the mirror leaves in place
        // (reference surfaces of a Lofted Bend profile). Returns false when the
        // sketch has no reference to a model edge or vertex (nothing to re-attach:
        // not this method's case). Throws when a reference cannot be read or
        // re-attached, or when the result is not the mirror image of the source
        // sketch within 1e-7 m.
        internal static bool RelinkSketchToReflectedGeometry71(ISldWorks app, ModelDoc2 model, string sketchName, ADDIN.Commands.PlaneData plane)
        {
            var part = (PartDoc)model;
            if (!model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, sketchName))
                throw new InvalidOperationException("RELINK71 cannot roll back after " + sketchName);
            var feature = part.FeatureByName(sketchName) as Feature;
            var sketch = feature == null ? null : feature.GetSpecificFeature2() as Sketch;
            if (sketch == null) throw new InvalidOperationException("RELINK71 sketch not found: " + sketchName);
            SketchLink71 link = ReadSketchLink71(app, sketch, sketchName, p => Reflect71(plane, p), ReflectionTransform71(app, plane));
            if (link.References.Count == 0) return false;

            if (!model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, sketchName))
                throw new InvalidOperationException("RELINK71 cannot roll back before " + sketchName);
            var importedNames = new List<string>();
            foreach (Body2 body in link.FixedBodies.Values.Concat(link.FixedEdgeSheets))
            {
                var imported = part.CreateFeatureFromBody3(body, false, (int)swCreateFeatureBodyOpts_e.swCreateFeatureBodyCheck) as Feature;
                if (imported == null) throw new InvalidOperationException("RELINK71 cannot insert the fixed reference surface before " + sketchName);
                importedNames.Add(imported.Name);
                FrozenSurfaceImports71.Add(imported.Name);
            }
            if (!model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, sketchName))
                throw new InvalidOperationException("RELINK71 cannot roll forward after " + sketchName);
            var candidates = importedNames.Select(n => part.FeatureByName(n) as Feature)
                .Select(f => f == null ? null : f.GetFaces() as object[]).Where(f => f != null && f.Length > 0)
                .Select(f => (Body2)((Face2)f[0]).GetBody()).ToList();

            int replaced, moved, released;
            ReattachSketchLink71(model, sketchName, link, candidates, 1e-7, out replaced, out moved, out released);
            model.EditRebuild3();
            feature = part.FeatureByName(sketchName) as Feature;
            double deviation = SketchDeviation71(feature.GetSpecificFeature2() as Sketch, link, sketchName);
            bool warning;
            int error = feature.GetErrorCode2(out warning);
            MirrorV7Diagnostics.Log("[RELINK71][SKETCH] sketch=" + sketchName + " relations=" + link.Relations + " reattached=" + replaced +
                " fixedBodies=" + importedNames.Count + " inContextEdges=" + link.FixedEdgeSheets.Count + " dimensionsReleased=" + released + " pointMoves=" + moved +
                " geometryVsMirror_m=" + deviation.ToString("E2") + " error=" + error + " externalReferences=" + feature.ListExternalFileReferencesCount());
            if ((error != 0 && !warning) || deviation > 1e-7)
                throw new InvalidOperationException("RELINK71 " + sketchName + " is not the mirror image after re-attachment. deviation_m=" +
                    deviation.ToString("E2") + " error=" + error);
            ADDIN.Helpers.NativeBodyCoincidence67.SurfaceChainResidualAllowed71 = true;
            return true;
        }

        // A sketch driven by edges of the Part's own body (for example the Offset
        // Edge path of a Curve Pattern): once the features above it are mirrored,
        // those edges are other entities. Its references are recorded before any
        // mutation and re-attached, on demand, to the mirrored edges themselves, so
        // the sketch keeps following the Part.
        private static readonly Dictionary<string, SketchLink71> sketchLinks71 = new Dictionary<string, SketchLink71>(StringComparer.Ordinal);

        private static void CaptureSketchLink71(string sketchName, Sketch sketch, Func<double[], double[]> reflect)
        {
            try
            {
                SketchLink71 link = ReadSketchLink71(null, sketch, sketchName, reflect, null);
                // Not with an Offset Entities relation in it: re-attached and with its
                // points sent to their own reflections such a figure ended tens of metres
                // out of shape (native: 62 m and 88 m). It keeps the older ways.
                bool offset = (sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll) as object[] ?? new object[0])
                    .Cast<SketchRelation>().Any(r => r.GetRelationType() == (int)swConstraintType_e.swConstraintType_OFFSETEDGE);
                if (link.References.Count > 0 && !offset) sketchLinks71[sketchName] = link;
                MirrorV7Diagnostics.Log("[RELINK71][CAPTURE] sketch=" + sketchName + " relations=" + link.Relations + " modelReferences=" + link.References.Count);
            }
            catch (InvalidOperationException ex)
            {
                MirrorV7Diagnostics.Log("[RELINK71][CAPTURE_SKIPPED] sketch=" + sketchName + " reason=" + ex.Message);
            }
        }

        private static bool TryRelinkToMirroredModel71(ModelDoc2 model, string sketchName)
        {
            SketchLink71 link;
            if (!sketchLinks71.TryGetValue(sketchName, out link)) return false;
            int replaced, moved, released;
            ReattachSketchLink71(model, sketchName, link, null, MatchTolerance71, out replaced, out moved, out released);
            var feature = ((PartDoc)model).FeatureByName(sketchName) as Feature;
            double deviation = SketchDeviation71(feature.GetSpecificFeature2() as Sketch, link, sketchName);
            bool warning;
            int error = feature.GetErrorCode2(out warning);
            MirrorV7Diagnostics.Log("[RELINK71][MIRRORED_MODEL] sketch=" + sketchName + " relations=" + link.Relations + " reattached=" + replaced +
                " dimensionsReleased=" + released + " pointMoves=" + moved + " geometryVsMirror_m=" + deviation.ToString("E2") + " error=" + error);
            if ((error != 0 && !warning) || deviation > MatchTolerance71)
                throw new InvalidOperationException("RELINK71 " + sketchName + " is not the mirror image after re-attachment to the mirrored model. deviation_m=" +
                    deviation.ToString("E2") + " error=" + error);
            return true;
        }

        // Name of the Lofted Bend that is the base feature of the current run, else null.
        internal static string LoftedBendBaseName71;

        internal static Feature FindLoftedBendBase71(ModelDoc2 model)
        {
            for (var feature = model.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
                if (feature.GetTypeName2() == "LoftedBend" && !feature.IsSuppressed()) return feature;
            return null;
        }

        private static double FarthestVertex71(ModelDoc2 model, Body2 expected)
        {
            object[] solids = ((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, true) as object[] ?? new object[0];
            if (solids.Length != 1) return double.PositiveInfinity;
            var a = ((expected.GetVertices() as object[]) ?? new object[0]).Cast<Vertex>().Select(v => (double[])v.GetPoint()).ToList();
            var b = ((((Body2)solids[0]).GetVertices() as object[]) ?? new object[0]).Cast<Vertex>().Select(v => (double[])v.GetPoint()).ToList();
            if (a.Count == 0 || a.Count != b.Count) return double.PositiveInfinity;
            return a.Max(p => b.Min(q => Distance(p, q)));
        }

        // One curve of a profile sketch as it has to lie in the mirrored Part: 65
        // points of the source curve, reflected.
        private sealed class ProfileCurve71 { internal int Type; internal bool Construction; internal double[][] Samples; internal double[] Centre; internal double Length; }

        private static List<ProfileCurve71> ReadProfileCurves71(ModelDoc2 model, string sketchName, ADDIN.Commands.PlaneData plane)
        {
            if (!model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, sketchName))
                throw new InvalidOperationException("LOFT71 cannot roll back after " + sketchName);
            var feature = ((PartDoc)model).FeatureByName(sketchName) as Feature;
            var sketch = feature == null ? null : feature.GetSpecificFeature2() as Sketch;
            if (sketch == null) throw new InvalidOperationException("LOFT71 profile sketch not found: " + sketchName);
            var result = new List<ProfileCurve71>();
            // A 3D sketch has model coordinates; a 2D profile would need its own frame and is left to re-attachment.
            if (!sketch.Is3D()) return result;
            foreach (SketchSegment segment in (sketch.GetSketchSegments() as object[] ?? new object[0]).Cast<SketchSegment>())
            {
                var curve = (Curve)segment.GetCurve();
                double a, b; bool closed, periodic;
                if (curve == null || !curve.GetEndParams(out a, out b, out closed, out periodic))
                    throw new InvalidOperationException("LOFT71 profile curve cannot be read: " + sketchName);
                var arc = segment as SketchArc;
                var centre = arc == null ? null : arc.GetCenterPoint2() as SketchPoint;
                result.Add(new ProfileCurve71 { Type = segment.GetType(), Construction = segment.ConstructionGeometry, Length = segment.GetLength(),
                    Centre = centre == null ? null : Reflect71(plane, new[] { centre.X, centre.Y, centre.Z }),
                    Samples = (ArcSamples71(segment, 65) ?? CurveSamples71(curve, a, b, 65)).Select(p => Reflect71(plane, p)).ToArray() });
            }
            return result;
        }

        // The way the user asked for when a profile cannot be re-attached
        // (2026-10-08, a profile taken from a swept surface that came out 88 m
        // away): its curves are removed and drawn again at their mirror image - a
        // line as a line, an arc as an arc, any other curve as a spline through 33
        // points of the reflected source curve. The Lofted Bend keeps its profile,
        // which is the sketch and not the curve; the curve no longer follows the
        // surface it was taken from. Returned: how far the farthest of the 65
        // points of every reflected source curve lies from the new curves (every
        // second one lies between two points the spline was drawn through).
        private static double RedrawProfileSketch3D71(ISldWorks app, ModelDoc2 model, string sketchName, List<ProfileCurve71> curves, double limit)
        {
            if (curves == null || curves.Count == 0)
                throw new InvalidOperationException("LOFT71 profile " + sketchName + " cannot be drawn again: it is not a 3D sketch with curves.");
            var part = (PartDoc)model;
            double deviation = double.PositiveInfinity;
            foreach (bool naturalEnds in new[] { false, true })
            {
                if (!model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, sketchName))
                    throw new InvalidOperationException("LOFT71 cannot roll back after " + sketchName);
                var feature = part.FeatureByName(sketchName) as Feature;
                model.ClearSelection2(true);
                if (feature == null || !feature.Select2(false, 0)) throw new InvalidOperationException("LOFT71 cannot select " + sketchName);
                model.EditSketch();
                Sketch active = model.SketchManager.ActiveSketch;
                if (active == null || !active.Is3D()) throw new InvalidOperationException("LOFT71 cannot edit the 3D profile " + sketchName);
                bool addToDb = model.SketchManager.AddToDB;
                bool relations = app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations);
                bool inference = app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference);
                try
                {
                    app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations, false);
                    app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference, false);
                    model.SketchManager.AddToDB = true;
                    model.ClearSelection2(true);
                    object[] existing = active.GetSketchSegments() as object[] ?? new object[0];
                    foreach (SketchSegment segment in existing.Cast<SketchSegment>())
                        if (!segment.Select4(true, null)) throw new InvalidOperationException("LOFT71 cannot select every curve of " + sketchName);
                    if (existing.Length > 0 && !model.Extension.DeleteSelection2(0))
                        throw new InvalidOperationException("LOFT71 the curves of " + sketchName + " could not be removed.");
                    if ((active.GetSketchSegments() as object[] ?? new object[0]).Length != 0)
                        throw new InvalidOperationException("LOFT71 curves remain in " + sketchName + " after removal.");
                    var keptIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (ProfileCurve71 curve in curves)
                    {
                        double[] first = curve.Samples[0], middle = curve.Samples[32], last = curve.Samples[64];
                        SketchSegment created;
                        if (curve.Type == (int)swSketchSegments_e.swSketchLINE)
                            created = model.SketchManager.CreateLine(first[0], first[1], first[2], last[0], last[1], last[2]) as SketchSegment;
                        else if (curve.Type == (int)swSketchSegments_e.swSketchARC)
                        {
                            created = model.SketchManager.Create3PointArc(first[0], first[1], first[2], last[0], last[1], last[2], middle[0], middle[1], middle[2]) as SketchSegment;
                            // Three points almost in a line are refused as an arc. Drawing
                            // it from its centre is not used here: in a plane that is
                            // not parallel to a principal one that arc came out with the
                            // right length and 0.17 mm away, and a second arc was left
                            // in the sketch.
                        }
                        else created = null;
                        // Any other curve, and an arc SOLIDWORKS does not draw: a
                        // spline through the points; the check below judges it.
                        if (created == null && curve.Type != (int)swSketchSegments_e.swSketchLINE)
                            created = model.SketchManager.CreateSpline2(
                                Enumerable.Range(0, 33).SelectMany(i => curve.Samples[2 * i].Take(3)).ToArray(), naturalEnds) as SketchSegment;
                        if (created == null) throw new InvalidOperationException("LOFT71 a curve of " + sketchName + " could not be drawn again (type " + curve.Type + ").");
                        created.ConstructionGeometry = curve.Construction;
                        keptIds.Add(string.Join(":", (int[])created.GetID()));
                    }
                    // A refused arc can leave a curve behind although the call returned
                    // nothing (natively two curves in the sketch for one drawn): what
                    // was not handed back by a successful call is removed.
                    var strays = (active.GetSketchSegments() as object[] ?? new object[0]).Cast<SketchSegment>()
                        .Where(s => !keptIds.Contains(string.Join(":", (int[])s.GetID()))).ToList();
                    if (strays.Count > 0)
                    {
                        string what = string.Join(" ", strays.Select(s => "type" + s.GetType() + ":len_mm=" + (s.GetLength() * 1000).ToString("F3")));
                        model.ClearSelection2(true);
                        foreach (SketchSegment stray in strays) stray.Select4(true, null);
                        bool removed = model.Extension.DeleteSelection2(0);
                        MirrorV7Diagnostics.Log("[LOFT71][PROFILE_REDRAW_STRAYS] sketch=" + sketchName + " strays=" + strays.Count + " [" + what + "] removed=" + removed +
                            " left=" + (active.GetSketchSegments() as object[] ?? new object[0]).Length);
                    }
                }
                finally
                {
                    model.SketchManager.AddToDB = addToDb;
                    app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations, relations);
                    app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference, inference);
                    model.ClearSelection2(true);
                    model.SketchManager.Insert3DSketch(true);
                }
                model.EditRebuild3();
                feature = part.FeatureByName(sketchName) as Feature;
                var sketch = feature == null ? null : feature.GetSpecificFeature2() as Sketch;
                if (sketch == null) throw new InvalidOperationException("LOFT71 profile lost after it was drawn again: " + sketchName);
                var drawn = (sketch.GetSketchSegments() as object[] ?? new object[0]).Cast<SketchSegment>().Select(s => (Curve)s.GetCurve()).ToList();
                if (drawn.Count != curves.Count)
                    throw new InvalidOperationException("LOFT71 profile " + sketchName + " has " + drawn.Count + " curves after it was drawn again; " + curves.Count + " expected.");
                deviation = 0;
                foreach (ProfileCurve71 curve in curves)
                    foreach (double[] p in curve.Samples)
                        deviation = Math.Max(deviation, drawn.Min(c =>
                        {
                            var q = c == null ? null : c.GetClosestPointOn(p[0], p[1], p[2]) as double[];
                            return q == null || q.Length < 3 ? double.PositiveInfinity : Distance(p, new[] { q[0], q[1], q[2] });
                        }));
                MirrorV7Diagnostics.Log("[LOFT71][PROFILE_REDRAW] sketch=" + sketchName + " naturalEnds=" + naturalEnds + " curves=" + drawn.Count + "/" + curves.Count +
                    " deviation_m=" + deviation.ToString("E2"));
                if (deviation <= limit) break;
            }
            return deviation;
        }

        // Base feature = Lofted Bend. Its profile sketches are mirrored by
        // re-attachment; a reflection exchanges the two sides of the sheet, so
        // the thickness direction is the one native option that may have to
        // change, decided against the reflected source body, never assumed.
        internal static void ReflectLoftedBendBase71(ISldWorks app, ModelDoc2 model, string loftName, ADDIN.Commands.PlaneData plane, Body2 expectedBase)
        {
            var part = (PartDoc)model;
            var loft = part.FeatureByName(loftName) as Feature;
            var data = loft == null ? null : loft.GetDefinition() as ILoftedBendsFeatureData;
            if (data == null || !data.AccessSelections(model, null)) throw new InvalidOperationException("LOFT71 cannot read the Lofted Bend profiles.");
            List<string> profiles;
            try
            {
                object[] raw = data.Profiles as object[] ?? new object[0];
                if (raw.Length == 0 || raw.Any(p => !(p is Feature))) throw new InvalidOperationException("LOFT71 Lofted Bend profile is not a sketch feature.");
                profiles = raw.Cast<Feature>().Select(f => f.Name).ToList();
            }
            finally { data.ReleaseSelectionAccess(); }
            // A profile whose curve was drawn again is the mirror image only within
            // the accepted local deviation; the corner test follows.
            double cornerLimit = 1e-7;
            foreach (string profile in profiles)
            {
                List<ProfileCurve71> curves = ReadProfileCurves71(model, profile, plane);   // before anything is changed
                string refused = null;
                try { if (!RelinkSketchToReflectedGeometry71(app, model, profile, plane)) refused = "not driven by model edges"; }
                catch (InvalidOperationException ex)
                {
                    if (IsUnsafeNativeFailure66(ex)) throw;
                    refused = ex.Message;
                }
                if (refused == null) continue;
                double limit = ADDIN.Helpers.NativeBodyCoincidence67.LocalDeviationLimit71;
                double deviation = RedrawProfileSketch3D71(app, model, profile, curves, limit);
                MirrorV7Diagnostics.Log("[LOFT71][PROFILE_DRAWN_AGAIN] sketch=" + profile + " curves=" + curves.Count + " deviation_m=" + deviation.ToString("E2") +
                    " limit_m=" + limit.ToString("E0") + " reattachment=" + refused);
                if (deviation > limit)
                    throw new InvalidOperationException("LOFT71 profile " + profile + " drawn again is not the mirror image. deviation_m=" + deviation.ToString("E2") +
                        " (re-attachment: " + refused + ")");
                ADDIN.Helpers.NativeBodyCoincidence67.LargestAcceptedDeviation71 = Math.Max(ADDIN.Helpers.NativeBodyCoincidence67.LargestAcceptedDeviation71, deviation);
                ADDIN.Helpers.NativeBodyCoincidence67.SurfaceChainResidualAllowed71 = true;
                cornerLimit = limit;
            }
            if (!model.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, loftName))
                throw new InvalidOperationException("LOFT71 cannot roll forward after " + loftName);
            model.EditRebuild3();
            double off = FarthestVertex71(model, expectedBase);
            bool flipped = false;
            if (off > cornerLimit)
            {
                loft = part.FeatureByName(loftName) as Feature;
                data = loft.GetDefinition() as ILoftedBendsFeatureData;
                if (data == null || !data.AccessSelections(model, null)) throw new InvalidOperationException("LOFT71 cannot access the Lofted Bend.");
                bool modified = false;
                try { data.Direction = !data.Direction; modified = loft.ModifyDefinition(data, model, null); }
                finally { if (!modified) data.ReleaseSelectionAccess(); }
                model.EditRebuild3();
                flipped = modified;
                off = FarthestVertex71(model, expectedBase);
            }
            loft = part.FeatureByName(loftName) as Feature;
            bool warning;
            int error = loft.GetErrorCode2(out warning);
            MirrorV7Diagnostics.Log("[LOFT71][BASE] feature=" + loftName + " profiles=" + string.Join(",", profiles) +
                " thicknessDirectionFlipped=" + flipped + " farthestVertex_m=" + off.ToString("E2") + " error=" + error);
            if ((error != 0 && !warning) || off > cornerLimit)
                throw new InvalidOperationException("LOFT71 Lofted Bend is not the mirror image of the source. farthestVertex_m=" + off.ToString("E2") + " error=" + error);
            if (off > 1e-7) ADDIN.Helpers.NativeBodyCoincidence67.LargestAcceptedDeviation71 = Math.Max(ADDIN.Helpers.NativeBodyCoincidence67.LargestAcceptedDeviation71, off);
            // Equal corners do not prove the surface between them: the two skins are
            // measured against each other and must stay inside the accepted local deviation.
            object[] solids = part.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[] ?? new object[0];
            double skin = solids.Length != 1 ? double.PositiveInfinity
                : ADDIN.Helpers.NativeBodyCoincidence67.MeasureSkinDeviation71(expectedBase, (Body2)solids[0], "LOFT71_BASE");
            if (skin > ADDIN.Helpers.NativeBodyCoincidence67.LocalDeviationLimit71)
                throw new InvalidOperationException("LOFT71 Lofted Bend surface is not the mirror image of the source. skinDeviation_m=" + skin.ToString("E2"));
        }
    }
}
