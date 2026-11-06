using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    internal sealed partial class AutoVCutSectionCommand
    {
        private const string OmoteSurfaceMarkPrefix = "TAI_OMOTE_SURFACE_";

        private sealed class OmoteSurfaceMarkPlan
        {
            internal double X, Y, OutX, OutY, Angle;
        }

        private sealed class OmoteSurfaceEdge
        {
            internal ProjectedSegmentInfo Segment;
            internal Entity Entity;
        }

        // Use the actual opposite skin of the colored URA face. The profile's
        // bounding box alone cannot distinguish OMOTE from an upturned flange.
        private static bool TryGetOppositeSkinPoint(Face2 ura, out double[] point)
        {
            point = null;
            double[] normal = ura == null ? null : ura.Normal as double[];
            double[] box = ura == null ? null : ura.GetBox() as double[];
            Body2 body = ura == null ? null : ura.GetBody() as Body2;
            Array faces = body == null ? null : body.GetFaces() as Array;
            if (normal == null || normal.Length < 3 || box == null || box.Length < 6 || faces == null)
                return false;
            double norm = Math.Sqrt(normal.Sum(v => v * v));
            if (norm < 1e-9) return false;
            normal = normal.Select(v => v / norm).ToArray();
            double[] origin = ura.GetClosestPointOn((box[0] + box[3]) / 2,
                (box[1] + box[4]) / 2, (box[2] + box[5]) / 2) as double[];
            if (origin == null || origin.Length < 3) return false;
            double bestDistance = double.MaxValue;
            double minArea = Math.Abs(ura.GetArea()) * .05;
            foreach (Face2 face in faces.OfType<Face2>())
            {
                double[] otherNormal = face.Normal as double[];
                if (otherNormal == null || otherNormal.Length < 3 || Math.Abs(face.GetArea()) < minArea)
                    continue;
                double dot = otherNormal[0] * normal[0] + otherNormal[1] * normal[1] + otherNormal[2] * normal[2];
                if (dot > -.999) continue;
                double[] candidate = face.GetClosestPointOn(origin[0], origin[1], origin[2]) as double[];
                if (candidate == null || candidate.Length < 3) continue;
                double distance = -(candidate[0] - origin[0]) * normal[0] -
                    (candidate[1] - origin[1]) * normal[1] - (candidate[2] - origin[2]) * normal[2];
                if (distance <= 1e-8 || distance >= bestDistance) continue;
                bestDistance = distance;
                point = candidate;
            }
            return point != null;
        }

        private bool UpdatePartOmoteSurfaceMark(ModelDoc2 drawing, DrawingDoc drawingDoc,
            SolidWorks.Interop.sldworks.View section, out string notice)
        {
            notice = "Chưa chèn dấu ▽: không xác định được mặt 表 đối diện mặt ウラ đã tô màu.";
            try
            {
                var root = GetPartSectionBase(section);
                var part = root == null ? null : root.ReferencedDocument as ModelDoc2;
                double[] uraNormal, skin;
                Face2 ura;
                if (part == null || !TryGetUraViewNormal(part, section, root.ReferencedConfiguration,
                    out uraNormal, out ura) || !TryGetOppositeSkinPoint(ura, out skin))
                {
                    RemoveOwnedOmoteMarks(drawing, section);
                    return false;
                }
                double[] skinInView = TransformModelPoint(app.IGetMathUtility(), section.ModelToViewTransform,
                    skin[0], skin[1], skin[2]);
                return UpdateOmoteSurfaceMark(drawing, drawingDoc, section, skinInView, uraNormal, null, out notice);
            }
            catch (Exception ex)
            {
                Log("OMOTE MARK ERROR view=" + section.GetName2() + " " + ex);
                RemoveOwnedOmoteMarks(drawing, section);
                notice = "Chưa chèn dấu ▽ mặt 表: " + ex.Message;
                return false;
            }
        }

        private bool UpdateAssemblyOmoteSurfaceMark(ModelDoc2 drawing, DrawingDoc drawingDoc,
            SolidWorks.Interop.sldworks.View section, AssemblyOuterPart outer, out string notice)
        {
            notice = "Chưa chèn dấu ▽: không xác định được mặt 表 của tấm ngoài Assembly.";
            try
            {
                Component2 component = assemblyModelComponents.Values.FirstOrDefault(c =>
                    c != null && AssemblyComponentKey(c) == outer.ComponentKey);
                ModelDoc2 part = component == null ? null : component.GetModelDoc2() as ModelDoc2;
                Face2 ura;
                double area;
                string why;
                double[] skin;
                if (part == null || !TryGetLargestUraFace(part, component.ReferencedConfiguration,
                    out ura, out area, out why) || !TryGetOppositeSkinPoint(ura, out skin))
                {
                    RemoveOwnedOmoteMarks(drawing, section);
                    return false;
                }
                return UpdateOmoteSurfaceMark(drawing, drawingDoc, section,
                    ProjectComponentPoint(component, section, skin),
                    TransformComponentVectorToView(component, section, ura.Normal as double[]),
                    outer.ComponentKey, out notice);
            }
            catch (Exception ex)
            {
                Log("ASSEMBLY OMOTE MARK ERROR " + ex);
                RemoveOwnedOmoteMarks(drawing, section);
                notice = "Chưa chèn dấu ▽ mặt 表 của tấm ngoài: " + ex.Message;
                return false;
            }
        }

        private static bool TryPlanOmoteSurfaceMark(List<ProjectedSegmentInfo> segments,
            double[] skinPoint, double[] uraNormal, out OmoteSurfaceMarkPlan plan)
        {
            plan = null;
            if (skinPoint == null || skinPoint.Length < 2 || uraNormal == null || uraNormal.Length < 3)
                return false;
            double projected = Math.Sqrt(uraNormal[0] * uraNormal[0] + uraNormal[1] * uraNormal[1]);
            double length = Math.Sqrt(projected * projected + uraNormal[2] * uraNormal[2]);
            // A face viewed head-on is not a section skin; do not guess a side.
            if (length < 1e-9 || projected < length * .98) return false;
            double nx = -uraNormal[0] / projected, ny = -uraNormal[1] / projected;
            double plane = nx * skinPoint[0] + ny * skinPoint[1];
            const double planeTolerance = .00001;
            var skin = segments.Where(s => s.Length >= .0005 &&
                Math.Abs(nx * (s.X2 - s.X1) + ny * (s.Y2 - s.Y1)) <= s.Length * .002 &&
                Math.Abs(nx * (s.X1 + s.X2) / 2 + ny * (s.Y1 + s.Y2) / 2 - plane) <= planeTolerance)
                .OrderByDescending(s => s.Length).ThenBy(s => s.X1).ThenBy(s => s.Y1).FirstOrDefault();
            if (skin == null) return false;
            // Pick the middle of one displayed straight run, never the center of
            // the entire view (which may be a break gap, bend or an empty area).
            plan = new OmoteSurfaceMarkPlan { X = (skin.X1 + skin.X2) / 2,
                Y = (skin.Y1 + skin.Y2) / 2, OutX = nx, OutY = ny,
                Angle = Math.Atan2(-nx, ny) };
            if (plan.Angle < 0) plan.Angle += 2 * Math.PI;
            return true;
        }

        private bool UpdateOmoteSurfaceMark(ModelDoc2 drawing, DrawingDoc drawingDoc,
            SolidWorks.Interop.sldworks.View section, double[] skinPoint, double[] uraNormal,
            string outerComponentKey, out string notice)
        {
            notice = "Chưa chèn dấu ▽: không tìm được đoạn mặt 表 hiển thị rõ trên mặt cắt.";
            OmoteSurfaceMarkPlan plan;
            if (!TryPlanOmoteSurfaceMark(GetDisplayedSegments(section), skinPoint, uraNormal, out plan))
            {
                RemoveOwnedOmoteMarks(drawing, section);
                return false;
            }
            var owned = GetOwnedOmoteMarks(section);
            if (section.IsBroken() && owned.Count > 0)
            {
                // Break/flip operations can replace the temporary cut edges.
                // Reinsert our symbol on the fresh displayed edge instead of
                // retaining an annotation tied to the previous cut body.
                foreach (var old in owned) RemoveOmoteAnnotation(drawing, old.GetAnnotation() as Annotation);
                owned.Clear();
            }
            SFSymbol symbol = owned.FirstOrDefault();
            foreach (var extra in owned.Skip(1)) RemoveOmoteAnnotation(drawing, extra.GetAnnotation() as Annotation);
            drawing.ClearSelection2(true);
            if (!drawingDoc.ActivateView(section.GetName2()))
                throw new InvalidOperationException("Không kích hoạt được mặt cắt để đặt dấu 表.");
            Entity edge = SelectDisplayedOmoteEdge(drawing, section, plan, outerComponentKey);
            if (edge == null)
            {
                var edges = GetOmoteSurfaceEdges(section, outerComponentKey);
                edge = edges.Where(e => PointOnOmoteSegment(plan, e.Segment.X1, e.Segment.Y1,
                    e.Segment.X2, e.Segment.Y2)).Select(e => e.Entity).FirstOrDefault(e => e != null);
            }
            if (edge == null)
            {
                RemoveOwnedOmoteMarks(drawing, section);
                notice = "Chưa chèn dấu ▽: không tìm được cạnh mặt 表 để liên kết ký hiệu.";
                return false;
            }
            drawing.ClearSelection2(true);
            if (symbol == null)
            {
                if (edge != null)
                {
                    var select = ((SelectionMgr)drawing.SelectionManager).CreateSelectData();
                    select.View = section; select.X = plan.X; select.Y = plan.Y; select.Z = 0;
                    edge.Select4(false, select);
                }
                // This JIS texture is a single empty triangle, the same native
                // Surface Finish symbol used by the manual PropertyManager.
                symbol = drawing.Extension.InsertSurfaceFinishSymbol3((int)swSFSymType_e.swSFJIS_Surface_Texture_1,
                    (int)swLeaderStyle_e.swNO_LEADER, plan.X, plan.Y, 0,
                    (int)swSFLaySym_e.swSFNone, 0, "", "", "", "", "", "", "");
                if (symbol == null) throw new InvalidOperationException("SOLIDWORKS không tạo được Surface Finish.");
                var created = symbol.GetAnnotation() as Annotation;
                if (created == null || !created.SetName(OmoteSurfaceMarkPrefix + Guid.NewGuid().ToString("N")))
                {
                    RemoveOmoteAnnotation(drawing, created);
                    throw new InvalidOperationException("Không đánh dấu được ký hiệu để tránh chèn trùng.");
                }
            }
            var annotation = symbol.GetAnnotation() as Annotation;
            if (edge != null) annotation.SetAttachedEntities(new object[] { edge });
            annotation.SetLeader3((int)swLeaderStyle_e.swNO_LEADER, 0, false, false, false, false);
            // Local JIS triangle: tip=(0,0), body extends toward +Y. Rotate +Y
            // onto the true OMOTE outward normal. Left=90deg, below=180deg.
            if (!symbol.SetAngle(plan.Angle) || !annotation.SetPosition2(plan.X, plan.Y, 0))
            {
                RemoveOmoteAnnotation(drawing, annotation);
                throw new InvalidOperationException("Không đặt được hướng ra ngoài của dấu 表.");
            }
            double actual = symbol.GetAngle();
            double facing = -Math.Sin(actual) * plan.OutX + Math.Cos(actual) * plan.OutY;
            double[] position = annotation.GetPosition() as double[];
            if (facing < .999 || position == null || position.Length < 2 ||
                Distance2(position[0], position[1], plan.X, plan.Y) > .0001 ||
                annotation.GetAttachedEntityCount3() < 1)
            {
                Log("OMOTE MARK INVALID view=" + section.GetName2() + " facing=" + F(facing) +
                    " attached=" + annotation.GetAttachedEntityCount3() + " expectedMm=(" + Mm(plan.X) +
                    "," + Mm(plan.Y) + ") actualMm=" + (position == null || position.Length < 2 ? "NONE" :
                        "(" + Mm(position[0]) + "," + Mm(position[1]) + ")"));
                RemoveOmoteAnnotation(drawing, annotation);
                throw new InvalidOperationException("Dấu 表 chưa bám đúng mặt ngoài; đã bỏ dấu chưa đúng.");
            }
            drawing.ClearSelection2(true);
            Log("OMOTE MARK view=" + section.GetName2() + " native=True exterior=True angleDeg=" +
                F(actual * 180 / Math.PI) + " tipMm=(" + Mm(plan.X) + "," + Mm(plan.Y) +
                ") normal=(" + F(plan.OutX) + "," + F(plan.OutY) + ") surfaceAttached=True");
            notice = "";
            return true;
        }

        private Entity SelectDisplayedOmoteEdge(ModelDoc2 drawing, SolidWorks.Interop.sldworks.View section,
            OmoteSurfaceMarkPlan plan, string outerComponentKey)
        {
            // Resolve the real displayed cut edge at the sheet point. In a
            // broken view its displayed intervals differ from model vertices.
            if (!drawing.Extension.SelectByID2("", "EDGE", plan.X, plan.Y, 0, false, 0, null, 0))
                return null;
            var manager = drawing.SelectionManager as SelectionMgr;
            var edge = manager == null ? null : manager.GetSelectedObject6(1, -1) as Entity;
            var ownerView = manager == null ? null : manager.GetSelectedObjectsDrawingView2(1, -1) as SolidWorks.Interop.sldworks.View;
            if (edge == null || (ownerView != null && ownerView.GetName2() != section.GetName2())) return null;
            if (!string.IsNullOrEmpty(outerComponentKey))
            {
                RememberAssemblyDrawingRootNames(section);
                Component2 component;
                string config, reason;
                if (!TryResolveAssemblyDrawingComponent(edge.IGetComponent2(), out component, out config, out reason) ||
                    AssemblyComponentKey(component) != outerComponentKey) return null;
            }
            return edge;
        }

        private List<OmoteSurfaceEdge> GetOmoteSurfaceEdges(SolidWorks.Interop.sldworks.View section,
            string outerComponentKey)
        {
            var result = new List<OmoteSurfaceEdge>();
            var native = new List<OmoteSurfaceEdge>();
            if (!string.IsNullOrEmpty(outerComponentKey))
            {
                foreach (var edge in GetAssemblySectionEdges(section).Where(e => e.ComponentKey == outerComponentKey))
                    native.Add(new OmoteSurfaceEdge { Entity = edge.Edge as Entity,
                        Segment = new ProjectedSegmentInfo { X1 = edge.X1, Y1 = edge.Y1, X2 = edge.X2, Y2 = edge.Y2 } });
            }
            else
            {
                object buffer;
                Array edges = section.GetPolylines7(1, out buffer) as Array;
                foreach (Edge edge in edges == null ? Enumerable.Empty<Edge>() : edges.OfType<Edge>().Distinct())
                {
                    var curve = edge.GetCurve() as Curve;
                    var first = edge.GetStartVertex() as Vertex;
                    var last = edge.GetEndVertex() as Vertex;
                    double[] a = first == null ? null : first.GetPoint() as double[];
                    double[] b = last == null ? null : last.GetPoint() as double[];
                    if (curve == null || !curve.IsLine() || a == null || b == null) continue;
                    a = TransformModelPoint(app.IGetMathUtility(), section.ModelToViewTransform, a[0], a[1], a[2]);
                    b = TransformModelPoint(app.IGetMathUtility(), section.ModelToViewTransform, b[0], b[1], b[2]);
                    if (a != null && b != null) native.Add(new OmoteSurfaceEdge { Entity = edge as Entity,
                        Segment = new ProjectedSegmentInfo { X1 = a[0], Y1 = a[1], X2 = b[0], Y2 = b[1] } });
                }
            }
            // Break View splits one model edge into several displayed polylines.
            // Their array indices are not a one-to-one edge correspondence.
            foreach (var displayed in GetDisplayedSegments(section))
            {
                var midpoint = new OmoteSurfaceMarkPlan { X = (displayed.X1 + displayed.X2) / 2,
                    Y = (displayed.Y1 + displayed.Y2) / 2 };
                var match = native.FirstOrDefault(e => e.Entity != null && e.Segment.Length > 1e-9 &&
                    Math.Abs((displayed.X2 - displayed.X1) * (e.Segment.Y2 - e.Segment.Y1) -
                        (displayed.Y2 - displayed.Y1) * (e.Segment.X2 - e.Segment.X1)) <=
                        displayed.Length * e.Segment.Length * .002 &&
                    PointOnOmoteSegment(midpoint, e.Segment.X1, e.Segment.Y1, e.Segment.X2, e.Segment.Y2));
                if (match != null) result.Add(new OmoteSurfaceEdge { Segment = displayed, Entity = match.Entity });
            }
            return result;
        }

        private static bool PointOnOmoteSegment(OmoteSurfaceMarkPlan plan, double x1, double y1,
            double x2, double y2)
        {
            double dx = x2 - x1, dy = y2 - y1, squared = dx * dx + dy * dy;
            if (squared < 1e-12) return false;
            double t = ((plan.X - x1) * dx + (plan.Y - y1) * dy) / squared;
            return t >= -.001 && t <= 1.001 &&
                Distance2(plan.X, plan.Y, x1 + t * dx, y1 + t * dy) < .00001;
        }

        private static List<SFSymbol> GetOwnedOmoteMarks(SolidWorks.Interop.sldworks.View view)
        {
            Array symbols = view == null ? null : view.GetSFSymbols() as Array;
            return (symbols == null ? Enumerable.Empty<SFSymbol>() : symbols.OfType<SFSymbol>())
                .Where(s => ((s.GetAnnotation() as Annotation)?.GetName() ?? "")
                    .StartsWith(OmoteSurfaceMarkPrefix, StringComparison.Ordinal)).ToList();
        }

        private static void RemoveOmoteAnnotation(ModelDoc2 drawing, Annotation annotation)
        {
            if (annotation == null) return;
            drawing.ClearSelection2(true);
            if (!annotation.Select3(false, null) || !drawing.Extension.DeleteSelection2(0))
                throw new InvalidOperationException("Không xóa được dấu 表 cần cập nhật.");
        }

        private static void RemoveOwnedOmoteMarks(ModelDoc2 drawing, SolidWorks.Interop.sldworks.View section)
        {
            foreach (var symbol in GetOwnedOmoteMarks(section))
                RemoveOmoteAnnotation(drawing, symbol.GetAnnotation() as Annotation);
        }
    }
}
