// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>
    /// Option "keep MARK sketches on the Drawing View": sketch entities the
    /// user drew on the source FlatPattern Drawing View (or on the sheet,
    /// inside that view) whose layer name contains MARK are exported on
    /// LASER_MARK.
    ///
    /// The pasted temporary view has its sketch deleted, and a pasted entity
    /// is not guaranteed to keep a layer that does not exist in the temporary
    /// Drawing. The entities are therefore read from the SOURCE Drawing before
    /// Copy, converted to model space (independent of view scale/position),
    /// and recreated on the private marker layer of the temporary view. The
    /// finalizer then maps them to LASER_MARK and removes any CUT/BEND copy
    /// underneath. The source Drawing is only read.
    /// </summary>
    internal partial class DxfOutPut
    {
        private const double SketchPointTolerance = 1e-6;
        private const int SketchCurveSamples = 256;

        internal sealed class DrawingViewMarkSketch
        {
            /// <summary>LINE, CIRCLE (center, point on circle), ARC (start,
            /// end, mid) or POLYLINE.</summary>
            public string Kind { get; set; } = "";
            public List<double[]> ModelPoints { get; } = new List<double[]>();

            /// <summary>
            /// The same points in SOURCE sheet coordinates (metres, at the
            /// source view scale). Used where the geometry cannot be redrawn
            /// through the view transform (section views exported as a block).
            /// </summary>
            public List<double[]> SheetPoints { get; } = new List<double[]>();
            public string LayerName { get; set; } = "";

            /// <summary>
            /// Drawn in the view sketch (moves with the view), as opposed to a
            /// sheet sketch that only lies over the view.
            /// </summary>
            public bool FromView { get; set; }
        }

        internal sealed class DrawingViewMarkSketchCollection
        {
            public List<DrawingViewMarkSketch> Items { get; } =
                new List<DrawingViewMarkSketch>();
            public SortedSet<string> MarkLayers { get; } =
                new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            public SortedSet<string> OtherLayers { get; } =
                new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, int> Skipped { get; } =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public int ViewSketchCount { get; set; }
            public int SheetSketchCount { get; set; }

            // What the read found, so an export that keeps nothing can say why.
            public int ViewSegmentTotal { get; set; }
            public int SheetSegmentTotal { get; set; }
            public int ConstructionSkipped { get; set; }
            public int OutsideOutlineSkipped { get; set; }
            public Dictionary<string, int> ViewLayerCounts { get; } =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, int> SheetLayerCounts { get; } =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            public void Skip(string reason)
            {
                Skipped[reason] = Skipped.TryGetValue(reason, out int count)
                    ? count + 1
                    : 1;
            }
        }

        private static DrawingViewMarkSketchCollection CollectDrawingViewMarkSketches(
            ModelDoc2 drawingModel,
            IView flatView,
            DXFPropforExport properties)
        {
            var result = new DrawingViewMarkSketchCollection();
            MathUtility mathUtility = null;
            MathTransform modelToView = null;
            MathTransform viewToModel = null;
            ISketch viewSketch = null;
            IView sheetView = null;
            ISketch sheetSketch = null;
            try
            {
                mathUtility = DxfExportContext.App?.GetMathUtility() as MathUtility;
                modelToView = flatView?.ModelToViewTransform;
                viewToModel = modelToView?.Inverse() as MathTransform;
                if (mathUtility == null || viewToModel == null)
                {
                    DxfExportNotices.Warning(
                        "Sketch MARK",
                        "CẢNH BÁO: không đọc được hệ tọa độ Drawing View nên " +
                        "không lấy được sketch MARK trên Drawing View.");
                    return result;
                }

                viewSketch = flatView.IGetSketch();
                result.ViewSketchCount = CollectMarkSketchSegments(
                    result, viewSketch, flatView, viewToModel, mathUtility, null);

                // A sketch drawn while the view is not activated belongs to
                // the sheet. Keep it when it lies inside the FlatPattern view.
                double[] outline = flatView.GetOutline() as double[];
                sheetView = (drawingModel as IDrawingDoc)?.GetFirstView() as IView;
                if (sheetView != null && outline != null && outline.Length >= 4)
                {
                    sheetSketch = sheetView.IGetSketch();
                    result.SheetSketchCount = CollectMarkSketchSegments(
                        result, sheetSketch, null, viewToModel, mathUtility, outline);
                }
            }
            catch (Exception ex)
            {
                ErrorEvents.LogError(
                    $"File:{properties?.FileName}-{properties?.PartNumber}: " +
                    "LỖI Sketch MARK: không đọc được sketch trên Drawing View: " +
                    ex.Message);
            }
            finally
            {
                SafeReleaseComObject(sheetSketch);
                SafeReleaseComObject(sheetView);
                SafeReleaseComObject(viewSketch);
                SafeReleaseComObject(viewToModel);
                SafeReleaseComObject(modelToView);
                SafeReleaseComObject(mathUtility);
            }

            if (result.Items.Count == 0)
            {
                DxfExportNotices.Info(
                    "Sketch MARK",
                    "Không có sketch nào trên Drawing View thuộc layer chứa MARK" +
                    (result.OtherLayers.Count > 0
                        ? " (layer đang có: " + string.Join(", ", result.OtherLayers) + ")."
                        : "."));
            }
            if (result.Skipped.Count > 0)
            {
                DxfExportNotices.Warning(
                    "Sketch MARK",
                    "CẢNH BÁO Sketch MARK: bỏ qua " +
                    string.Join("; ", result.Skipped.Select(pair => pair.Key + " ×" + pair.Value)) +
                    " trên layer MARK.");
            }
            return result;
        }

        /// <summary>
        /// Reads 2D Sketch MARK features from the referenced Part (e.g. ｽｹｯﾁ1-mark) and
        /// MARK-layer sketches drawn on the Drawing View / Sheet, projected into the
        /// 1:1 View-local millimeter coordinate space of <see cref="IView.GetPolyLinesAndCurves"/>.
        /// </summary>
        internal static List<ADDIN.Commands.DxfChecking.DxfEdge> ExtractViewMarkSketchEdges(
            IView flatView,
            ISldWorks app = null,
            ModelDoc2 drawingModel = null)
        {
            var result = new List<ADDIN.Commands.DxfChecking.DxfEdge>();
            if (flatView == null) return result;

            ISldWorks swApp = app ?? DxfExportContext.App;
            MathUtility mathUtility = null;
            MathTransform modelToView = null;
            MathTransform viewToModel = null;
            ISketch viewSketch = null;
            MathTransform viewModelToSketch = null;
            IView sheetView = null;
            ISketch sheetSketch = null;
            try
            {
                mathUtility = swApp?.GetMathUtility() as MathUtility;
                modelToView = flatView.ModelToViewTransform;
                viewToModel = modelToView?.Inverse() as MathTransform;
                viewSketch = flatView.IGetSketch();
                viewModelToSketch = viewSketch?.ModelToSketchTransform;
                if (mathUtility == null || modelToView == null) return result;

                double[] m = modelToView.ArrayData as double[];
                Func<double[], ADDIN.Commands.DxfChecking.DxfPoint?> projectModelToViewMm = modelPoint =>
                {
                    if (modelPoint == null || modelPoint.Length < 3) return null;
                    if (viewModelToSketch != null)
                    {
                        double[] vp = TransformModelPoint(
                            modelPoint, null, modelToView, viewModelToSketch, mathUtility);
                        if (IsUsableSketchPoint(vp))
                            return new ADDIN.Commands.DxfChecking.DxfPoint(vp[0] * 1000.0, vp[1] * 1000.0);
                    }
                    double[] sp = ApplyTransform(modelPoint, modelToView, mathUtility);
                    if (!IsUsableSketchPoint(sp) || m == null || m.Length < 13 || Math.Abs(m[12]) <= 1e-12)
                        return null;
                    return new ADDIN.Commands.DxfChecking.DxfPoint(
                        (sp[0] - m[9]) / m[12] * 1000.0,
                        (sp[1] - m[10]) / m[12] * 1000.0);
                };

                // 1. Part 2D Sketch features with MARK in their name (LaserMarkFeatureKind.Sketch2D).
                ModelDoc2 part = flatView.ReferencedDocument as ModelDoc2;
                if (part != null && part.GetType() == (int)swDocumentTypes_e.swDocPART)
                {
                    string configuration = "";
                    try { configuration = flatView.ReferencedConfiguration ?? ""; } catch { }
                    var recognizer = new LaserMarkFeatureRecognizer();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (Feature feature = part.FirstFeature() as Feature;
                         feature != null;
                         feature = feature.GetNextFeature() as Feature)
                    {
                        string name = "";
                        try { name = feature.Name ?? ""; } catch { }
                        if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;
                        if (!recognizer.TryResolve(feature, out LaserMarkFeatureRecognizer.Resolution mark)) continue;
                        if (mark.SourceKind != LaserMarkFeatureKind.Sketch2D) continue;
                        if (!string.Equals(mark.SourceName, name, StringComparison.OrdinalIgnoreCase)) continue;
                        if (IsFeatureSuppressedIn(feature, configuration)) continue;

                        ISketch partSketch = null;
                        MathTransform partModelToSketch = null;
                        MathTransform sketchToPartModel = null;
                        try
                        {
                            partSketch = feature.GetSpecificFeature2() as ISketch;
                            if (partSketch == null) continue;
                            partModelToSketch = partSketch.ModelToSketchTransform;
                            sketchToPartModel = partModelToSketch?.Inverse() as MathTransform;
                            if (sketchToPartModel == null) continue;

                            foreach (ISketchSegment segment in (partSketch.GetSketchSegments() as object[] ?? new object[0]).OfType<ISketchSegment>())
                            {
                                bool construction = false;
                                try { construction = segment.ConstructionGeometry; } catch { }
                                if (construction) continue;

                                if (!TryReadMarkSketchSegment(
                                        segment, partModelToSketch, null, mathUtility,
                                        out string kind, out List<double[]> sketchPoints, out _))
                                    continue;

                                var pointsMm = new List<ADDIN.Commands.DxfChecking.DxfPoint>(sketchPoints.Count);
                                foreach (double[] sp in sketchPoints)
                                {
                                    double[] mp = ApplyTransform(sp, sketchToPartModel, mathUtility);
                                    ADDIN.Commands.DxfChecking.DxfPoint? vp = projectModelToViewMm(mp);
                                    if (!vp.HasValue) { pointsMm = null; break; }
                                    pointsMm.Add(vp.Value);
                                }
                                if (pointsMm != null && pointsMm.Count >= 2)
                                    AppendMarkSketchEdges(kind, pointsMm, result);
                            }
                        }
                        catch { }
                        finally
                        {
                            SafeReleaseComObject(sketchToPartModel);
                            SafeReleaseComObject(partModelToSketch);
                        }
                    }
                }

                // 2. User-drawn MARK-layer sketches on the Drawing View or Sheet inside the view outline.
                if (viewToModel != null)
                {
                    var collection = new DrawingViewMarkSketchCollection();
                    CollectMarkSketchSegments(
                        collection, viewSketch, flatView, viewToModel, mathUtility, null);

                    ModelDoc2 drawing = drawingModel ?? (swApp?.ActiveDoc as ModelDoc2);
                    double[] outline = flatView.GetOutline() as double[];
                    sheetView = (drawing as IDrawingDoc)?.GetFirstView() as IView;
                    if (sheetView != null && outline != null && outline.Length >= 4)
                    {
                        sheetSketch = sheetView.IGetSketch();
                        CollectMarkSketchSegments(
                            collection, sheetSketch, null, viewToModel, mathUtility, outline);
                    }

                    foreach (DrawingViewMarkSketch sketchItem in collection.Items)
                    {
                        if (sketchItem.ModelPoints.Count < 2) continue;
                        var pointsMm = new List<ADDIN.Commands.DxfChecking.DxfPoint>(sketchItem.ModelPoints.Count);
                        foreach (double[] mp in sketchItem.ModelPoints)
                        {
                            ADDIN.Commands.DxfChecking.DxfPoint? vp = projectModelToViewMm(mp);
                            if (!vp.HasValue) { pointsMm = null; break; }
                            pointsMm.Add(vp.Value);
                        }
                        if (pointsMm != null && pointsMm.Count >= 2)
                            AppendMarkSketchEdges(sketchItem.Kind, pointsMm, result);
                    }
                }
            }
            catch { }
            finally
            {
                SafeReleaseComObject(sheetSketch);
                SafeReleaseComObject(sheetView);
                SafeReleaseComObject(viewModelToSketch);
                SafeReleaseComObject(viewSketch);
                SafeReleaseComObject(viewToModel);
                SafeReleaseComObject(modelToView);
                SafeReleaseComObject(mathUtility);
            }
            return result;
        }

        private static void AppendMarkSketchEdges(
            string kind,
            List<ADDIN.Commands.DxfChecking.DxfPoint> points,
            List<ADDIN.Commands.DxfChecking.DxfEdge> target)
        {
            var created = new List<ADDIN.Commands.DxfChecking.DxfEdge>();
            switch (kind)
            {
                case "LINE":
                    if (points.Count >= 2 && points[0].Distance(points[1]) > 1e-3)
                        created.Add(ADDIN.Commands.DxfChecking.DxfGeometry.Line(points[0], points[1]));
                    break;
                case "CIRCLE":
                    if (points.Count >= 2)
                    {
                        double radius = points[0].Distance(points[1]);
                        if (radius > 1e-3)
                            created.Add(ADDIN.Commands.DxfChecking.DxfGeometry.Circle(
                                points[0], radius, 0, ADDIN.Commands.DxfChecking.DxfGeometry.Tau));
                    }
                    break;
                case "ARC":
                    if (points.Count >= 3)
                    {
                        var start = points[0];
                        var end = points[1];
                        var mid = points[2];
                        double ax = start.X, ay = start.Y, bx = mid.X, by = mid.Y, cx = end.X, cy = end.Y;
                        double det = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
                        if (Math.Abs(det) <= 1e-12)
                        {
                            if (start.Distance(end) > 1e-3)
                                created.Add(ADDIN.Commands.DxfChecking.DxfGeometry.Line(start, end));
                        }
                        else
                        {
                            double a2 = ax * ax + ay * ay, b2 = bx * bx + by * by, c2 = cx * cx + cy * cy;
                            double centerX = (a2 * (by - cy) + b2 * (cy - ay) + c2 * (ay - by)) / det;
                            double centerY = (a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / det;
                            var center = new ADDIN.Commands.DxfChecking.DxfPoint(centerX, centerY);
                            double radius = start.Distance(center);
                            double startAngle = Math.Atan2(ay - centerY, ax - centerX);
                            double midAngle = NormalizePositiveAngle(Math.Atan2(by - centerY, bx - centerX) - startAngle);
                            double endAngle = NormalizePositiveAngle(Math.Atan2(cy - centerY, cx - centerX) - startAngle);
                            double sweep = midAngle <= endAngle ? endAngle : endAngle - 2 * Math.PI;
                            if (radius > 1e-3 && Math.Abs(sweep) > 1e-6)
                                created.Add(ADDIN.Commands.DxfChecking.DxfGeometry.Circle(center, radius, startAngle, sweep));
                        }
                    }
                    break;
                default:
                    for (int i = 1; i < points.Count; i++)
                    {
                        if (points[i - 1].Distance(points[i]) > 1e-3)
                            created.Add(ADDIN.Commands.DxfChecking.DxfGeometry.Line(points[i - 1], points[i]));
                    }
                    break;
            }
            foreach (var edge in created)
            {
                if (edge.Bounds.Size <= 1e-5) continue;
                if (edge.Type == "LINE" && edge.Length <= 0.001) continue;
                edge.Space = "Model";
                edge.Layer = "DrawingView";
                target.Add(edge);
            }
        }

        /// <summary>
        /// Reads the MARK-layer segments of one sketch. <paramref name="owner"/>
        /// is the Drawing View for a view sketch, or null for the sheet sketch
        /// (then <paramref name="sheetOutline"/> limits the entities to the
        /// FlatPattern view). Returns the number of entities kept.
        /// </summary>
        private static int CollectMarkSketchSegments(
            DrawingViewMarkSketchCollection result,
            ISketch sketch,
            IView owner,
            MathTransform viewToModel,
            MathUtility mathUtility,
            double[] sheetOutline)
        {
            if (sketch == null) return 0;

            object[] segments = sketch.GetSketchSegments() as object[];
            if (segments == null || segments.Length == 0) return 0;

            MathTransform sheetToSketch = null;
            MathTransform sketchToSheet = null;
            int kept = 0;
            try
            {
                sheetToSketch = sketch.ModelToSketchTransform;
                sketchToSheet = sheetToSketch?.Inverse() as MathTransform;
                if (sketchToSheet == null) return 0;

                foreach (object value in segments)
                {
                    ISketchSegment segment = value as ISketchSegment;
                    if (segment == null) continue;

                    string layer = "";
                    bool construction = false;
                    try { layer = segment.Layer ?? ""; } catch { }
                    try { construction = segment.ConstructionGeometry; } catch { }

                    string layerLabel = string.IsNullOrWhiteSpace(layer)
                        ? "(không layer)"
                        : layer;
                    Dictionary<string, int> layerCounts = owner != null
                        ? result.ViewLayerCounts
                        : result.SheetLayerCounts;
                    layerCounts[layerLabel] = layerCounts.TryGetValue(layerLabel, out int seen)
                        ? seen + 1
                        : 1;
                    if (owner != null) result.ViewSegmentTotal++;
                    else result.SheetSegmentTotal++;

                    if (!LaserMarkFeatureRecognizer.HasMarkToken(layer))
                    {
                        if (owner != null)
                            result.OtherLayers.Add(layerLabel);
                        continue;
                    }
                    if (construction)
                    {
                        result.ConstructionSkipped++;
                        continue;
                    }

                    string kind;
                    List<double[]> sketchPoints;
                    string skipReason;
                    if (!TryReadMarkSketchSegment(
                            segment, sheetToSketch, owner, mathUtility,
                            out kind, out sketchPoints, out skipReason))
                    {
                        result.Skip(skipReason);
                        continue;
                    }

                    var sheetPoints = new List<double[]>(sketchPoints.Count);
                    foreach (double[] point in sketchPoints)
                    {
                        double[] sheetPoint = ApplyTransform(point, sketchToSheet, mathUtility);
                        if (sheetPoint == null) break;
                        sheetPoints.Add(sheetPoint);
                    }
                    if (sheetPoints.Count != sketchPoints.Count)
                    {
                        result.Skip("không đổi được tọa độ");
                        continue;
                    }

                    if (sheetOutline != null &&
                        !AllInsideOutline(kind, sheetPoints, sheetOutline))
                    {
                        result.OutsideOutlineSkipped++;
                        continue;
                    }

                    var item = new DrawingViewMarkSketch
                    {
                        Kind = kind,
                        LayerName = layer,
                        FromView = owner != null
                    };
                    item.SheetPoints.AddRange(sheetPoints);
                    foreach (double[] sheetPoint in sheetPoints)
                    {
                        double[] modelPoint = ApplyTransform(sheetPoint, viewToModel, mathUtility);
                        if (modelPoint == null) break;
                        item.ModelPoints.Add(modelPoint);
                    }
                    if (item.ModelPoints.Count != sheetPoints.Count)
                    {
                        result.Skip("không đổi được tọa độ");
                        continue;
                    }

                    result.Items.Add(item);
                    result.MarkLayers.Add(layer);
                    kept++;
                }
                return kept;
            }
            finally
            {
                SafeReleaseComObject(sketchToSheet);
                SafeReleaseComObject(sheetToSketch);
            }
        }

        private static bool TryReadMarkSketchSegment(
            ISketchSegment segment,
            MathTransform sheetToSketch,
            IView owner,
            MathUtility mathUtility,
            out string kind,
            out List<double[]> points,
            out string skipReason)
        {
            kind = "";
            points = new List<double[]>();
            skipReason = "";

            int type;
            try { type = segment.GetType(); }
            catch
            {
                skipReason = "không đọc được loại sketch";
                return false;
            }

            switch (type)
            {
                case (int)swSketchSegments_e.swSketchLINE:
                {
                    var line = segment as ISketchLine;
                    double[] start = ReadSketchPoint(line?.GetStartPoint2());
                    double[] end = ReadSketchPoint(line?.GetEndPoint2());
                    if (start == null || end == null)
                    {
                        skipReason = "line lỗi";
                        return false;
                    }
                    kind = "LINE";
                    points.Add(start);
                    points.Add(end);
                    return true;
                }
                case (int)swSketchSegments_e.swSketchARC:
                    return TryReadSketchArc(segment as ISketchArc, segment,
                        out kind, out points, out skipReason);
                case (int)swSketchSegments_e.swSketchELLIPSE:
                    return TryReadSketchEllipse(segment as ISketchEllipse, segment,
                        out kind, out points, out skipReason);
                case (int)swSketchSegments_e.swSketchSPLINE:
                    return TryReadSketchSpline(segment, sheetToSketch, owner, mathUtility,
                        out kind, out points, out skipReason);
                case (int)swSketchSegments_e.swSketchTEXT:
                    skipReason = "sketch text";
                    return false;
                default:
                    skipReason = "loại sketch " + ((swSketchSegments_e)type);
                    return false;
            }
        }

        private static bool TryReadSketchArc(
            ISketchArc arc,
            ISketchSegment segment,
            out string kind,
            out List<double[]> points,
            out string skipReason)
        {
            kind = "";
            points = new List<double[]>();
            skipReason = "arc lỗi";
            if (arc == null) return false;

            double[] center = ReadSketchPoint(arc.GetCenterPoint2());
            double[] start = ReadSketchPoint(arc.GetStartPoint2());
            double[] end = ReadSketchPoint(arc.GetEndPoint2());
            if (center == null) return false;

            double radius = 0;
            try { radius = arc.GetRadius(); } catch { }
            if (start != null) radius = Distance2D(center, start);
            if (radius <= 1e-12) return false;

            bool isCircle = false;
            // IsCircle() returns an int (1, or -1 as a VARIANT_TRUE); any
            // non-zero value means a full circle.
            try { isCircle = arc.IsCircle() != 0; } catch { }
            if (isCircle || start == null || end == null ||
                Distance2D(start, end) <= SketchPointTolerance)
            {
                kind = "CIRCLE";
                points.Add(center);
                points.Add(start ?? new[] { center[0] + radius, center[1], center[2] });
                return true;
            }

            double startAngle = Math.Atan2(start[1] - center[1], start[0] - center[0]);
            double endAngle = Math.Atan2(end[1] - center[1], end[0] - center[0]);
            double counterClockwise = NormalizePositiveAngle(endAngle - startAngle);
            double clockwise = counterClockwise - 2 * Math.PI;

            // GetRotationDir gives the direction; the segment length confirms
            // it whenever it matches one of the two candidate arcs.
            int direction = 1;
            try { direction = arc.GetRotationDir(); } catch { }
            double sweep = direction < 0 ? clockwise : counterClockwise;
            try
            {
                double length = segment.GetLength();
                if (length > 0)
                {
                    bool ccwMatches = Math.Abs(radius * counterClockwise - length) <= length * 0.02;
                    bool cwMatches = Math.Abs(radius * Math.Abs(clockwise) - length) <= length * 0.02;
                    if (ccwMatches && !cwMatches) sweep = counterClockwise;
                    else if (cwMatches && !ccwMatches) sweep = clockwise;
                }
            }
            catch { }

            double midAngle = startAngle + sweep / 2;
            kind = "ARC";
            points.Add(start);
            points.Add(end);
            points.Add(new[]
            {
                center[0] + radius * Math.Cos(midAngle),
                center[1] + radius * Math.Sin(midAngle),
                start[2]
            });
            return true;
        }

        private static bool TryReadSketchEllipse(
            ISketchEllipse ellipse,
            ISketchSegment segment,
            out string kind,
            out List<double[]> points,
            out string skipReason)
        {
            kind = "";
            points = new List<double[]>();
            skipReason = "ellipse lỗi";
            if (ellipse == null) return false;

            double[] center = ReadSketchPoint(ellipse.GetCenterPoint2());
            double[] major = ReadSketchPoint(ellipse.GetMajorPoint2());
            double[] minor = ReadSketchPoint(ellipse.GetMinorPoint2());
            if (center == null || major == null || minor == null) return false;

            double majorRadius = Distance2D(center, major);
            double minorRadius = Distance2D(center, minor);
            if (majorRadius <= 1e-12 || minorRadius <= 1e-12) return false;

            double ux = (major[0] - center[0]) / majorRadius;
            double uy = (major[1] - center[1]) / majorRadius;
            double vx = -uy;
            double vy = ux;

            double[] start = ReadSketchPoint(ellipse.GetStartPoint2());
            double[] end = ReadSketchPoint(ellipse.GetEndPoint2());
            double startParameter = 0;
            double sweep = 2 * Math.PI;
            if (start != null && end != null &&
                Distance2D(start, end) > SketchPointTolerance)
            {
                startParameter = EllipseParameter(start, center, ux, uy, vx, vy, majorRadius, minorRadius);
                double endParameter = EllipseParameter(end, center, ux, uy, vx, vy, majorRadius, minorRadius);
                double counterClockwise = NormalizePositiveAngle(endParameter - startParameter);
                sweep = counterClockwise;
                try
                {
                    double length = segment.GetLength();
                    double ccwLength = EllipseArcLength(majorRadius, minorRadius, startParameter, counterClockwise);
                    double cwLength = EllipseArcLength(majorRadius, minorRadius, startParameter, counterClockwise - 2 * Math.PI);
                    if (length > 0 &&
                        Math.Abs(cwLength - length) < Math.Abs(ccwLength - length))
                        sweep = counterClockwise - 2 * Math.PI;
                }
                catch { }
            }

            kind = "POLYLINE";
            for (int index = 0; index <= SketchCurveSamples; index++)
            {
                double parameter = startParameter + sweep * index / SketchCurveSamples;
                double cos = Math.Cos(parameter) * majorRadius;
                double sin = Math.Sin(parameter) * minorRadius;
                points.Add(new[]
                {
                    center[0] + ux * cos + vx * sin,
                    center[1] + uy * cos + vy * sin,
                    center[2]
                });
            }
            if (start != null && end != null && sweep < 2 * Math.PI - 1e-9)
            {
                points[0] = start;
                points[points.Count - 1] = end;
            }
            return true;
        }

        /// <summary>
        /// Samples a sketch spline through its curve. The curve of a Drawing
        /// sketch may be expressed in sketch, sheet or model coordinates, so
        /// the space whose samples pass through the spline's own first point
        /// is used. Falls back to the spline points when that fails.
        /// </summary>
        private static bool TryReadSketchSpline(
            ISketchSegment segment,
            MathTransform sheetToSketch,
            IView owner,
            MathUtility mathUtility,
            out string kind,
            out List<double[]> points,
            out string skipReason)
        {
            kind = "POLYLINE";
            points = new List<double[]>();
            skipReason = "spline lỗi";

            var splinePoints = new List<double[]>();
            if ((segment as ISketchSpline)?.GetPoints2() is object[] rawPoints)
            {
                foreach (object rawPoint in rawPoints)
                {
                    double[] point = ReadSketchPoint(rawPoint);
                    if (point != null) splinePoints.Add(point);
                }
            }
            if (splinePoints.Count < 2) return false;

            List<double[]> samples = SampleSketchCurve(segment);
            if (samples != null)
            {
                MathTransform modelToView = null;
                try
                {
                    modelToView = owner?.ModelToViewTransform;
                    var candidates = new List<Func<double[], double[]>>
                    {
                        point => point,
                        point => ApplyTransform(point, sheetToSketch, mathUtility)
                    };
                    if (modelToView != null)
                    {
                        MathTransform viewTransform = modelToView;
                        candidates.Add(point => ApplyTransform(
                            ApplyTransform(point, viewTransform, mathUtility),
                            sheetToSketch,
                            mathUtility));
                    }

                    foreach (Func<double[], double[]> toSketch in candidates)
                    {
                        List<double[]> converted = samples
                            .Select(toSketch)
                            .ToList();
                        if (converted.Any(point => point == null)) continue;

                        double startGap = Math.Min(
                            Distance2D(converted[0], splinePoints[0]),
                            Distance2D(converted[converted.Count - 1], splinePoints[0]));
                        double endGap = Math.Min(
                            Distance2D(converted[0], splinePoints[splinePoints.Count - 1]),
                            Distance2D(converted[converted.Count - 1], splinePoints[splinePoints.Count - 1]));
                        if (startGap > SketchPointTolerance * 10 ||
                            endGap > SketchPointTolerance * 10)
                            continue;

                        if (Distance2D(converted[0], splinePoints[0]) >
                            Distance2D(converted[converted.Count - 1], splinePoints[0]))
                            converted.Reverse();
                        points = converted;
                        return true;
                    }
                }
                finally
                {
                    SafeReleaseComObject(modelToView);
                }
            }

            // Approximation through the spline points; better than losing the
            // mark, and reported so the user can check it.
            points = splinePoints;
            skipReason = "";
            DxfExportNotices.Warning(
                "Sketch MARK",
                "CẢNH BÁO Sketch MARK: một spline được xuất gần đúng theo các " +
                "điểm của nó - kiểm tra DXF.");
            return true;
        }

        private static List<double[]> SampleSketchCurve(ISketchSegment segment)
        {
            ICurve curve = null;
            try
            {
                curve = segment.GetCurve() as ICurve;
                if (curve == null) return null;

                double startParameter;
                double endParameter;
                bool closed;
                bool periodic;
                if (!curve.GetEndParams(out startParameter, out endParameter, out closed, out periodic) ||
                    Math.Abs(endParameter - startParameter) <= 1e-12)
                    return null;

                var samples = new List<double[]>(SketchCurveSamples + 1);
                for (int index = 0; index <= SketchCurveSamples; index++)
                {
                    double parameter = startParameter +
                        (endParameter - startParameter) * index / SketchCurveSamples;
                    double[] value = curve.Evaluate2(parameter, 0) as double[];
                    if (value == null || value.Length < 3) return null;
                    samples.Add(new[] { value[0], value[1], value[2] });
                }
                return samples;
            }
            catch (Exception ex)
            {
                DxfTrace.Debug("Cannot sample sketch curve: " + ex.Message);
                return null;
            }
            finally
            {
                SafeReleaseComObject(curve);
            }
        }

        private static int CreateDrawingViewMarkSketchGeometry(
            IModelDoc2 model,
            IView view,
            string layerName,
            DXFPropforExport properties)
        {
            DrawingViewMarkSketchCollection marks = properties?.DrawingViewMarkSketches;
            if (marks == null || marks.Items.Count == 0 || model == null || view == null)
                return 0;

            IDrawingDoc drawing = model as IDrawingDoc;
            ISketch viewSketch = null;
            SketchManager sketchManager = null;
            MathUtility mathUtility = null;
            MathTransform modelToView = null;
            MathTransform modelToSketch = null;
            bool oldAddToDb = false;
            int created = 0;
            int failed = 0;
            var markerContext = new MarkerCreationContext(model, view, properties);
            try
            {
                if (drawing == null) return 0;
                drawing.ActivateView(view.GetName2());
                viewSketch = view.IGetSketch();
                sketchManager = model.SketchManager as SketchManager;
                mathUtility = DxfExportContext.App?.GetMathUtility() as MathUtility;
                modelToView = view.ModelToViewTransform;
                modelToSketch = viewSketch?.ModelToSketchTransform;
                if (viewSketch == null || sketchManager == null ||
                    mathUtility == null || modelToView == null ||
                    modelToSketch == null)
                {
                    failed = marks.Items.Count;
                    return 0;
                }

                oldAddToDb = sketchManager.AddToDB;
                sketchManager.AddToDB = true;
                foreach (DrawingViewMarkSketch mark in marks.Items)
                {
                    if (CreateDrawingSketchMark(
                            mark, sketchManager, modelToView,
                            modelToSketch, mathUtility, layerName,
                            markerContext))
                        created++;
                    else
                        failed++;
                }
                return created;
            }
            catch (Exception ex)
            {
                failed = marks.Items.Count - created;
                DxfTrace.Debug("Cannot recreate Drawing View MARK sketches: " + ex.Message);
                return created;
            }
            finally
            {
                if (sketchManager != null)
                {
                    try { sketchManager.AddToDB = oldAddToDb; } catch { }
                }
                try { model.ClearSelection2(true); } catch { }
                SafeReleaseComObject(viewSketch);
                SafeReleaseComObject(sketchManager);
                SafeReleaseComObject(mathUtility);
                SafeReleaseComObject(modelToView);
                SafeReleaseComObject(modelToSketch);

                string source = marks.SheetSketchCount > 0
                    ? $" ({marks.ViewSketchCount} trong view, {marks.SheetSketchCount} trên sheet)"
                    : "";
                if (created > 0)
                {
                    DxfExportNotices.Info(
                        "Sketch MARK",
                        "Giữ " + created + " sketch trên Drawing View" + source +
                        " → LASER_MARK, layer: " + string.Join(", ", marks.MarkLayers) + ".");
                }
                if (failed > 0)
                {
                    ErrorEvents.LogError(
                        $"File:{properties?.FileName}-{properties?.PartNumber}: " +
                        "LỖI Sketch MARK: không vẽ lại được " + failed + "/" +
                        marks.Items.Count + " sketch MARK của Drawing View " +
                        "trên Drawing tạm - DXF sẽ thiếu các đường này.");
                }
            }
        }

        private static bool CreateDrawingSketchMark(
            DrawingViewMarkSketch mark,
            SketchManager sketchManager,
            MathTransform modelToView,
            MathTransform modelToSketch,
            MathUtility mathUtility,
            string layerName,
            MarkerCreationContext markerContext)
        {
            var points = new List<double[]>(mark.ModelPoints.Count);
            foreach (double[] modelPoint in mark.ModelPoints)
            {
                double[] point = TransformModelPoint(
                    modelPoint, null, modelToView, modelToSketch, mathUtility);
                if (!IsUsableSketchPoint(point)) return false;
                points.Add(point);
            }

            var segments = new List<ISketchSegment>();
            try
            {
                switch (mark.Kind)
                {
                    case "LINE":
                        AddSegment(segments, sketchManager.CreateLine(
                            points[0][0], points[0][1], 0,
                            points[1][0], points[1][1], 0));
                        break;
                    case "CIRCLE":
                    {
                        double radius = Distance2D(points[0], points[1]);
                        if (!TryCreateVerifiedCircleMarker(
                                markerContext, sketchManager,
                                points[0], radius, segments))
                            AddSketchPolyline(segments, sketchManager, SampleCircle(points[0], radius));
                        break;
                    }
                    case "ARC":
                        // Built, read back and compared; a wrong arc is
                        // discarded and the exact sampled arc takes its place.
                        if (!TryCreateVerifiedArcMarker(
                                markerContext, sketchManager,
                                points[0], points[1], points[2], segments))
                            AddSketchPolyline(segments, sketchManager, SampleThreePointArc(points[0], points[1], points[2]));
                        break;
                    default:
                        AddSketchPolyline(segments, sketchManager, points);
                        break;
                }

                if (segments.Count == 0) return false;
                bool assigned = true;
                foreach (ISketchSegment segment in segments)
                {
                    segment.Layer = layerName;
                    segment.LayerOverride = 0;
                    assigned &= string.Equals(segment.Layer, layerName, StringComparison.OrdinalIgnoreCase);
                }
                return assigned;
            }
            finally
            {
                foreach (ISketchSegment segment in segments)
                    SafeReleaseComObject(segment);
            }
        }

        private static void AddSketchPolyline(
            ICollection<ISketchSegment> segments,
            SketchManager sketchManager,
            IList<double[]> points)
        {
            if (points == null) return;
            double[] previous = null;
            foreach (double[] point in points)
            {
                if (previous != null && Distance2D(previous, point) > 1e-9)
                {
                    AddSegment(segments, sketchManager.CreateLine(
                        previous[0], previous[1], 0,
                        point[0], point[1], 0));
                }
                if (previous == null || Distance2D(previous, point) > 1e-9)
                    previous = point;
            }
        }

        /// <summary>2D arc through start, mid and end, as a polyline.</summary>
        private static List<double[]> SampleThreePointArc(
            double[] start,
            double[] end,
            double[] mid)
        {
            double ax = start[0], ay = start[1];
            double bx = mid[0], by = mid[1];
            double cx = end[0], cy = end[1];
            double determinant = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
            if (Math.Abs(determinant) <= 1e-18)
                return new List<double[]> { start, end };

            double a2 = ax * ax + ay * ay;
            double b2 = bx * bx + by * by;
            double c2 = cx * cx + cy * cy;
            double centerX = (a2 * (by - cy) + b2 * (cy - ay) + c2 * (ay - by)) / determinant;
            double centerY = (a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / determinant;
            double radius = Math.Sqrt((ax - centerX) * (ax - centerX) + (ay - centerY) * (ay - centerY));

            double startAngle = Math.Atan2(ay - centerY, ax - centerX);
            double midAngle = NormalizePositiveAngle(Math.Atan2(by - centerY, bx - centerX) - startAngle);
            double endAngle = NormalizePositiveAngle(Math.Atan2(cy - centerY, cx - centerX) - startAngle);
            double sweep = midAngle <= endAngle ? endAngle : endAngle - 2 * Math.PI;

            var points = new List<double[]>(SketchCurveSamples + 1);
            for (int index = 0; index <= SketchCurveSamples; index++)
            {
                double angle = startAngle + sweep * index / SketchCurveSamples;
                points.Add(new[] { centerX + radius * Math.Cos(angle), centerY + radius * Math.Sin(angle), 0 });
            }
            points[0] = start;
            points[points.Count - 1] = end;
            return points;
        }

        private static bool AllInsideOutline(
            string kind,
            IList<double[]> sheetPoints,
            double[] outline)
        {
            const double margin = 1e-4;
            IEnumerable<double[]> checkedPoints = sheetPoints;
            if (kind == "CIRCLE")
            {
                double radius = Distance2D(sheetPoints[0], sheetPoints[1]);
                double[] center = sheetPoints[0];
                checkedPoints = new[]
                {
                    new[] { center[0] - radius, center[1] - radius },
                    new[] { center[0] + radius, center[1] + radius }
                };
            }
            return checkedPoints.All(point =>
                point[0] >= outline[0] - margin && point[0] <= outline[2] + margin &&
                point[1] >= outline[1] - margin && point[1] <= outline[3] + margin);
        }

        private static double[] ReadSketchPoint(object value)
        {
            ISketchPoint point = value as ISketchPoint;
            if (point == null) return null;
            try { return new[] { point.X, point.Y, point.Z }; }
            catch { return null; }
        }

        private static double[] ApplyTransform(
            double[] point,
            MathTransform transform,
            MathUtility mathUtility)
        {
            if (point == null || transform == null || mathUtility == null) return null;
            MathPoint mathPoint = mathUtility.CreatePoint(
                new[] { point[0], point[1], point.Length > 2 ? point[2] : 0 }) as MathPoint;
            if (mathPoint == null) return null;
            MathPoint transformed = null;
            try
            {
                transformed = mathPoint.MultiplyTransform(transform) as MathPoint;
                return transformed?.ArrayData as double[];
            }
            finally
            {
                SafeReleaseComObject(transformed);
                SafeReleaseComObject(mathPoint);
            }
        }

        private static double NormalizePositiveAngle(double angle)
        {
            double twoPi = 2 * Math.PI;
            angle %= twoPi;
            if (angle <= 1e-12) angle += twoPi;
            return angle;
        }

        private static double EllipseParameter(
            double[] point,
            double[] center,
            double ux,
            double uy,
            double vx,
            double vy,
            double majorRadius,
            double minorRadius)
        {
            double dx = point[0] - center[0];
            double dy = point[1] - center[1];
            return Math.Atan2(
                (dx * vx + dy * vy) / minorRadius,
                (dx * ux + dy * uy) / majorRadius);
        }

        private static double EllipseArcLength(
            double majorRadius,
            double minorRadius,
            double startParameter,
            double sweep)
        {
            const int steps = 256;
            double length = 0;
            double previousX = majorRadius * Math.Cos(startParameter);
            double previousY = minorRadius * Math.Sin(startParameter);
            for (int index = 1; index <= steps; index++)
            {
                double parameter = startParameter + sweep * index / steps;
                double x = majorRadius * Math.Cos(parameter);
                double y = minorRadius * Math.Sin(parameter);
                length += Math.Sqrt((x - previousX) * (x - previousX) + (y - previousY) * (y - previousY));
                previousX = x;
                previousY = y;
            }
            return length;
        }
    }
}
