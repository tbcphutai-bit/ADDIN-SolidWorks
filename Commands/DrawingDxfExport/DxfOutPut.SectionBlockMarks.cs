// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>
    /// Exact placement of MARK sketches on a Section view exported through a
    /// sketch block (View -> DXF).
    ///
    /// The old approach compared the DXF bounding box with the outline of the
    /// source view. That outline is not the geometry (it carries a margin, and
    /// it grows when sketches stick out), so the comparison failed as soon as
    /// the margin was not equal on all sides. Here nothing is estimated:
    ///
    ///   0. SOURCE view, before it becomes a block: the bounding box of its
    ///      model edges (IView.GetPolylines7) on the sheet, and the sheet
    ///      position of the view origin (see <see cref="ReadSectionViewGeometry"/>).
    ///   1. SOURCE block, right after it was made and before it is rescaled:
    ///      the transform block -> sheet of the instance and the exact extent
    ///      of the block definition. InsertViewAsBlock does NOT leave the
    ///      block on top of the view: it puts the given point of the VIEW
    ///      frame (here the view origin) at the given SHEET position. The
    ///      block is tied to the view by matching its extent with the
    ///      measured view geometry (same size, verified); only when that
    ///      cannot be measured is the view origin used, and the result says
    ///      it was not verified. The sketches (sheet coordinates) are then
    ///      converted to block coordinates with the inverse.
    ///   2. TEMPORARY drawing, after the paste: the transform of the pasted
    ///      instance (position and scale 1/S) takes the sketches, and the
    ///      block extent, to temporary-drawing coordinates.
    ///   3. DXF: the geometry is measured and must have the size of the block
    ///      extent; its position gives the offset drawing -> DXF
    ///      (DxfLayerFinalizer.InjectExactMarks).
    /// </summary>
    internal partial class DxfOutPut
    {
        /// <summary>x' = A x + B y + TX, y' = C x + D y + TY.</summary>
        internal sealed class BlockFrameTransform
        {
            public double A = 1, B, C, D = 1, TX, TY;

            public double[] Apply(double[] point)
            {
                return new[]
                {
                    A * point[0] + B * point[1] + TX,
                    C * point[0] + D * point[1] + TY,
                    0.0
                };
            }

            public bool IsIdentity =>
                Math.Abs(A - 1) < 1e-9 && Math.Abs(D - 1) < 1e-9 &&
                Math.Abs(B) < 1e-9 && Math.Abs(C) < 1e-9 &&
                Math.Abs(TX) < 1e-9 && Math.Abs(TY) < 1e-9;

            public double ScaleFactor => Math.Sqrt(Math.Abs(A * D - B * C));

            public BlockFrameTransform Inverse()
            {
                double determinant = A * D - B * C;
                if (Math.Abs(determinant) < 1e-24) return null;
                return new BlockFrameTransform
                {
                    A = D / determinant,
                    B = -B / determinant,
                    C = -C / determinant,
                    D = A / determinant,
                    TX = (B * TY - D * TX) / determinant,
                    TY = (C * TX - A * TY) / determinant
                };
            }

            /// <summary>outer(inner(p)).</summary>
            public static BlockFrameTransform Compose(
                BlockFrameTransform outer,
                BlockFrameTransform inner)
            {
                return new BlockFrameTransform
                {
                    A = outer.A * inner.A + outer.B * inner.C,
                    B = outer.A * inner.B + outer.B * inner.D,
                    C = outer.C * inner.A + outer.D * inner.C,
                    D = outer.C * inner.B + outer.D * inner.D,
                    TX = outer.A * inner.TX + outer.B * inner.TY + outer.TX,
                    TY = outer.C * inner.TX + outer.D * inner.TY + outer.TY
                };
            }

            public static BlockFrameTransform Translation(double x, double y)
            {
                return new BlockFrameTransform { TX = x, TY = y };
            }
        }

        /// <summary>The MARK sketches in the coordinates of the block definition.</summary>
        internal sealed class SectionMarkFrame
        {
            public bool Ok;
            public string Failure = "";
            public string Diagnostics = "";

            /// <summary>minX, minY, maxX, maxY of the block definition (block frame, metres).</summary>
            public double[] BlockBounds;

            /// <summary>Every sampled point of the block geometry (block frame).</summary>
            public List<double[]> BlockPoints = new List<double[]>();
            public List<DxfInjectMark> BlockMarks = new List<DxfInjectMark>();

            /// <summary>The instance is inside a sketch whose own frame is not the sheet.</summary>
            public bool UsesSketchToSheet;

            /// <summary>
            /// The block was tied to the MEASURED geometry of the view (same
            /// size). False when only the view origin placed it.
            /// </summary>
            public bool Verified;

            /// <summary>How the block was tied to the view (message, log).</summary>
            public string Method = "";

            /// <summary>The view geometry the block matched (source sheet, metres), when verified.</summary>
            public double[] ViewBounds;

            /// <summary>Block definition -> source sheet, where the view is.</summary>
            public BlockFrameTransform BlockToSheet;

            /// <summary>MARK sketches SOLIDWORKS converted into the block with the geometry.</summary>
            public int CarriedMarks;
        }

        /// <summary>
        /// A placement of the block proven by the MARK sketches found inside
        /// it: the block, shifted by (ShiftX, ShiftY) on the sheet, lies on
        /// the view.
        /// </summary>
        internal sealed class SectionFrameAnchor
        {
            public bool UsesSketchToSheet;
            public double ShiftX;
            public double ShiftY;
            public string Label = "";

            /// <summary>
            /// One of several positions where the marks all match (repeated or
            /// symmetric geometry): used only when exactly one of them agrees
            /// with a verified placement of the block.
            /// </summary>
            public bool Ambiguous;
        }

        /// <summary>The MARK sketches in the coordinates of the temporary drawing.</summary>
        internal sealed class SectionMarkPlacement
        {
            public bool Ok;
            public string Failure = "";
            public string Diagnostics = "";
            public List<DxfInjectMark> Marks = new List<DxfInjectMark>();

            /// <summary>minX, minY, maxX, maxY of the block geometry, temporary drawing, metres.</summary>
            public double[] ExpectedBounds;

            /// <summary>Block definition (source frame) -> temporary drawing.</summary>
            public BlockFrameTransform BlockToTemporary;
        }

        /// <summary>
        /// What the source Section view says about where its geometry is,
        /// read before the view becomes a block.
        /// </summary>
        internal sealed class SectionViewGeometry
        {
            /// <summary>
            /// Bounding box of the model edges of the view on the source sheet
            /// (minX, minY, maxX, maxY, metres), once per frame the points of
            /// GetPolylines7 may be expressed in. The right one is the one
            /// that has the size of the block and lies inside the view.
            /// </summary>
            public List<KeyValuePair<string, double[]>> Bounds =
                new List<KeyValuePair<string, double[]>>();

            /// <summary>Sheet position (x, y, metres) of the view origin, per way of reading it.</summary>
            public List<KeyValuePair<string, double[]>> Origins =
                new List<KeyValuePair<string, double[]>>();

            public string Diagnostics = "";
        }

        /// <summary>The model edges of a drawing view as GetPolylines7 returns them (raw frame).</summary>
        internal sealed class ViewPolylineSet
        {
            public readonly List<double[]> Points = new List<double[]>();
            public readonly List<ViewPolylineArc> Arcs = new List<ViewPolylineArc>();
            public int Polylines;
            public int ArcItems;
        }

        /// <summary>Center + Radius * (cos t * U + sin t * V), t = 0..Sweep.</summary>
        internal sealed class ViewPolylineArc
        {
            public double[] Center;
            public double[] U;
            public double[] V;
            public double Radius;
            public double Sweep;

            public double[] At(double t)
            {
                double cos = Math.Cos(t) * Radius;
                double sin = Math.Sin(t) * Radius;
                return new[]
                {
                    Center[0] + cos * U[0] + sin * V[0],
                    Center[1] + cos * U[1] + sin * V[1],
                    Center[2] + cos * U[2] + sin * V[2]
                };
            }
        }

        /// <summary>How the block instance on the source sheet relates to the view.</summary>
        internal sealed class SectionFrameChoice
        {
            public bool Ok;

            /// <summary>Block definition -> source sheet, where the view is.</summary>
            public BlockFrameTransform Transform;
            public bool UsesSketchToSheet;
            public bool Verified;
            public string Method = "";
            public double[] ViewBounds;
            public string Failure = "";
            public string Diagnostics = "";
        }

        private sealed class SectionFrameCandidate
        {
            public bool UsesSketchToSheet;
            public BlockFrameTransform Base;
            public double ShiftX;
            public double ShiftY;
            public string Label = "";
            public double SizeError = double.NaN;

            /// <summary>Block extent on the source sheet once shifted.</summary>
            public double[] Placed;

            /// <summary>The measured view geometry it matched (verified candidates only).</summary>
            public double[] Target;

            /// <summary>Lower is more trusted among verified candidates.</summary>
            public int Priority = 9;

            /// <summary>From an ambiguous anchor (see <see cref="SectionFrameAnchor.Ambiguous"/>).</summary>
            public bool Ambiguous;
        }

        // At most this many positions of an ambiguous MARK match are kept.
        private const int MaxAmbiguousAnchors = 256;

        // Sheet-space tolerance for "the block lies inside the view outline".
        private const double BlockInsideOutlineTolerance = 0.0005;

        // "The insertion point of the block is the view origin" (sheet, 0.02 mm).
        private const double InsertionAtOriginTolerance = 0.00002;

        // MARK sketches searched inside the block (source sheet metres):
        // 0.01 mm, samples every 0.2 mm.
        private const double BlockMarkTolerance = 0.00001;
        private const double BlockMarkSpacing = 0.0002;

        // Size agreement block <-> measured view geometry, on the source sheet:
        // 0.05 mm plus 0.1 % of the larger side.
        private const double ViewGeometrySizeToleranceBase = 0.00005;
        private const double ViewGeometrySizeToleranceRatio = 0.001;

        /// <summary>
        /// Step 0. Reads, from the SOURCE view, the bounding box of its model
        /// edges on the sheet and the sheet position of its origin. Never
        /// throws and never modifies the view (the window is zoomed to fit
        /// so that every polyline exists, as the API help asks).
        /// </summary>
        internal static SectionViewGeometry ReadSectionViewGeometry(IModelDoc2 drawing, IView view)
        {
            var geometry = new SectionViewGeometry();
            var notes = new List<string>();
            MathUtility mathUtility = null;
            ISketch sketch = null;
            MathTransform modelToSketch = null;
            MathTransform sketchToSheet = null;
            MathTransform modelToView = null;
            try
            {
                if (view == null)
                {
                    geometry.Diagnostics = "không có view";
                    return geometry;
                }

                var readings = new List<KeyValuePair<string, double[]>>
                {
                    new KeyValuePair<string, double[]>("sheet", new double[] { 1, 0, 0, 0, 0, 1, 0, 0 })
                };
                mathUtility = DxfExportContext.App?.GetMathUtility() as MathUtility;
                if (mathUtility != null)
                {
                    try
                    {
                        sketch = view.IGetSketch();
                        modelToSketch = sketch?.ModelToSketchTransform;
                        sketchToSheet = modelToSketch?.Inverse() as MathTransform;
                        double[] affine = ReadAffineToSheet(sketchToSheet, mathUtility);
                        if (affine != null)
                        {
                            readings.Add(new KeyValuePair<string, double[]>("sketch view", affine));
                            geometry.Origins.Add(new KeyValuePair<string, double[]>(
                                "gốc sketch view", new[] { affine[3], affine[7] }));
                        }
                    }
                    catch (Exception ex)
                    {
                        notes.Add("sketch view: " + ex.Message);
                    }

                    try
                    {
                        modelToView = view.ModelToViewTransform;
                        double[] affine = ReadAffineToSheet(modelToView, mathUtility);
                        if (affine != null)
                        {
                            readings.Add(new KeyValuePair<string, double[]>("model", affine));
                            geometry.Origins.Add(new KeyValuePair<string, double[]>(
                                "gốc model", new[] { affine[3], affine[7] }));
                        }
                    }
                    catch (Exception ex)
                    {
                        notes.Add("model: " + ex.Message);
                    }
                }

                // Polylines outside the zoomed region may not exist yet.
                try { drawing?.ViewZoomtofit2(); } catch { }

                int displayMode = -1;
                try { displayMode = view.GetDisplayMode2(); } catch { }

                object raw = null;
                try
                {
                    // 1 = without crosshatch lines.
                    view.GetPolylines7(1, out raw);
                }
                catch (Exception ex)
                {
                    notes.Add("GetPolylines7: " + ex.Message);
                }

                string failure;
                ViewPolylineSet polylines = ParseViewPolylines(ToPolylineData(raw), out failure);
                if (polylines == null)
                {
                    notes.Add("polyline: " + failure + " (display mode " + displayMode + ")");
                }
                else
                {
                    notes.Add(
                        "polyline: " + polylines.Polylines + " đường, " +
                        polylines.Arcs.Count + "/" + polylines.ArcItems + " cung theo tâm, " +
                        polylines.Points.Count + " điểm");
                    foreach (KeyValuePair<string, double[]> reading in readings)
                    {
                        double[] bounds = BoundsOfPolylines(polylines, reading.Value);
                        if (bounds == null) continue;
                        geometry.Bounds.Add(new KeyValuePair<string, double[]>(reading.Key, bounds));
                        notes.Add("hình [" + reading.Key + "] " + FormatBounds(bounds));
                    }
                }
                foreach (KeyValuePair<string, double[]> origin in geometry.Origins)
                {
                    notes.Add(origin.Key + " (" + Mm(origin.Value[0]) + ", " +
                              Mm(origin.Value[1]) + ") mm");
                }
            }
            catch (Exception ex)
            {
                notes.Add("lỗi khi đọc hình view: " + ex.Message);
            }
            finally
            {
                SafeReleaseComObject(modelToView);
                SafeReleaseComObject(sketchToSheet);
                SafeReleaseComObject(modelToSketch);
                SafeReleaseComObject(sketch);
                SafeReleaseComObject(mathUtility);
            }
            geometry.Diagnostics = string.Join("; ", notes);
            return geometry;
        }

        /// <summary>
        /// Parses the buffer of IView.GetPolylines7:
        /// [Type, GeomDataSize, GeomData[], LineColor, LineStyle, LineFont,
        /// LineWeight, LayerID, LayerOverride, NumPolyPoints, (x, y, z) ...]
        /// repeated. Type 1 (arc/circle) carries centre, start, end, normal.
        /// Returns null (with the reason) when the buffer does not follow
        /// that layout exactly.
        /// </summary>
        internal static ViewPolylineSet ParseViewPolylines(double[] data, out string failure)
        {
            failure = "";
            if (data == null || data.Length == 0)
            {
                failure = "view không trả về polyline nào";
                return null;
            }

            var set = new ViewPolylineSet();
            int index = 0;
            while (index < data.Length)
            {
                int itemStart = index;
                int type;
                int geometrySize;
                if (!TryReadPolylineCount(data, ref index, out type) || type < 0 || type > 100 ||
                    !TryReadPolylineCount(data, ref index, out geometrySize) || geometrySize < 0 ||
                    (long)index + geometrySize > data.Length)
                {
                    failure = "dữ liệu polyline sai định dạng ở vị trí " + itemStart;
                    return null;
                }

                double[] geometryData = null;
                if (geometrySize > 0)
                {
                    geometryData = new double[geometrySize];
                    Array.Copy(data, index, geometryData, 0, geometrySize);
                }
                index += geometrySize;

                // LineColor, LineStyle, LineFont, LineWeight, LayerID, LayerOverride.
                index += 6;
                int pointCount;
                if (index >= data.Length ||
                    !TryReadPolylineCount(data, ref index, out pointCount) || pointCount < 0 ||
                    (long)index + 3L * pointCount > data.Length)
                {
                    failure = "dữ liệu polyline sai định dạng ở vị trí " + itemStart;
                    return null;
                }

                var points = new List<double[]>(pointCount);
                for (int item = 0; item < pointCount; item++, index += 3)
                {
                    double x = data[index], y = data[index + 1], z = data[index + 2];
                    if (IsFiniteValue(x) && IsFiniteValue(y) && IsFiniteValue(z))
                        points.Add(new[] { x, y, z });
                }
                set.Polylines++;
                set.Points.AddRange(points);

                if (type == 1)
                {
                    set.ArcItems++;
                    ViewPolylineArc arc = BuildPolylineArc(geometryData, points);
                    if (arc != null) set.Arcs.Add(arc);
                }
            }

            if (set.Points.Count == 0 && set.Arcs.Count == 0)
            {
                failure = "polyline không có điểm nào";
                return null;
            }
            return set;
        }

        /// <summary>
        /// Bounding box (minX, minY, maxX, maxY) of the polylines after the
        /// affine map x' = m0 x + m1 y + m2 z + m3, y' = m4 x + m5 y + m6 z + m7.
        /// Arcs contribute their exact extreme points, not only the
        /// tessellation.
        /// </summary>
        internal static double[] BoundsOfPolylines(ViewPolylineSet set, double[] affine)
        {
            if (set == null || affine == null || affine.Length < 8) return null;
            var bounds = new[] { double.MaxValue, double.MaxValue, double.MinValue, double.MinValue };
            foreach (double[] point in set.Points)
                AddMappedPoint(bounds, affine, point);

            foreach (ViewPolylineArc arc in set.Arcs)
            {
                AddMappedPoint(bounds, affine, arc.At(0));
                AddMappedPoint(bounds, affine, arc.At(arc.Sweep));
                for (int axis = 0; axis < 2; axis++)
                {
                    int row = axis * 4;
                    double alongU = affine[row] * arc.U[0] + affine[row + 1] * arc.U[1] + affine[row + 2] * arc.U[2];
                    double alongV = affine[row] * arc.V[0] + affine[row + 1] * arc.V[1] + affine[row + 2] * arc.V[2];
                    if (Math.Abs(alongU) < 1e-15 && Math.Abs(alongV) < 1e-15) continue;

                    // d/dt (alongU cos t + alongV sin t) = 0
                    double extreme = Math.Atan2(alongV, alongU);
                    for (int side = 0; side < 2; side++, extreme += Math.PI)
                    {
                        double angle = extreme % (2 * Math.PI);
                        if (angle < 0) angle += 2 * Math.PI;
                        if (angle <= arc.Sweep) AddMappedPoint(bounds, affine, arc.At(angle));
                    }
                }
            }

            if (bounds[0] > bounds[2] || bounds[1] > bounds[3]) return null;
            return bounds;
        }

        /// <summary>
        /// Decides where the block instance is relative to the view, from
        /// pure data (testable without SOLIDWORKS).
        ///
        /// VERIFIED placements, most trusted first:
        ///   1. <paramref name="anchors"/>: the MARK sketches were found inside
        ///      the block (SOLIDWORKS converted them with the geometry): exact.
        ///   2. the block lies inside the outline AND its insertion point is
        ///      exactly a view origin: SOLIDWORKS put the view point we gave
        ///      (the view origin) where the view origin is, i.e. the block
        ///      was left on the view (ReplaceViewWithBlock).
        ///   3. the block extent has the size of a measured view geometry box
        ///      lying inside the outline (GetPolylines7); centres give the shift.
        /// Anchors win; 2 and 3 must agree with each other. Without any of
        /// them an UNVERIFIED placement (block inside the outline, or the view
        /// origin put at <paramref name="blockPosition"/>) is returned with
        /// Verified = false - for the log only, it is never written.
        /// </summary>
        internal static SectionFrameChoice ChooseSectionBlockFrame(
            IList<double[]> blockPoints,
            BlockFrameTransform blockToSketch,
            BlockFrameTransform sketchToSheet,
            double[] outline,
            SectionViewGeometry viewGeometry,
            double[] blockPosition,
            IList<SectionFrameAnchor> anchors = null)
        {
            var choice = new SectionFrameChoice();
            if (blockPoints == null || blockPoints.Count < 2 || blockToSketch == null ||
                outline == null || outline.Length < 4)
            {
                choice.Failure = "thiếu dữ liệu block hoặc khung view";
                return choice;
            }

            var bases = new List<KeyValuePair<bool, BlockFrameTransform>>
            {
                new KeyValuePair<bool, BlockFrameTransform>(false, blockToSketch)
            };
            if (sketchToSheet != null && !sketchToSheet.IsIdentity)
            {
                bases.Add(new KeyValuePair<bool, BlockFrameTransform>(
                    true,
                    BlockFrameTransform.Compose(sketchToSheet, blockToSketch)));
            }

            var notes = new List<string>();
            var anchored = new List<SectionFrameCandidate>();
            var verified = new List<SectionFrameCandidate>();
            var unverified = new List<SectionFrameCandidate>();
            foreach (KeyValuePair<bool, BlockFrameTransform> entry in bases)
            {
                double[] onSheet = BoundsOf(blockPoints.Select(entry.Value.Apply));
                double[] insertion = entry.Value.Apply(new[] { 0.0, 0.0, 0.0 });
                notes.Add((entry.Key ? "block (qua sketch) " : "block trên sheet gốc ") +
                          FormatBounds(onSheet) + ", điểm chèn (" + Mm(insertion[0]) + ", " +
                          Mm(insertion[1]) + ") mm");

                if (anchors != null)
                {
                    foreach (SectionFrameAnchor anchor in anchors)
                    {
                        if (anchor == null || anchor.UsesSketchToSheet != entry.Key) continue;
                        anchored.Add(new SectionFrameCandidate
                        {
                            UsesSketchToSheet = entry.Key,
                            Base = entry.Value,
                            ShiftX = anchor.ShiftX,
                            ShiftY = anchor.ShiftY,
                            Label = anchor.Label,
                            Placed = ShiftBox(onSheet, anchor.ShiftX, anchor.ShiftY),
                            Ambiguous = anchor.Ambiguous
                        });
                    }
                }

                if (viewGeometry != null)
                {
                    foreach (KeyValuePair<string, double[]> measured in viewGeometry.Bounds)
                    {
                        double[] target = measured.Value;
                        double sizeError = Math.Max(
                            Math.Abs((target[2] - target[0]) - (onSheet[2] - onSheet[0])),
                            Math.Abs((target[3] - target[1]) - (onSheet[3] - onSheet[1])));
                        if (sizeError > ViewGeometrySizeTolerance(target) ||
                            !IsInsideBox(target, outline, BlockInsideOutlineTolerance))
                            continue;

                        double shiftX = (target[0] + target[2]) / 2 - (onSheet[0] + onSheet[2]) / 2;
                        double shiftY = (target[1] + target[3]) / 2 - (onSheet[1] + onSheet[3]) / 2;
                        verified.Add(new SectionFrameCandidate
                        {
                            UsesSketchToSheet = entry.Key,
                            Base = entry.Value,
                            ShiftX = shiftX,
                            ShiftY = shiftY,
                            Label = "khớp hình view [" + measured.Key + "]",
                            SizeError = sizeError,
                            Placed = ShiftBox(onSheet, shiftX, shiftY),
                            Target = target,
                            Priority = 2
                        });
                    }
                }

                if (IsInsideBox(onSheet, outline, BlockInsideOutlineTolerance))
                {
                    string originName = null;
                    if (viewGeometry != null)
                    {
                        foreach (KeyValuePair<string, double[]> origin in viewGeometry.Origins)
                        {
                            double dx = insertion[0] - origin.Value[0];
                            double dy = insertion[1] - origin.Value[1];
                            if (Math.Sqrt(dx * dx + dy * dy) <= InsertionAtOriginTolerance)
                            {
                                originName = origin.Key;
                                break;
                            }
                        }
                    }
                    var inPlace = new SectionFrameCandidate
                    {
                        UsesSketchToSheet = entry.Key,
                        Base = entry.Value,
                        Label = originName != null
                            ? "block nằm tại chỗ view (điểm chèn trùng " + originName + ")"
                            : "block nằm tại chỗ view",
                        Placed = onSheet,
                        Priority = 1
                    };
                    if (originName != null) verified.Add(inPlace);
                    else unverified.Add(inPlace);
                }
                if (viewGeometry != null && blockPosition != null && blockPosition.Length >= 2)
                {
                    foreach (KeyValuePair<string, double[]> origin in viewGeometry.Origins)
                    {
                        double shiftX = origin.Value[0] - blockPosition[0];
                        double shiftY = origin.Value[1] - blockPosition[1];
                        if (Math.Abs(shiftX) < 1e-9 && Math.Abs(shiftY) < 1e-9) continue;
                        double[] moved = ShiftBox(onSheet, shiftX, shiftY);
                        if (!IsInsideBox(moved, outline, BlockInsideOutlineTolerance)) continue;
                        unverified.Add(new SectionFrameCandidate
                        {
                            UsesSketchToSheet = entry.Key,
                            Base = entry.Value,
                            ShiftX = shiftX,
                            ShiftY = shiftY,
                            Label = origin.Key + " đặt tại điểm chèn block",
                            Placed = moved
                        });
                    }
                }
            }

            notes.Add("khung view " + FormatBounds(outline));
            if (viewGeometry != null)
            {
                foreach (KeyValuePair<string, double[]> measured in viewGeometry.Bounds)
                    notes.Add("hình view [" + measured.Key + "] " + FormatBounds(measured.Value));
                foreach (KeyValuePair<string, double[]> origin in viewGeometry.Origins)
                    notes.Add(origin.Key + " (" + Mm(origin.Value[0]) + ", " + Mm(origin.Value[1]) + ") mm");
                if (viewGeometry.Bounds.Count == 0 && !string.IsNullOrEmpty(viewGeometry.Diagnostics))
                    notes.Add(viewGeometry.Diagnostics);
            }
            string measuredText = string.Join("; ", notes);

            // Ambiguous MARK matches (the marks fit at several places of a
            // repeated or symmetric geometry): one of them is used only when
            // it is the ONLY one on the verified placement of the block.
            if (anchored.Any(candidate => !candidate.Ambiguous))
            {
                anchored = anchored.Where(candidate => !candidate.Ambiguous).ToList();
            }
            else if (anchored.Count > 0)
            {
                int positions = anchored.Count;
                List<SectionFrameCandidate> ambiguous = anchored;
                anchored = new List<SectionFrameCandidate>();
                SectionFrameCandidate reference = verified
                    .OrderBy(candidate => candidate.Priority)
                    .ThenBy(candidate => double.IsNaN(candidate.SizeError) ? 0 : candidate.SizeError)
                    .FirstOrDefault();
                double tolerance = reference == null ? 0 : ViewGeometrySizeTolerance(reference.Placed);
                if (reference == null ||
                    verified.Any(candidate => !SameBox(candidate.Placed, reference.Placed, tolerance)))
                {
                    measuredText += "; sketch MARK khớp " + positions + " vị trí trong block, " +
                                    "không có cách đặt đã kiểm chứng (thống nhất) để chọn";
                }
                else
                {
                    List<SectionFrameCandidate> matching = ambiguous
                        .Where(candidate => candidate.UsesSketchToSheet == reference.UsesSketchToSheet &&
                                            SameBox(candidate.Placed, reference.Placed, tolerance))
                        .ToList();
                    if (matching.Count == 1)
                    {
                        anchored = matching;
                        anchored[0].Label += " (1 trong " + positions + " vị trí, chọn theo " + reference.Label + ")";
                    }
                    else
                    {
                        measuredText += "; sketch MARK khớp " + positions + " vị trí trong block, " +
                                        matching.Count + " vị trí trùng " + reference.Label + " nên không dùng";
                    }
                }
            }

            SectionFrameCandidate chosen;
            if (anchored.Count > 0)
            {
                // The MARK sketches themselves, converted by SOLIDWORKS: exact.
                chosen = anchored[0];
                double tolerance = ViewGeometrySizeTolerance(chosen.Placed);
                if (anchored.Any(candidate => !SameBox(candidate.Placed, chosen.Placed, tolerance)))
                {
                    choice.Failure =
                        "sketch MARK trong block cho nhiều vị trí block khác nhau (" +
                        string.Join(", ", anchored.Select(candidate =>
                            candidate.Label + " " + FormatBounds(candidate.Placed))) + ")";
                    choice.Diagnostics = measuredText;
                    return choice;
                }
                foreach (SectionFrameCandidate other in verified)
                {
                    if (!SameBox(other.Placed, chosen.Placed, ViewGeometrySizeTolerance(other.Placed)))
                        measuredText += "; CHÚ Ý: " + other.Label + " cho vị trí khác " + FormatBounds(other.Placed);
                }
                choice.Verified = true;
            }
            else if (verified.Count > 0)
            {
                chosen = verified
                    .OrderBy(candidate => candidate.Priority)
                    .ThenBy(candidate => double.IsNaN(candidate.SizeError) ? 0 : candidate.SizeError)
                    .First();
                double tolerance = ViewGeometrySizeTolerance(chosen.Placed);
                if (verified.Any(candidate => !SameBox(candidate.Placed, chosen.Placed, tolerance)))
                {
                    choice.Failure =
                        "các cách kiểm chứng cho nhiều vị trí block khác nhau (" +
                        string.Join(", ", verified.Select(candidate =>
                            candidate.Label + " " + FormatBounds(candidate.Placed))) + ")";
                    choice.Diagnostics = measuredText;
                    return choice;
                }
                choice.Verified = true;
            }
            else if (unverified.Count > 0)
            {
                chosen = unverified[0];
                double tolerance = ViewGeometrySizeTolerance(chosen.Placed);
                if (unverified.Any(candidate => !SameBox(candidate.Placed, chosen.Placed, tolerance)))
                {
                    choice.Failure =
                        "nhiều cách đặt block cùng nằm trong khung view nên không chọn được (" +
                        string.Join(", ", unverified.Select(candidate =>
                            candidate.Label + " " + FormatBounds(candidate.Placed))) + ")";
                    choice.Diagnostics = measuredText;
                    return choice;
                }
            }
            else
            {
                choice.Failure =
                    "không xác định được block nằm đâu so với view (" + measuredText + ")";
                choice.Diagnostics = measuredText;
                return choice;
            }

            choice.Ok = true;
            choice.Method = chosen.Label;
            choice.UsesSketchToSheet = chosen.UsesSketchToSheet;
            choice.ViewBounds = chosen.Target;
            choice.Transform = BlockFrameTransform.Compose(
                BlockFrameTransform.Translation(chosen.ShiftX, chosen.ShiftY),
                chosen.Base);
            choice.Diagnostics =
                chosen.Label + ": dời block " + Mm(chosen.ShiftX) + ", " + Mm(chosen.ShiftY) + " mm" +
                (double.IsNaN(chosen.SizeError) ? "" : ", lệch kích thước " + Mm(chosen.SizeError) + " mm") +
                ", block trên view " + FormatBounds(chosen.Placed) + "; " + measuredText;
            return choice;
        }

        /// <summary>
        /// Step 1. Never throws; <see cref="SectionMarkFrame.Ok"/> tells
        /// whether the sketches could be tied to the block.
        /// </summary>
        internal static SectionMarkFrame BuildSectionMarkFrame(
            SketchBlockDefinition definition,
            SketchBlockInstance instance,
            double[] sourceOutline,
            DrawingViewMarkSketchCollection marks,
            SectionViewGeometry viewGeometry = null,
            double[] blockPosition = null)
        {
            var frame = new SectionMarkFrame();
            MathUtility mathUtility = null;
            try
            {
                mathUtility = DxfExportContext.App?.GetMathUtility() as MathUtility;
                if (mathUtility == null || definition == null || instance == null)
                {
                    frame.Failure = "không đọc được block của view";
                    return frame;
                }
                if (marks == null || marks.Items.Count == 0)
                {
                    frame.Failure = "không có sketch MARK";
                    return frame;
                }
                if (sourceOutline == null || sourceOutline.Length < 4)
                {
                    frame.Failure = "không đọc được khung view gốc";
                    return frame;
                }

                string boundsFailure;
                double[] bounds;
                List<double[]> points;
                var shapes = new List<SectionMarkGeometry.Shape>();
                if (!TryReadBlockGeometry(definition, out bounds, out points, out boundsFailure, shapes))
                {
                    frame.Failure = boundsFailure;
                    return frame;
                }

                BlockFrameTransform blockToSketch =
                    ReadBlockTransform(instance.BlockToSketchTransform, mathUtility);
                if (blockToSketch == null)
                {
                    frame.Failure = "không đọc được phép biến đổi của block";
                    return frame;
                }
                BlockFrameTransform sketchToSheet = ReadInstanceSketchToSheet(instance, mathUtility);

                // The MARK sketches left in the view are converted with the
                // geometry: found inside the block they tie it to the view
                // exactly.
                string anchorNote;
                int carried;
                List<SectionFrameAnchor> anchors = FindMarkAnchors(
                    shapes, blockToSketch, sketchToSheet, marks, out anchorNote, out carried);
                frame.CarriedMarks = carried;

                SectionFrameChoice choice = ChooseSectionBlockFrame(
                    points, blockToSketch, sketchToSheet, sourceOutline,
                    viewGeometry, blockPosition, anchors);
                frame.Diagnostics = "block " + shapes.Count + " đường, " + points.Count +
                                    " điểm mẫu; " + anchorNote + "; " + choice.Diagnostics;
                if (!choice.Ok)
                {
                    frame.Failure = choice.Failure;
                    return frame;
                }

                BlockFrameTransform sheetToBlock = choice.Transform.Inverse();
                if (sheetToBlock == null)
                {
                    frame.Failure = "phép biến đổi của block không đảo được";
                    return frame;
                }

                foreach (DrawingViewMarkSketch mark in marks.Items)
                {
                    if (mark.SheetPoints.Count < 2) continue;
                    var converted = new DxfInjectMark { Kind = mark.Kind };
                    foreach (double[] point in mark.SheetPoints)
                        converted.Points.Add(sheetToBlock.Apply(point));
                    frame.BlockMarks.Add(converted);
                }
                if (frame.BlockMarks.Count == 0)
                {
                    frame.Failure = "không đổi được tọa độ sketch MARK sang block";
                    return frame;
                }

                frame.BlockBounds = bounds;
                frame.BlockPoints = points;
                frame.UsesSketchToSheet = choice.UsesSketchToSheet;
                frame.Verified = choice.Verified;
                frame.Method = choice.Method;
                frame.ViewBounds = choice.ViewBounds;
                frame.BlockToSheet = choice.Transform;
                frame.Ok = true;
                return frame;
            }
            catch (Exception ex)
            {
                frame.Ok = false;
                frame.Failure = "lỗi khi đọc block của view: " + ex.Message;
                return frame;
            }
            finally
            {
                SafeReleaseComObject(mathUtility);
            }
        }

        /// <summary>
        /// Looks for the MARK sketches of the view (lines, arcs, circles: the
        /// shapes SOLIDWORKS converts exactly) inside the block, once per way
        /// of reading the block on the sheet. A match gives the shift that
        /// lays the block on the view - exact, nothing assumed. Pure data.
        /// </summary>
        internal static List<SectionFrameAnchor> FindMarkAnchors(
            IList<SectionMarkGeometry.Shape> blockShapes,
            BlockFrameTransform blockToSketch,
            BlockFrameTransform sketchToSheet,
            DrawingViewMarkSketchCollection marks,
            out string note,
            out int carried)
        {
            note = "";
            carried = 0;
            var anchors = new List<SectionFrameAnchor>();
            List<DrawingViewMarkSketch> viewMarks = marks == null
                ? new List<DrawingViewMarkSketch>()
                : marks.Items.Where(mark => mark.FromView && mark.SheetPoints.Count >= 2).ToList();
            if (viewMarks.Count == 0)
            {
                note = "MARK trong block: không có sketch MARK vẽ trong view";
                return anchors;
            }
            if (blockShapes == null || blockShapes.Count == 0 || blockToSketch == null)
            {
                note = "MARK trong block: không đọc được hình của block";
                return anchors;
            }

            var sheet = new BlockFrameTransform();
            List<SectionMarkGeometry.Shape> markShapes = viewMarks
                .Select(mark => SectionMarkGeometry.FromMark(
                    new DxfInjectMark { Kind = mark.Kind, Points = mark.SheetPoints.ToList() },
                    sheet.Apply))
                .Where(shape => shape != null && shape.Kind != SectionMarkGeometry.ShapeKind.Curve)
                .ToList();
            if (markShapes.Count == 0)
            {
                note = "MARK trong block: sketch MARK chỉ có spline/ellipse, không dùng làm mốc";
                return anchors;
            }

            var bases = new List<KeyValuePair<bool, BlockFrameTransform>>
            {
                new KeyValuePair<bool, BlockFrameTransform>(false, blockToSketch)
            };
            if (sketchToSheet != null && !sketchToSheet.IsIdentity)
            {
                bases.Add(new KeyValuePair<bool, BlockFrameTransform>(
                    true, BlockFrameTransform.Compose(sketchToSheet, blockToSketch)));
            }

            var notes = new List<string>();
            foreach (KeyValuePair<bool, BlockFrameTransform> entry in bases)
            {
                List<SectionMarkGeometry.Shape> placed = blockShapes
                    .Select(shape => SectionMarkGeometry.Map(shape, entry.Value, BlockMarkSpacing))
                    .Where(shape => shape != null)
                    .ToList();
                SectionMarkGeometry.TranslationSearch search = SectionMarkGeometry.FindTranslation(
                    markShapes, placed, BlockMarkTolerance, BlockMarkSpacing);
                notes.Add((entry.Key ? "qua sketch: " : "") + search.Diagnostics);

                // marks + T = their copies in the block, so the block moved by -T lies on the view.
                if (search.Found)
                {
                    anchors.Add(new SectionFrameAnchor
                    {
                        UsesSketchToSheet = entry.Key,
                        ShiftX = -search.Translation[0],
                        ShiftY = -search.Translation[1],
                        Label = "khớp " + markShapes.Count + " sketch MARK nằm trong block"
                    });
                    carried = viewMarks.Count;
                }
                else if (search.AllSolutions.Count > 1)
                {
                    // Every position is kept; ChooseSectionBlockFrame takes
                    // one only if it alone agrees with a verified placement.
                    if (search.AllSolutions.Count > MaxAmbiguousAnchors)
                    {
                        notes.Add("quá nhiều vị trí khớp (" + search.AllSolutions.Count + "), không dùng");
                    }
                    else
                    {
                        foreach (double[] translation in search.AllSolutions)
                        {
                            anchors.Add(new SectionFrameAnchor
                            {
                                UsesSketchToSheet = entry.Key,
                                ShiftX = -translation[0],
                                ShiftY = -translation[1],
                                Label = "khớp " + markShapes.Count + " sketch MARK nằm trong block",
                                Ambiguous = true
                            });
                        }
                    }
                    carried = viewMarks.Count;
                }
            }
            note = "MARK trong block (" + markShapes.Count + " mốc): " + string.Join("; ", notes);
            return anchors;
        }

        /// <summary>
        /// The verified ways from the source sheet to the DXF for
        /// <see cref="DxfLayerFinalizer.PlaceSectionMarks"/>: the block chain
        /// (only when the block was tied to the view by a verified method) and
        /// the measured view geometry at 1:1 (every reading inside the view;
        /// the finalizer keeps those with the size of the DXF geometry and
        /// requires them to agree).
        /// </summary>
        internal static List<DxfLayerFinalizer.SectionMarkAlignment> BuildSectionMarkAlignments(
            SectionMarkFrame frame,
            SectionMarkPlacement placement,
            SectionViewGeometry viewGeometry,
            double[] outline,
            double viewScale)
        {
            var alignments = new List<DxfLayerFinalizer.SectionMarkAlignment>();
            if (frame != null && frame.Ok && frame.Verified && frame.BlockToSheet != null &&
                placement != null && placement.Ok && placement.BlockToTemporary != null &&
                placement.ExpectedBounds != null)
            {
                BlockFrameTransform sheetToBlock = frame.BlockToSheet.Inverse();
                if (sheetToBlock != null)
                {
                    alignments.Add(new DxfLayerFinalizer.SectionMarkAlignment
                    {
                        Label = "block của view (" + frame.Method + ")",
                        SheetToFrame = BlockFrameTransform.Compose(placement.BlockToTemporary, sheetToBlock),
                        FrameBounds = placement.ExpectedBounds
                    });
                }
            }

            if (viewGeometry != null && viewScale > 0)
            {
                double factor = 1.0 / viewScale;
                foreach (KeyValuePair<string, double[]> measured in viewGeometry.Bounds)
                {
                    if (outline != null && outline.Length >= 4 &&
                        !IsInsideBox(measured.Value, outline, BlockInsideOutlineTolerance))
                        continue;
                    alignments.Add(new DxfLayerFinalizer.SectionMarkAlignment
                    {
                        Label = "hình view gốc đo được [" + measured.Key + "]",
                        SheetToFrame = new BlockFrameTransform { A = factor, D = factor },
                        FrameBounds = measured.Value.Select(value => value * factor).ToArray()
                    });
                }
            }
            return alignments;
        }

        /// <summary>
        /// Step 2. Takes the sketches and the block extent to the coordinates
        /// of the temporary drawing that holds the pasted block. Never throws.
        /// </summary>
        internal static SectionMarkPlacement PlaceSectionMarksOnTemporary(
            IModelDoc2 temporaryModel,
            SectionMarkFrame frame)
        {
            var placement = new SectionMarkPlacement();
            MathUtility mathUtility = null;
            try
            {
                if (temporaryModel == null || frame == null || !frame.Ok)
                {
                    placement.Failure = "chưa có dữ liệu block của view gốc";
                    return placement;
                }
                mathUtility = DxfExportContext.App?.GetMathUtility() as MathUtility;
                if (mathUtility == null)
                {
                    placement.Failure = "không đọc được MathUtility";
                    return placement;
                }

                SketchBlockDefinition definition;
                SketchBlockInstance instance;
                int instanceCount;
                if (!TryFindPastedBlock(temporaryModel, out definition, out instance, out instanceCount))
                {
                    placement.Failure = "không thấy block đã dán trong Drawing tạm";
                    return placement;
                }

                double[] bounds;
                List<double[]> points;
                string boundsFailure;
                if (!TryReadBlockGeometry(definition, out bounds, out points, out boundsFailure))
                {
                    placement.Failure = "block trong Drawing tạm: " + boundsFailure;
                    return placement;
                }

                // Pasting must not change the block itself; a pure shift of
                // its frame is tolerated and applied to the sketches too.
                double widthGap = (bounds[2] - bounds[0]) -
                                  (frame.BlockBounds[2] - frame.BlockBounds[0]);
                double heightGap = (bounds[3] - bounds[1]) -
                                   (frame.BlockBounds[3] - frame.BlockBounds[1]);
                if (Math.Abs(widthGap) > 1e-5 || Math.Abs(heightGap) > 1e-5)
                {
                    placement.Failure =
                        "block dán vào Drawing tạm khác block gốc (" +
                        Mm(bounds[2] - bounds[0]) + " x " + Mm(bounds[3] - bounds[1]) +
                        " mm so với " + Mm(frame.BlockBounds[2] - frame.BlockBounds[0]) +
                        " x " + Mm(frame.BlockBounds[3] - frame.BlockBounds[1]) + " mm)";
                    return placement;
                }
                double shiftX = bounds[0] - frame.BlockBounds[0];
                double shiftY = bounds[1] - frame.BlockBounds[1];

                BlockFrameTransform transform =
                    ReadBlockTransform(instance.BlockToSketchTransform, mathUtility);
                if (transform == null)
                {
                    placement.Failure = "không đọc được phép biến đổi của block trong Drawing tạm";
                    return placement;
                }
                if (frame.UsesSketchToSheet)
                {
                    BlockFrameTransform sketchToSheet =
                        ReadInstanceSketchToSheet(instance, mathUtility);
                    if (sketchToSheet != null)
                        transform = BlockFrameTransform.Compose(sketchToSheet, transform);
                }

                foreach (DxfInjectMark mark in frame.BlockMarks)
                {
                    var moved = new DxfInjectMark { Kind = mark.Kind };
                    foreach (double[] point in mark.Points)
                    {
                        moved.Points.Add(transform.Apply(new[]
                        {
                            point[0] + shiftX,
                            point[1] + shiftY,
                            0.0
                        }));
                    }
                    placement.Marks.Add(moved);
                }
                placement.ExpectedBounds = BoundsOf(points.Select(transform.Apply));
                placement.BlockToTemporary = BlockFrameTransform.Compose(
                    transform, BlockFrameTransform.Translation(shiftX, shiftY));
                placement.Ok = true;
                placement.Diagnostics =
                    "block dán: " + (instanceCount > 1 ? instanceCount + " bản, lấy bản đầu; " : "") +
                    "tỷ lệ " + transform.ScaleFactor.ToString("0.####", CultureInfo.InvariantCulture) +
                    ", hình " + Mm(placement.ExpectedBounds[2] - placement.ExpectedBounds[0]) +
                    " x " + Mm(placement.ExpectedBounds[3] - placement.ExpectedBounds[1]) + " mm";
                return placement;
            }
            catch (Exception ex)
            {
                placement.Ok = false;
                placement.Failure = "lỗi khi đặt sketch MARK theo block: " + ex.Message;
                return placement;
            }
            finally
            {
                SafeReleaseComObject(mathUtility);
            }
        }

        private static string Mm(double metres)
        {
            return (metres * 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string FormatMillimetres(double millimetres)
        {
            return millimetres.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string FormatBounds(double[] bounds)
        {
            if (bounds == null || bounds.Length < 4) return "?";
            return Mm(bounds[0]) + ".." + Mm(bounds[2]) + " x " +
                   Mm(bounds[1]) + ".." + Mm(bounds[3]) + " mm";
        }

        private static double ViewGeometrySizeTolerance(double[] box)
        {
            double size = box == null || box.Length < 4
                ? 0
                : Math.Max(box[2] - box[0], box[3] - box[1]);
            return ViewGeometrySizeToleranceBase + ViewGeometrySizeToleranceRatio * size;
        }

        private static bool IsInsideBox(double[] inner, double[] outer, double tolerance)
        {
            return inner[0] >= outer[0] - tolerance &&
                   inner[1] >= outer[1] - tolerance &&
                   inner[2] <= outer[2] + tolerance &&
                   inner[3] <= outer[3] + tolerance;
        }

        private static double[] ShiftBox(double[] box, double x, double y)
        {
            return new[] { box[0] + x, box[1] + y, box[2] + x, box[3] + y };
        }

        private static bool SameBox(double[] first, double[] second, double tolerance)
        {
            for (int index = 0; index < 4; index++)
            {
                if (Math.Abs(first[index] - second[index]) > tolerance) return false;
            }
            return true;
        }

        private static double[] BoundsOf(IEnumerable<double[]> points)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (double[] point in points)
            {
                if (point == null) continue;
                if (point[0] < minX) minX = point[0];
                if (point[0] > maxX) maxX = point[0];
                if (point[1] < minY) minY = point[1];
                if (point[1] > maxY) maxY = point[1];
            }
            return new[] { minX, minY, maxX, maxY };
        }

        private static void AddMappedPoint(double[] bounds, double[] affine, double[] point)
        {
            double x = affine[0] * point[0] + affine[1] * point[1] + affine[2] * point[2] + affine[3];
            double y = affine[4] * point[0] + affine[5] * point[1] + affine[6] * point[2] + affine[7];
            if (!IsFiniteValue(x) || !IsFiniteValue(y)) return;
            if (x < bounds[0]) bounds[0] = x;
            if (y < bounds[1]) bounds[1] = y;
            if (x > bounds[2]) bounds[2] = x;
            if (y > bounds[3]) bounds[3] = y;
        }

        /// <summary>
        /// The arc of a type-1 polyline from its GeomData (centre, start,
        /// end, normal). The direction given by the normal is checked against
        /// the tessellated points; null when the data do not agree with the
        /// points (the points alone then give the extent).
        /// </summary>
        private static ViewPolylineArc BuildPolylineArc(double[] geometry, List<double[]> points)
        {
            if (geometry == null || geometry.Length < 12) return null;
            for (int index = 0; index < 12; index++)
            {
                if (!IsFiniteValue(geometry[index])) return null;
            }

            double[] center = { geometry[0], geometry[1], geometry[2] };
            double[] start = { geometry[3], geometry[4], geometry[5] };
            double[] end = { geometry[6], geometry[7], geometry[8] };
            double[] normal = VecNormalize(new[] { geometry[9], geometry[10], geometry[11] });
            double[] toStart = VecSub(start, center);
            double radius = VecLength(toStart);
            if (normal == null || !(radius > 1e-9)) return null;

            double[] u = VecScale(toStart, 1 / radius);
            if (Math.Abs(VecDot(u, normal)) > 1e-3) return null;
            double[] v = VecNormalize(VecCross(normal, u));
            if (v == null) return null;

            double[] toEnd = VecSub(end, center);
            if (Math.Abs(VecLength(toEnd) - radius) > 1e-7 + 1e-3 * radius) return null;

            double onCircle = 1e-7 + 2e-3 * radius;
            foreach (double[] point in points)
            {
                double[] offset = VecSub(point, center);
                double along = VecDot(offset, normal);
                double planar = VecLength(VecSub(offset, VecScale(normal, along)));
                if (Math.Abs(along) > onCircle || Math.Abs(planar - radius) > onCircle) return null;
            }

            bool full = VecLength(VecSub(end, start)) <= 1e-9 + 1e-7 * radius;
            var counterClockwise = new ViewPolylineArc
            {
                Center = center,
                U = u,
                V = v,
                Radius = radius,
                Sweep = full
                    ? 2 * Math.PI
                    : NormalizePositiveAngle(Math.Atan2(VecDot(toEnd, v), VecDot(toEnd, u)))
            };
            if (full) return counterClockwise;

            var clockwise = new ViewPolylineArc
            {
                Center = center,
                U = u,
                V = VecScale(v, -1),
                Radius = radius,
                Sweep = 2 * Math.PI - counterClockwise.Sweep
            };

            double endTolerance = 1e-7 + 1e-5 * radius;
            int interior = 0, onCounterClockwise = 0, onClockwise = 0;
            foreach (double[] point in points)
            {
                if (VecLength(VecSub(point, start)) <= endTolerance ||
                    VecLength(VecSub(point, end)) <= endTolerance)
                    continue;
                interior++;
                double[] offset = VecSub(point, center);
                if (ArcContains(counterClockwise, offset)) onCounterClockwise++;
                if (ArcContains(clockwise, offset)) onClockwise++;
            }
            if (interior == 0) return counterClockwise;
            if (onCounterClockwise == interior && onClockwise < interior) return counterClockwise;
            if (onClockwise == interior && onCounterClockwise < interior) return clockwise;
            return null;
        }

        private static bool ArcContains(ViewPolylineArc arc, double[] offset)
        {
            double angle = Math.Atan2(VecDot(offset, arc.V), VecDot(offset, arc.U));
            if (angle < 0) angle += 2 * Math.PI;
            return angle <= arc.Sweep + 1e-6;
        }

        private static bool TryReadPolylineCount(double[] data, ref int index, out int value)
        {
            value = 0;
            if (index >= data.Length) return false;
            double raw = data[index++];
            if (!IsFiniteValue(raw) || Math.Abs(raw - Math.Round(raw)) > 1e-6 ||
                raw < int.MinValue || raw > int.MaxValue)
                return false;
            value = (int)Math.Round(raw);
            return true;
        }

        private static double[] ToPolylineData(object raw)
        {
            if (raw is double[] doubles) return doubles;
            if (!(raw is Array array)) return null;
            var result = new double[array.Length];
            int index = 0;
            foreach (object item in array)
            {
                try { result[index++] = Convert.ToDouble(item, CultureInfo.InvariantCulture); }
                catch { return null; }
            }
            return result;
        }

        /// <summary>
        /// x' = m0 x + m1 y + m2 z + m3, y' = m4 x + m5 y + m6 z + m7 for a
        /// SOLIDWORKS transform whose result is on the sheet.
        /// </summary>
        private static double[] ReadAffineToSheet(MathTransform transform, MathUtility mathUtility)
        {
            if (transform == null || mathUtility == null) return null;
            double[] origin = ApplyTransform(new[] { 0.0, 0.0, 0.0 }, transform, mathUtility);
            double[] unitX = ApplyTransform(new[] { 1.0, 0.0, 0.0 }, transform, mathUtility);
            double[] unitY = ApplyTransform(new[] { 0.0, 1.0, 0.0 }, transform, mathUtility);
            double[] unitZ = ApplyTransform(new[] { 0.0, 0.0, 1.0 }, transform, mathUtility);
            if (origin == null || unitX == null || unitY == null || unitZ == null ||
                origin.Length < 2 || unitX.Length < 2 || unitY.Length < 2 || unitZ.Length < 2)
                return null;
            var affine = new[]
            {
                unitX[0] - origin[0], unitY[0] - origin[0], unitZ[0] - origin[0], origin[0],
                unitX[1] - origin[1], unitY[1] - origin[1], unitZ[1] - origin[1], origin[1]
            };
            foreach (double value in affine)
            {
                if (!IsFiniteValue(value)) return null;
            }
            return affine;
        }

        private static bool IsFiniteValue(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double VecDot(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        private static double[] VecCross(double[] a, double[] b)
        {
            return new[]
            {
                a[1] * b[2] - a[2] * b[1],
                a[2] * b[0] - a[0] * b[2],
                a[0] * b[1] - a[1] * b[0]
            };
        }

        private static double[] VecSub(double[] a, double[] b)
        {
            return new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        }

        private static double[] VecScale(double[] a, double factor)
        {
            return new[] { a[0] * factor, a[1] * factor, a[2] * factor };
        }

        private static double VecLength(double[] a)
        {
            return Math.Sqrt(VecDot(a, a));
        }

        private static double[] VecNormalize(double[] a)
        {
            double length = VecLength(a);
            return length > 1e-12 ? VecScale(a, 1 / length) : null;
        }

        private static BlockFrameTransform ReadBlockTransform(
            MathTransform transform,
            MathUtility mathUtility)
        {
            if (transform == null || mathUtility == null) return null;
            double[] origin = ApplyTransform(new[] { 0.0, 0.0, 0.0 }, transform, mathUtility);
            double[] unitX = ApplyTransform(new[] { 1.0, 0.0, 0.0 }, transform, mathUtility);
            double[] unitY = ApplyTransform(new[] { 0.0, 1.0, 0.0 }, transform, mathUtility);
            if (origin == null || unitX == null || unitY == null ||
                origin.Length < 2 || unitX.Length < 2 || unitY.Length < 2)
                return null;
            return new BlockFrameTransform
            {
                A = unitX[0] - origin[0],
                B = unitY[0] - origin[0],
                C = unitX[1] - origin[1],
                D = unitY[1] - origin[1],
                TX = origin[0],
                TY = origin[1]
            };
        }

        /// <summary>
        /// Frame of the sketch that holds the block instance, to the sheet.
        /// Null when it cannot be read (then the sketch frame is the sheet).
        /// </summary>
        private static BlockFrameTransform ReadInstanceSketchToSheet(
            SketchBlockInstance instance,
            MathUtility mathUtility)
        {
            ISketch sketch = null;
            MathTransform modelToSketch = null;
            MathTransform sketchToModel = null;
            try
            {
                sketch = instance.GetSketch() as ISketch;
                modelToSketch = sketch?.ModelToSketchTransform;
                sketchToModel = modelToSketch?.Inverse() as MathTransform;
                return ReadBlockTransform(sketchToModel, mathUtility);
            }
            catch
            {
                return null;
            }
            finally
            {
                SafeReleaseComObject(sketchToModel);
                SafeReleaseComObject(modelToSketch);
                SafeReleaseComObject(sketch);
            }
        }

        private static bool TryFindPastedBlock(
            IModelDoc2 model,
            out SketchBlockDefinition definition,
            out SketchBlockInstance instance,
            out int instanceCount)
        {
            definition = null;
            instance = null;
            instanceCount = 0;
            object[] definitions = model.SketchManager?.GetSketchBlockDefinitions() as object[];
            if (definitions == null) return false;

            foreach (object value in definitions)
            {
                SketchBlockDefinition candidate = value as SketchBlockDefinition;
                if (candidate == null) continue;
                object[] instances = candidate.GetInstances() as object[];
                if (instances == null) continue;
                foreach (object item in instances)
                {
                    SketchBlockInstance found = item as SketchBlockInstance;
                    if (found == null) continue;
                    instanceCount++;
                    if (instance == null)
                    {
                        definition = candidate;
                        instance = found;
                    }
                }
            }
            return instance != null;
        }

        /// <summary>
        /// Extent of the geometry of a block definition in its own frame:
        /// lines, arcs, circles, ellipses and splines (through their points).
        /// </summary>
        private static bool TryReadBlockGeometry(
            SketchBlockDefinition definition,
            out double[] bounds,
            out List<double[]> points,
            out string failure,
            List<SectionMarkGeometry.Shape> shapes = null)
        {
            bounds = null;
            points = new List<double[]>();
            failure = "";
            ISketch sketch = null;
            try
            {
                sketch = definition.GetSketch() as ISketch;
                object[] segments = sketch?.GetSketchSegments() as object[];
                if (segments == null || segments.Length == 0)
                {
                    failure = "block không có đường nào";
                    return false;
                }

                foreach (object value in segments)
                {
                    ISketchSegment segment = value as ISketchSegment;
                    if (segment == null) continue;

                    bool construction = false;
                    try { construction = segment.ConstructionGeometry; } catch { }
                    if (construction) continue;

                    SectionMarkGeometry.Shape shape;
                    List<double[]> sampled = ReadSegmentExtentPoints(segment, out shape);
                    if (sampled != null) points.AddRange(sampled);
                    if (shape != null && shapes != null) shapes.Add(shape);
                }
                if (points.Count < 2)
                {
                    failure = "không đọc được hình học của block";
                    return false;
                }

                bounds = BoundsOf(points);
                return true;
            }
            catch (Exception ex)
            {
                failure = "không đọc được hình học của block: " + ex.Message;
                return false;
            }
            finally
            {
                SafeReleaseComObject(sketch);
            }
        }

        /// <summary>
        /// Points that bound one block segment, and the segment as a shape
        /// (line / circle / arc exact; ellipse and spline as their points).
        /// </summary>
        private static List<double[]> ReadSegmentExtentPoints(
            ISketchSegment segment,
            out SectionMarkGeometry.Shape shape)
        {
            shape = null;
            int type;
            try { type = segment.GetType(); }
            catch { return null; }

            var result = new List<double[]>();
            switch (type)
            {
                case (int)swSketchSegments_e.swSketchLINE:
                {
                    var line = segment as ISketchLine;
                    double[] start = ReadSketchPoint(line?.GetStartPoint2());
                    double[] end = ReadSketchPoint(line?.GetEndPoint2());
                    if (start == null || end == null) return null;
                    result.Add(start);
                    result.Add(end);
                    shape = SectionMarkGeometry.Line(start, end);
                    return result;
                }
                case (int)swSketchSegments_e.swSketchARC:
                {
                    string kind;
                    List<double[]> arcPoints;
                    string reason;
                    if (!TryReadSketchArc(
                            segment as ISketchArc, segment,
                            out kind, out arcPoints, out reason))
                        return null;
                    if (kind == "CIRCLE")
                    {
                        double radius = Distance2D(arcPoints[0], arcPoints[1]);
                        double[] center = arcPoints[0];
                        result.Add(new[] { center[0] - radius, center[1] - radius, 0.0 });
                        result.Add(new[] { center[0] + radius, center[1] + radius, 0.0 });
                        shape = SectionMarkGeometry.Circle(center, radius);
                    }
                    else
                    {
                        // start, end, a point on the arc
                        result.AddRange(SampleThreePointArc(arcPoints[0], arcPoints[1], arcPoints[2]));
                        shape = SectionMarkGeometry.ArcThrough(arcPoints[0], arcPoints[1], arcPoints[2]);
                    }
                    return result;
                }
                case (int)swSketchSegments_e.swSketchELLIPSE:
                {
                    string kind;
                    List<double[]> ellipsePoints;
                    string reason;
                    if (!TryReadSketchEllipse(
                            segment as ISketchEllipse, segment,
                            out kind, out ellipsePoints, out reason))
                        return null;
                    result.AddRange(ellipsePoints);
                    shape = SectionMarkGeometry.Curve(ellipsePoints);
                    return result;
                }
                case (int)swSketchSegments_e.swSketchSPLINE:
                {
                    if ((segment as ISketchSpline)?.GetPoints2() is object[] splinePoints)
                    {
                        foreach (object splinePoint in splinePoints)
                        {
                            double[] point = ReadSketchPoint(splinePoint);
                            if (point != null) result.Add(point);
                        }
                    }
                    shape = SectionMarkGeometry.Curve(result);
                    return result;
                }
                default:
                    return result;
            }
        }
    }
}
