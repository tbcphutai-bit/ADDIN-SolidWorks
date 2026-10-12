// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>
    /// MARK sketches of a Section view (View -> DXF), applied to the saved DXF.
    ///
    /// The MARK sketches are left in the view when it becomes a block, so
    /// SOLIDWORKS converts and exports them WITH the geometry, through exactly
    /// the same transforms. They are then recognised in the DXF by their
    /// shape (one translation must lay every MARK sketch, scaled 1/view
    /// scale, exactly on entities that lie on it) and moved to LASER_MARK.
    /// No coordinate frame is assumed, so nothing can drift.
    ///
    /// When SOLIDWORKS did not carry them, the position comes only from
    /// alignments that were VERIFIED on the source side (block tied to the
    /// view, geometry measured) and are checked here against the size of the
    /// geometry in the DXF; several alignments must agree. Without any of
    /// these nothing is written: a mark at a guessed place is worse than a
    /// clear error.
    /// </summary>
    internal static partial class DxfLayerFinalizer
    {
        // DXF millimetres.
        private const double SectionMarkToleranceMm = 0.02;
        private const double SectionMarkCurveToleranceMm = 0.05;
        private const double SectionMarkSpacingMm = 0.5;
        private const double SectionPlanAgreementMm = 0.05;

        /// <summary>
        /// A verified way from the source sheet to a frame whose geometry
        /// bounds are known (the temporary drawing, or the view at 1:1); the
        /// DXF is that frame in millimetres plus an offset found from the size.
        /// </summary>
        internal sealed class SectionMarkAlignment
        {
            public string Label = "";

            /// <summary>Source sheet metres -> frame metres.</summary>
            public DxfOutPut.BlockFrameTransform SheetToFrame;

            /// <summary>minX, minY, maxX, maxY of the exported geometry in that frame (metres).</summary>
            public double[] FrameBounds;
        }

        private sealed class DxfWorldEntity
        {
            public DxfRecord Record;
            public bool InBlock;

            /// <summary>The block is inserted more than once: its entities are never edited.</summary>
            public bool Shared;
            public string Layer = "";
            public readonly List<SectionMarkGeometry.Shape> Shapes =
                new List<SectionMarkGeometry.Shape>();
        }

        /// <summary>
        /// Puts the MARK sketches of a Section view on LASER_MARK in
        /// <paramref name="filePath"/>.
        /// </summary>
        /// <param name="marks">All MARK sketches, source sheet metres.</param>
        /// <param name="carried">Per mark: drawn in the view, so SOLIDWORKS may have exported it with the geometry.</param>
        /// <param name="dxfUnitsPerSheetMetre">1000 / view scale.</param>
        /// <param name="alignments">Verified alignments, most trusted first.</param>
        internal static DxfMarkInjectionResult PlaceSectionMarks(
            string filePath,
            IList<DxfInjectMark> marks,
            IList<bool> carried,
            double dxfUnitsPerSheetMetre,
            IList<SectionMarkAlignment> alignments,
            DxfManufacturingLayerSettings layerSettings)
        {
            var result = new DxfMarkInjectionResult { Exact = true };
            var notes = new List<string>();
            if (marks == null || marks.Count == 0)
            {
                result.Message = "không có sketch MARK để ghi";
                return result;
            }
            if (!(dxfUnitsPerSheetMetre > 0) || double.IsInfinity(dxfUnitsPerSheetMetre))
            {
                result.Message = "không đọc được tỷ lệ view";
                return result;
            }

            DxfManufacturingLayerSettings settings =
                DxfManufacturingLayerSettings.Normalize(layerSettings);
            List<string> lines;
            Encoding encoding;
            string loadMessage;
            if (!TryLoadDxfLines(filePath, out lines, out encoding, out loadMessage))
            {
                result.Message = loadMessage;
                return result;
            }

            List<DxfWorldEntity> entities = CollectWorldEntities(lines);
            var targets = new List<SectionMarkGeometry.Shape>();
            foreach (DxfWorldEntity entity in entities)
            {
                if (IsLaserLayer(entity.Layer, settings)) continue;
                targets.AddRange(entity.Shapes);
            }

            double k = dxfUnitsPerSheetMetre;
            DxfOutPut.BlockFrameTransform plan = null;

            var carriedIndexes = new List<int>();
            for (int index = 0; index < marks.Count; index++)
            {
                if (IsCarried(carried, index))
                    carriedIndexes.Add(index);
            }

            // Verified alignments of the whole geometry: the fallback, and
            // the expected place used to choose among several exact positions
            // of the carried marks.
            var alignmentNotes = new List<string>();
            var alignmentResult = new DxfMarkInjectionResult();
            DxfOutPut.BlockFrameTransform aligned = ChooseSectionAlignment(
                lines, marks, carried, alignments, alignmentNotes, alignmentResult);

            // 1) The MARK sketches exported by SOLIDWORKS with the geometry.
            if (carriedIndexes.Count > 0)
            {
                var scale = new DxfOutPut.BlockFrameTransform { A = k, D = k };
                List<SectionMarkGeometry.Shape> markShapes = carriedIndexes
                    .Select(index => SectionMarkGeometry.FromMark(marks[index], scale.Apply))
                    .ToList();
                if (markShapes.Any(shape => shape == null))
                {
                    notes.Add("MARK đi cùng hình: có sketch không dựng được hình để so");
                }
                else
                {
                    // Lines, arcs and circles are exported exactly; splines
                    // and ellipses are only known through their samples, so
                    // they position the marks only when nothing else can.
                    List<SectionMarkGeometry.Shape> exact = markShapes
                        .Where(shape => shape.Kind != SectionMarkGeometry.ShapeKind.Curve)
                        .ToList();
                    List<SectionMarkGeometry.Shape> searchShapes = exact.Count > 0 ? exact : markShapes;
                    double tolerance = searchShapes.Max(SectionMarkTolerance);

                    double[] preferred;
                    double preferredRadius = 0;
                    if (TryGetScaledTranslation(aligned, k, out preferred))
                        preferredRadius = SectionPlanAgreementMm + alignmentResult.ExtentErrorMm;

                    SectionMarkGeometry.TranslationSearch search = SectionMarkGeometry.FindTranslation(
                        searchShapes, targets, tolerance, SectionMarkSpacingMm, preferred, preferredRadius);
                    notes.Add("MARK đi cùng hình: " + search.Diagnostics);
                    if (search.Found)
                    {
                        plan = new DxfOutPut.BlockFrameTransform
                        {
                            A = k,
                            D = k,
                            TX = search.Translation[0],
                            TY = search.Translation[1]
                        };
                        result.Method = "carried";
                        if (preferred != null)
                        {
                            double gap = SectionMarkGeometry.Distance(search.Translation, preferred);
                            if (gap > preferredRadius)
                            {
                                notes.Add("CHÚ Ý: " + alignmentResult.Method + " lệch " + FormatMm(gap) +
                                          " mm so với MARK đi cùng hình (dùng MARK đi cùng hình)");
                            }
                        }
                    }
                }
            }

            // 2) Verified alignments of the whole geometry.
            notes.AddRange(alignmentNotes);
            if (plan == null && aligned != null)
            {
                plan = aligned;
                result.Method = alignmentResult.Method;
                result.ExtentErrorMm = alignmentResult.ExtentErrorMm;
            }

            result.Diagnostics = string.Join("; ", notes);
            if (plan == null)
            {
                result.Message = result.Diagnostics.Length > 0
                    ? result.Diagnostics
                    : "không có cách đặt nào đã kiểm chứng";
                return result;
            }

            // 3) Exported copies -> LASER_MARK; the other marks are written.
            var relayer = new HashSet<DxfRecord>();
            var remove = new HashSet<DxfRecord>();
            var toWrite = new List<DxfInjectMark>();
            int relayeredMarks = 0;
            for (int index = 0; index < marks.Count; index++)
            {
                DxfInjectMark mark = marks[index];
                SectionMarkGeometry.Shape shape = SectionMarkGeometry.FromMark(mark, plan.Apply);
                if (shape != null && IsCarried(carried, index))
                {
                    DxfWorldEntity copy = FindExportedCopy(entities, shape, settings);
                    if (copy != null)
                    {
                        result.MarksFromCopies++;
                        if (!copy.InBlock &&
                            GetGroupValue(lines, copy.Record.Start, copy.Record.EndExclusive, "8") != null)
                        {
                            relayer.Add(copy.Record);
                            relayeredMarks++;
                            continue;
                        }
                        // A copy inside the block definition: removed there,
                        // written again at top level at the same place.
                        remove.Add(copy.Record);
                    }
                }
                toWrite.Add(mark);
            }

            foreach (DxfRecord record in relayer.Where(record => !remove.Contains(record)))
                SetGroupValue(lines, record.Start, record.EndExclusive, "8", settings.LaserMark.Name);
            foreach (DxfRecord record in remove.OrderByDescending(record => record.Start))
                lines.RemoveRange(record.Start, record.EndExclusive - record.Start);
            result.CopiesRelayered = relayer.Count(record => !remove.Contains(record));
            result.CopiesRemoved = remove.Count;

            bool changed = result.CopiesRelayered > 0 || result.CopiesRemoved > 0;
            if (!WriteMarksIntoDxf(filePath, lines, encoding, toWrite, plan.Apply,
                    k / 1000.0, settings, result, changed))
                return result;

            result.MarksWritten += relayeredMarks;
            result.Applied = result.MarksWritten > 0;
            return result;
        }

        /// <summary>
        /// The verified alignment to use, checked against the size of the
        /// geometry in the DXF. All alignments that pass must put the marks
        /// at the same place, otherwise none is used.
        /// </summary>
        private static DxfOutPut.BlockFrameTransform ChooseSectionAlignment(
            List<string> lines,
            IList<DxfInjectMark> marks,
            IList<bool> carried,
            IList<SectionMarkAlignment> alignments,
            List<string> notes,
            DxfMarkInjectionResult result)
        {
            if (alignments == null || alignments.Count == 0)
            {
                notes.Add("không có cách đặt nào đã kiểm chứng (block chưa nối được với view)");
                return null;
            }

            DxfBounds bounds;
            string reason;
            if (!TryGetGeometryBounds(lines, out bounds, out reason))
            {
                notes.Add("không đo được hình trong DXF (" + reason + ")");
                return null;
            }
            double dxfWidth = bounds.MaxX - bounds.MinX;
            double dxfHeight = bounds.MaxY - bounds.MinY;

            var passing = new List<KeyValuePair<SectionMarkAlignment, DxfOutPut.BlockFrameTransform>>();
            var errors = new List<double>();
            foreach (SectionMarkAlignment alignment in alignments)
            {
                if (alignment?.SheetToFrame == null || alignment.FrameBounds == null ||
                    alignment.FrameBounds.Length < 4)
                    continue;
                // The marks left in the view are exported with the geometry
                // and count in the DXF extent; the measured view geometry
                // (model edges) does not hold them, the block may. The frame
                // with them and the frame without them are both tried, the
                // closer one is used.
                double[] frame = alignment.FrameBounds;
                double[] withMarks = WithCarriedMarks(frame, marks, carried, alignment.SheetToFrame);
                double error = SizeErrorMm(frame, dxfWidth, dxfHeight);
                if (withMarks != null)
                {
                    double errorWithMarks = SizeErrorMm(withMarks, dxfWidth, dxfHeight);
                    if (errorWithMarks < error)
                    {
                        frame = withMarks;
                        error = errorWithMarks;
                    }
                }
                double width = (frame[2] - frame[0]) * 1000.0;
                double height = (frame[3] - frame[1]) * 1000.0;
                if (error > ExactSizeToleranceMm(width, height))
                {
                    notes.Add(alignment.Label + ": hình " + FormatMm(width) + " x " + FormatMm(height) +
                              " mm không khớp hình DXF " + FormatMm(dxfWidth) + " x " +
                              FormatMm(dxfHeight) + " mm");
                    continue;
                }

                var toDxf = new DxfOutPut.BlockFrameTransform
                {
                    A = 1000.0,
                    D = 1000.0,
                    TX = (bounds.MinX + bounds.MaxX) / 2 - (frame[0] + frame[2]) / 2 * 1000.0,
                    TY = (bounds.MinY + bounds.MaxY) / 2 - (frame[1] + frame[3]) / 2 * 1000.0
                };
                passing.Add(new KeyValuePair<SectionMarkAlignment, DxfOutPut.BlockFrameTransform>(
                    alignment,
                    DxfOutPut.BlockFrameTransform.Compose(toDxf, alignment.SheetToFrame)));
                errors.Add(error);
            }
            if (passing.Count == 0) return null;

            // Two centre alignments of boxes whose sizes differ by e1, e2 can
            // differ by (e1 + e2) / 2 without either being wrong.
            double worst = 0;
            bool disagree = false;
            for (int other = 1; other < passing.Count; other++)
            {
                double allowed = SectionPlanAgreementMm + (errors[0] + errors[other]) / 2;
                foreach (DxfInjectMark mark in marks)
                {
                    foreach (double[] point in mark.Points)
                    {
                        double[] first = passing[0].Value.Apply(point);
                        double[] second = passing[other].Value.Apply(point);
                        double gap = SectionMarkGeometry.Distance(first, second);
                        worst = Math.Max(worst, gap);
                        if (gap > allowed) disagree = true;
                    }
                }
            }
            if (disagree)
            {
                notes.Add("các cách đặt đã kiểm chứng lệch nhau " + FormatMm(worst) + " mm (" +
                          string.Join(", ", passing.Select(pair => pair.Key.Label)) + ") nên không ghi");
                return null;
            }

            result.Method = passing[0].Key.Label;
            result.ExtentErrorMm = errors[0];
            if (passing.Count > 1)
                notes.Add(passing.Count + " cách đặt cùng kết quả (lệch tối đa " + FormatMm(worst) + " mm)");
            return passing[0].Value;
        }

        /// <summary>
        /// The copy SOLIDWORKS exported of <paramref name="mark"/> (DXF frame):
        /// ONE entity that lies on the mark and covers all of it, as each
        /// sketch segment is exported as one entity. A model edge that only
        /// overlaps part of the mark (collinear, shorter) is never taken - it
        /// must stay on its cutting layer. Entities on a MARK layer are
        /// preferred over an identical model edge.
        /// </summary>
        private static DxfWorldEntity FindExportedCopy(
            List<DxfWorldEntity> entities,
            SectionMarkGeometry.Shape mark,
            DxfManufacturingLayerSettings settings)
        {
            double tolerance = SectionMarkTolerance(mark);
            DxfWorldEntity fallback = null;
            foreach (DxfWorldEntity entity in entities)
            {
                if (entity.Shared || IsLaserLayer(entity.Layer, settings)) continue;
                if (!entity.Shapes.All(part => SectionMarkGeometry.IsContained(
                        part, mark, tolerance, SectionMarkSpacingMm)))
                    continue;
                if (!SectionMarkGeometry.IsCovered(mark, entity.Shapes, tolerance, SectionMarkSpacingMm))
                    continue;
                if (LaserMarkFeatureRecognizer.HasMarkToken(entity.Layer)) return entity;
                if (fallback == null) fallback = entity;
            }
            return fallback;
        }

        private static bool IsCarried(IList<bool> carried, int index)
        {
            return carried != null && index < carried.Count && carried[index];
        }

        /// <summary>
        /// DXF tolerance for a mark: exact shapes 0.02 mm; a spline or ellipse
        /// is a polyline of samples, off the true curve by up to its sagitta.
        /// </summary>
        private static double SectionMarkTolerance(SectionMarkGeometry.Shape mark)
        {
            if (mark == null || mark.Kind != SectionMarkGeometry.ShapeKind.Curve || mark.Points == null)
                return SectionMarkToleranceMm;
            double longest = 0;
            for (int index = 1; index < mark.Points.Count; index++)
                longest = Math.Max(longest, SectionMarkGeometry.Distance(mark.Points[index - 1], mark.Points[index]));
            return Math.Max(SectionMarkCurveToleranceMm, 0.025 * longest);
        }

        /// <summary>
        /// The translation of <paramref name="plan"/> when it is p -> k p + T
        /// (no rotation, no mirror), the form of the carried-mark plan.
        /// </summary>
        private static bool TryGetScaledTranslation(
            DxfOutPut.BlockFrameTransform plan,
            double k,
            out double[] translation)
        {
            translation = null;
            if (plan == null) return false;
            double epsilon = 1e-6 * k;
            if (Math.Abs(plan.A - k) > epsilon || Math.Abs(plan.D - k) > epsilon ||
                Math.Abs(plan.B) > epsilon || Math.Abs(plan.C) > epsilon)
                return false;
            translation = new[] { plan.TX, plan.TY, 0.0 };
            return true;
        }

        /// <summary>
        /// <paramref name="frame"/> grown by the carried marks mapped into it,
        /// or null when there are none.
        /// </summary>
        private static double[] WithCarriedMarks(
            double[] frame,
            IList<DxfInjectMark> marks,
            IList<bool> carried,
            DxfOutPut.BlockFrameTransform sheetToFrame)
        {
            double[] grown = null;
            for (int index = 0; index < marks.Count; index++)
            {
                if (!IsCarried(carried, index)) continue;
                SectionMarkGeometry.Shape shape = SectionMarkGeometry.FromMark(marks[index], sheetToFrame.Apply);
                if (shape?.Bounds == null) continue;
                if (grown == null) grown = (double[])frame.Clone();
                grown[0] = Math.Min(grown[0], shape.Bounds[0]);
                grown[1] = Math.Min(grown[1], shape.Bounds[1]);
                grown[2] = Math.Max(grown[2], shape.Bounds[2]);
                grown[3] = Math.Max(grown[3], shape.Bounds[3]);
            }
            return grown;
        }

        /// <summary>Size difference (mm) between a frame box (metres) and the DXF geometry.</summary>
        private static double SizeErrorMm(double[] frame, double dxfWidth, double dxfHeight)
        {
            double width = (frame[2] - frame[0]) * 1000.0;
            double height = (frame[3] - frame[1]) * 1000.0;
            return Math.Max(Math.Abs(width - dxfWidth), Math.Abs(height - dxfHeight));
        }

        private static bool IsLaserLayer(string layer, DxfManufacturingLayerSettings settings)
        {
            return string.Equals(layer, settings.LaserMark.Name, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------
        // Every geometry entity of the DXF in world coordinates, including
        // the entities of inserted blocks.
        // ------------------------------------------------------------------

        private static List<DxfWorldEntity> CollectWorldEntities(List<string> lines)
        {
            var result = new List<DxfWorldEntity>();
            Dictionary<string, DxfBlockDefinition> blocks = CollectBlockDefinitions(lines);
            List<DxfRecord> records = CollectEntityRecords(lines);

            var insertCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (DxfRecord record in records)
            {
                if (!IsDxfType(record, "INSERT")) continue;
                string name = GetGroupValue(lines, record.Start, record.EndExclusive, "2") ?? "";
                int count;
                insertCounts[name] = insertCounts.TryGetValue(name, out count) ? count + 1 : 1;
            }

            foreach (DxfRecord record in records)
            {
                if (IsDxfType(record, "INSERT"))
                {
                    AddInsertedEntities(lines, record, blocks, insertCounts, result);
                    continue;
                }
                var entity = new DxfWorldEntity
                {
                    Record = record,
                    Layer = GetGroupValue(lines, record.Start, record.EndExclusive, "8") ?? "0"
                };
                ReadEntityShapes(lines, record, entity, null);
                if (entity.Shapes.Count > 0) result.Add(entity);
            }
            return result;
        }

        private static void AddInsertedEntities(
            List<string> lines,
            DxfRecord insert,
            Dictionary<string, DxfBlockDefinition> blocks,
            Dictionary<string, int> insertCounts,
            List<DxfWorldEntity> result)
        {
            string name = GetGroupValue(lines, insert.Start, insert.EndExclusive, "2");
            DxfBlockDefinition definition;
            if (string.IsNullOrEmpty(name) || !blocks.TryGetValue(name, out definition)) return;

            double ix, iy, parsed;
            double sx = 1, sy = 1, rotation = 0;
            TryGetDoubleGroupValue(lines, insert.Start, insert.EndExclusive, "10", out ix);
            TryGetDoubleGroupValue(lines, insert.Start, insert.EndExclusive, "20", out iy);
            if (TryGetDoubleGroupValue(lines, insert.Start, insert.EndExclusive, "41", out parsed)) sx = parsed;
            if (TryGetDoubleGroupValue(lines, insert.Start, insert.EndExclusive, "42", out parsed)) sy = parsed;
            if (TryGetDoubleGroupValue(lines, insert.Start, insert.EndExclusive, "50", out parsed)) rotation = parsed;
            double cos = Math.Cos(rotation * Math.PI / 180.0);
            double sin = Math.Sin(rotation * Math.PI / 180.0);

            // p' = R(rotation) * S(sx, sy) * (p - base) + insertion point
            var map = new DxfOutPut.BlockFrameTransform
            {
                A = sx * cos,
                B = -sy * sin,
                C = sx * sin,
                D = sy * cos
            };
            map.TX = ix - (map.A * definition.BaseX + map.B * definition.BaseY);
            map.TY = iy - (map.C * definition.BaseX + map.D * definition.BaseY);

            int inserts;
            bool shared = insertCounts.TryGetValue(name, out inserts) && inserts > 1;
            string insertLayer = GetGroupValue(lines, insert.Start, insert.EndExclusive, "8") ?? "0";
            foreach (DxfRecord record in definition.Entities)
            {
                if (IsDxfType(record, "INSERT")) continue;
                string layer = GetGroupValue(lines, record.Start, record.EndExclusive, "8") ?? "0";
                var entity = new DxfWorldEntity
                {
                    Record = record,
                    InBlock = true,
                    Shared = shared,
                    Layer = layer == "0" ? insertLayer : layer
                };
                ReadEntityShapes(lines, record, entity, map);
                if (entity.Shapes.Count > 0) result.Add(entity);
            }
        }

        /// <summary>Shapes of one geometry entity, through <paramref name="map"/> when given.</summary>
        private static void ReadEntityShapes(
            List<string> lines,
            DxfRecord record,
            DxfWorldEntity entity,
            DxfOutPut.BlockFrameTransform map)
        {
            var shapes = new List<SectionMarkGeometry.Shape>();
            int start = record.Start;
            int end = record.EndExclusive;
            try
            {
                switch ((record.Type ?? "").Trim().ToUpperInvariant())
                {
                    case "LINE":
                    {
                        double x1, y1, x2, y2;
                        if (TryGetDoubleGroupValue(lines, start, end, "10", out x1) &&
                            TryGetDoubleGroupValue(lines, start, end, "20", out y1) &&
                            TryGetDoubleGroupValue(lines, start, end, "11", out x2) &&
                            TryGetDoubleGroupValue(lines, start, end, "21", out y2))
                            shapes.Add(SectionMarkGeometry.Line(new[] { x1, y1, 0.0 }, new[] { x2, y2, 0.0 }));
                        break;
                    }
                    case "CIRCLE":
                    case "ARC":
                    {
                        double cx, cy, radius;
                        if (!TryGetDoubleGroupValue(lines, start, end, "10", out cx) ||
                            !TryGetDoubleGroupValue(lines, start, end, "20", out cy) ||
                            !TryGetDoubleGroupValue(lines, start, end, "40", out radius) ||
                            !(radius > 0))
                            break;
                        double mirror = GetNormalZ(lines, start, end) < 0 ? -1 : 1;
                        if (IsDxfType(record, "CIRCLE"))
                        {
                            shapes.Add(SectionMarkGeometry.Circle(new[] { cx * mirror, cy, 0.0 }, radius));
                            break;
                        }
                        double startDegrees, endDegrees;
                        if (!TryGetDoubleGroupValue(lines, start, end, "50", out startDegrees) ||
                            !TryGetDoubleGroupValue(lines, start, end, "51", out endDegrees))
                            break;
                        double sweepDegrees = endDegrees - startDegrees;
                        while (sweepDegrees <= 0) sweepDegrees += 360;
                        while (sweepDegrees > 360) sweepDegrees -= 360;
                        double from = startDegrees * Math.PI / 180.0;
                        double sweep = sweepDegrees * Math.PI / 180.0;
                        // Three points in the entity frame, mirrored to world.
                        Func<double, double[]> at = angle => new[]
                        {
                            (cx + radius * Math.Cos(angle)) * mirror,
                            cy + radius * Math.Sin(angle),
                            0.0
                        };
                        shapes.Add(SectionMarkGeometry.ArcThrough(at(from), at(from + sweep), at(from + sweep / 2)));
                        break;
                    }
                    case "LWPOLYLINE":
                    {
                        List<LightweightVertex> vertices;
                        bool closed;
                        double normalZ;
                        if (!TryReadLightweightPolyline(lines, record, out vertices, out closed, out normalZ))
                            break;
                        double mirror = normalZ < 0 ? -1 : 1;
                        int count = closed ? vertices.Count : vertices.Count - 1;
                        for (int index = 0; index < count; index++)
                        {
                            LightweightVertex first = vertices[index];
                            LightweightVertex second = vertices[(index + 1) % vertices.Count];
                            shapes.Add(BulgeShape(first, second, mirror));
                        }
                        break;
                    }
                    case "SPLINE":
                    case "ELLIPSE":
                    {
                        List<List<DxfPoint2D>> polylines;
                        if (!TryTessellateEntity(lines, record, 0.002, SectionMarkSpacingMm, out polylines))
                            break;
                        foreach (List<DxfPoint2D> polyline in polylines)
                        {
                            shapes.Add(SectionMarkGeometry.Curve(polyline
                                .Select(point => new[] { point.X, point.Y, 0.0 })
                                .ToList()));
                        }
                        break;
                    }
                }
            }
            catch (Exception)
            {
                shapes.Clear();
            }

            foreach (SectionMarkGeometry.Shape shape in shapes)
            {
                SectionMarkGeometry.Shape placed = map == null
                    ? shape
                    : SectionMarkGeometry.Map(shape, map, SectionMarkSpacingMm);
                if (placed == null) continue;
                placed.Tag = entity;
                entity.Shapes.Add(placed);
            }
        }

        /// <summary>One LWPOLYLINE segment (bulge = tan(sweep / 4), counter-clockwise when positive).</summary>
        private static SectionMarkGeometry.Shape BulgeShape(
            LightweightVertex first,
            LightweightVertex second,
            double mirror)
        {
            var start = new[] { first.X * mirror, first.Y, 0.0 };
            var end = new[] { second.X * mirror, second.Y, 0.0 };
            double dx = second.X - first.X;
            double dy = second.Y - first.Y;
            double chord = Math.Sqrt(dx * dx + dy * dy);
            if (Math.Abs(first.Bulge) < 1e-12 || chord <= 1e-12)
                return SectionMarkGeometry.Line(start, end);

            double sweep = 4 * Math.Atan(first.Bulge);
            double centerOffset = (chord / 2) / Math.Tan(sweep / 2);
            double centerX = (first.X + second.X) / 2 - dy / chord * centerOffset;
            double centerY = (first.Y + second.Y) / 2 + dx / chord * centerOffset;
            double radius = Math.Sqrt((first.X - centerX) * (first.X - centerX) +
                                      (first.Y - centerY) * (first.Y - centerY));
            double middle = Math.Atan2(first.Y - centerY, first.X - centerX) + sweep / 2;
            var through = new[]
            {
                (centerX + radius * Math.Cos(middle)) * mirror,
                centerY + radius * Math.Sin(middle),
                0.0
            };
            return SectionMarkGeometry.ArcThrough(start, end, through);
        }
    }
}
