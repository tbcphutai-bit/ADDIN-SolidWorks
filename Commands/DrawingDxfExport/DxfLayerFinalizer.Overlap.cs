// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>
    /// "Keep only the LASER_MARK line": any CUT/BEND/layer-0 geometry lying
    /// under LASER_MARK geometry is removed (or, for a straight line that is
    /// only partly covered, trimmed to the uncovered pieces).
    ///
    /// The previous implementation matched rounded coordinate keys, so an
    /// overlap survived whenever the two copies were not written identically:
    /// a circle exported as two arcs, a model edge split differently from its
    /// marker, a spline/text mark, or a coordinate on a rounding boundary.
    /// This pass compares geometry within a tolerance instead.
    /// </summary>
    internal static partial class DxfLayerFinalizer
    {
        // DXF output is in millimetres.
        private const double OverlapTolerance = 0.02;
        private const double LaserChordTolerance = 0.004;
        private const double MinimumTrimPieceLength = 0.05;
        private const double CandidateSampleSpacing = 0.25;
        private const double ParallelSineTolerance = 0.0087; // ~0.5 degree
        private const int MaxTessellationPoints = 4096;

        internal sealed class OverlapCleanupResult
        {
            public int RemovedCount { get; set; }
            public int TrimmedCount { get; set; }
            public int PartialCount { get; set; }
        }

        private sealed class LaserSegment
        {
            public DxfLineSegment2D Segment;
            public bool IsStraight;
            /// <summary>Chỉ số nét sở hữu (chỉ bước gỡ LASER_MARK chồng LASER_MARK dùng).</summary>
            public int Owner;
        }

        private sealed class OverlapEdit
        {
            public DxfRecord Record;
            public List<DxfLineSegment2D> RemainingPieces;
        }

        /// <param name="coverWithLaserMarks">
        /// false = only the erase layer covers (View -> DXF without Cut-Extrude
        /// markers keeps its old behaviour for user sketches drawn on edges).
        /// </param>
        private static OverlapCleanupResult RemoveGeometryCoveredByLaserMarks(
            List<string> lines,
            DxfManufacturingLayerSettings settings,
            bool coverWithLaserMarks = true)
        {
            var result = new OverlapCleanupResult();
            List<DxfRecord> records = CollectEntityRecords(lines);
            var laserSegments = new List<LaserSegment>();
            var candidates = new List<DxfRecord>();

            foreach (DxfRecord record in records)
            {
                if (!IsOverlapGeometryType(record.Type)) continue;

                string layer = GetGroupValue(
                    lines,
                    record.Start,
                    record.EndExclusive,
                    "8") ?? "0";
                string canonical = MapCanonicalLayer(layer, settings);
                // An erased inner MARK rim covers like a LASER_MARK entity:
                // the CUT copy under it must go too.
                bool erase = string.Equals(
                    layer,
                    MarkEraseLayerName,
                    StringComparison.OrdinalIgnoreCase);
                bool laser = string.Equals(
                    canonical,
                    settings.LaserMark.Name,
                    StringComparison.OrdinalIgnoreCase);
                if (erase || laser)
                {
                    if (erase || coverWithLaserMarks)
                        AddLaserSegments(lines, record, laserSegments);
                }
                else if (string.Equals(
                             canonical,
                             settings.Cut.Name,
                             StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(
                             canonical,
                             settings.Bend.Name,
                             StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(
                             layer,
                             "0",
                             StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(record);
                }
            }

            if (laserSegments.Count == 0 || candidates.Count == 0)
                return result;

            var index = new LaserSegmentIndex(laserSegments, OverlapTolerance);
            var edits = new List<OverlapEdit>();
            foreach (DxfRecord candidate in candidates)
            {
                if (string.Equals(
                        candidate.Type,
                        "LINE",
                        StringComparison.OrdinalIgnoreCase))
                {
                    DxfLineSegment2D line;
                    if (!TryGetLineSegment(lines, candidate, out line))
                        continue;

                    bool covered;
                    List<DxfLineSegment2D> pieces =
                        GetUncoveredLinePieces(line, index, out covered);
                    if (!covered) continue;

                    edits.Add(new OverlapEdit
                    {
                        Record = candidate,
                        RemainingPieces = pieces
                    });
                    continue;
                }

                List<List<DxfPoint2D>> samples;
                if (!TryTessellateEntity(
                        lines,
                        candidate,
                        OverlapTolerance,
                        CandidateSampleSpacing,
                        out samples))
                    continue;

                int total = 0;
                int near = 0;
                foreach (List<DxfPoint2D> polyline in samples)
                {
                    foreach (DxfPoint2D point in polyline)
                    {
                        total++;
                        if (index.IsNear(point, OverlapTolerance)) near++;
                    }
                }

                if (total == 0) continue;
                if (near == total)
                {
                    edits.Add(new OverlapEdit
                    {
                        Record = candidate,
                        RemainingPieces = new List<DxfLineSegment2D>()
                    });
                }
                else if (near * 2 >= total)
                {
                    result.PartialCount++;
                }
            }

            if (edits.Count == 0) return result;

            var handles = new DxfHandleAllocator(lines);
            foreach (OverlapEdit edit in edits
                .OrderByDescending(item => item.Record.Start))
            {
                DxfRecord record = edit.Record;
                if (edit.RemainingPieces.Count == 0)
                {
                    lines.RemoveRange(
                        record.Start,
                        record.EndExclusive - record.Start);
                    result.RemovedCount++;
                    continue;
                }

                List<string> template = lines.GetRange(
                    record.Start,
                    record.EndExclusive - record.Start);
                SetLineCoordinates(
                    lines,
                    record.Start,
                    record.EndExclusive,
                    edit.RemainingPieces[0]);

                int insertAt = record.EndExclusive;
                for (int pieceIndex = 1;
                     pieceIndex < edit.RemainingPieces.Count;
                     pieceIndex++)
                {
                    var clone = new List<string>(template);
                    SetLineCoordinates(
                        clone,
                        0,
                        clone.Count,
                        edit.RemainingPieces[pieceIndex]);
                    handles.AssignNewHandle(clone);
                    lines.InsertRange(insertAt, clone);
                    insertAt += clone.Count;
                }
                result.TrimmedCount++;
            }
            handles.WriteHandleSeed(lines);

            return result;
        }

        private static bool IsOverlapGeometryType(string type)
        {
            switch ((type ?? "").Trim().ToUpperInvariant())
            {
                case "LINE":
                case "ARC":
                case "CIRCLE":
                case "ELLIPSE":
                case "LWPOLYLINE":
                case "SPLINE":
                    return true;
                default:
                    return false;
            }
        }

        private static void AddLaserSegments(
            IList<string> lines,
            DxfRecord record,
            ICollection<LaserSegment> target)
        {
            string type = (record.Type ?? "").Trim().ToUpperInvariant();
            if (type == "LINE")
            {
                DxfLineSegment2D segment;
                if (TryGetLineSegment(lines, record, out segment))
                    target.Add(new LaserSegment
                    {
                        Segment = segment,
                        IsStraight = true
                    });
                return;
            }

            if (type == "LWPOLYLINE")
            {
                AddLightweightPolylineLaserSegments(lines, record, target);
                return;
            }

            List<List<DxfPoint2D>> polylines;
            if (!TryTessellateEntity(
                    lines,
                    record,
                    LaserChordTolerance,
                    double.PositiveInfinity,
                    out polylines))
                return;

            foreach (List<DxfPoint2D> polyline in polylines)
            {
                for (int index = 1; index < polyline.Count; index++)
                {
                    target.Add(new LaserSegment
                    {
                        Segment = new DxfLineSegment2D
                        {
                            Start = polyline[index - 1],
                            End = polyline[index]
                        },
                        IsStraight = false
                    });
                }
            }
        }

        private static void AddLightweightPolylineLaserSegments(
            IList<string> lines,
            DxfRecord record,
            ICollection<LaserSegment> target)
        {
            List<LightweightVertex> vertices;
            bool closed;
            double normalZ;
            if (!TryReadLightweightPolyline(
                    lines,
                    record,
                    out vertices,
                    out closed,
                    out normalZ))
                return;

            int count = closed ? vertices.Count : vertices.Count - 1;
            for (int index = 0; index < count; index++)
            {
                LightweightVertex start = vertices[index];
                LightweightVertex end = vertices[(index + 1) % vertices.Count];
                var points = new List<DxfPoint2D>();
                AppendBulgeSegment(
                    points,
                    start,
                    end,
                    LaserChordTolerance,
                    double.PositiveInfinity);
                bool straight = Math.Abs(start.Bulge) < 1e-12;
                for (int pointIndex = 1; pointIndex < points.Count; pointIndex++)
                {
                    target.Add(new LaserSegment
                    {
                        Segment = new DxfLineSegment2D
                        {
                            Start = ToWorld(points[pointIndex - 1], normalZ),
                            End = ToWorld(points[pointIndex], normalZ)
                        },
                        IsStraight = straight
                    });
                }
            }
        }

        /// <summary>
        /// Returns the pieces of <paramref name="line"/> that are not covered
        /// by collinear LASER_MARK geometry. <paramref name="covered"/> is
        /// false when nothing needs to change.
        /// </summary>
        private static List<DxfLineSegment2D> GetUncoveredLinePieces(
            DxfLineSegment2D line,
            LaserSegmentIndex index,
            out bool covered)
        {
            covered = false;
            double dx = line.End.X - line.Start.X;
            double dy = line.End.Y - line.Start.Y;
            double length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length <= OverlapTolerance)
            {
                covered =
                    index.IsNear(line.Start, OverlapTolerance) &&
                    index.IsNear(line.End, OverlapTolerance);
                return new List<DxfLineSegment2D>();
            }

            double ux = dx / length;
            double uy = dy / length;
            double nx = -uy;
            double ny = ux;
            var intervals = new List<KeyValuePair<double, double>>();
            foreach (LaserSegment laser in index.QueryAlong(line))
            {
                DxfLineSegment2D segment = laser.Segment;
                double sx = segment.End.X - segment.Start.X;
                double sy = segment.End.Y - segment.Start.Y;
                double segmentLength = Math.Sqrt((sx * sx) + (sy * sy));
                if (segmentLength <= 1e-9) continue;

                // Only straight laser geometry may trim a straight line; a
                // tessellated arc that merely touches the line tangentially
                // must not cut a gap into CUT/BEND.
                double sine = Math.Abs((ux * sy) - (uy * sx)) / segmentLength;
                if (!laser.IsStraight || sine > ParallelSineTolerance)
                    continue;

                double offsetStart =
                    ((segment.Start.X - line.Start.X) * nx) +
                    ((segment.Start.Y - line.Start.Y) * ny);
                double offsetEnd =
                    ((segment.End.X - line.Start.X) * nx) +
                    ((segment.End.Y - line.Start.Y) * ny);
                if (Math.Abs(offsetStart) > OverlapTolerance ||
                    Math.Abs(offsetEnd) > OverlapTolerance)
                    continue;

                double t0 =
                    ((segment.Start.X - line.Start.X) * ux) +
                    ((segment.Start.Y - line.Start.Y) * uy);
                double t1 =
                    ((segment.End.X - line.Start.X) * ux) +
                    ((segment.End.Y - line.Start.Y) * uy);
                double low = Math.Max(0, Math.Min(t0, t1));
                double high = Math.Min(length, Math.Max(t0, t1));
                if (high - low > 1e-9)
                    intervals.Add(new KeyValuePair<double, double>(low, high));
            }

            if (intervals.Count == 0) return null;

            intervals.Sort((first, second) => first.Key.CompareTo(second.Key));
            var merged = new List<KeyValuePair<double, double>>();
            foreach (KeyValuePair<double, double> interval in intervals)
            {
                if (merged.Count > 0 &&
                    interval.Key <= merged[merged.Count - 1].Value +
                        OverlapTolerance)
                {
                    KeyValuePair<double, double> last = merged[merged.Count - 1];
                    merged[merged.Count - 1] =
                        new KeyValuePair<double, double>(
                            last.Key,
                            Math.Max(last.Value, interval.Value));
                }
                else
                {
                    merged.Add(interval);
                }
            }

            // A short line (a 0.03 mm piece of a rolled-sheet slot edge)
            // covered over its whole length is an overlap, however short.
            if (merged[0].Key <= OverlapTolerance &&
                merged.Any(interval => interval.Value >= length - OverlapTolerance) &&
                merged.Count == 1)
            {
                covered = true;
                return new List<DxfLineSegment2D>();
            }

            // Ignore contact shorter than a trim piece: that is a crossing or
            // an end touching the line, not an overlap.
            merged.RemoveAll(interval =>
                interval.Value - interval.Key < MinimumTrimPieceLength);
            if (merged.Count == 0) return null;

            covered = true;
            var pieces = new List<DxfLineSegment2D>();
            double cursor = 0;
            foreach (KeyValuePair<double, double> interval in merged)
            {
                AddLinePiece(pieces, line, ux, uy, cursor, interval.Key);
                cursor = Math.Max(cursor, interval.Value);
            }
            AddLinePiece(pieces, line, ux, uy, cursor, length);
            return pieces;
        }

        private static void AddLinePiece(
            ICollection<DxfLineSegment2D> pieces,
            DxfLineSegment2D line,
            double ux,
            double uy,
            double from,
            double to)
        {
            if (to - from <= MinimumTrimPieceLength) return;
            pieces.Add(new DxfLineSegment2D
            {
                Start = new DxfPoint2D
                {
                    X = line.Start.X + (ux * from),
                    Y = line.Start.Y + (uy * from)
                },
                End = new DxfPoint2D
                {
                    X = line.Start.X + (ux * to),
                    Y = line.Start.Y + (uy * to)
                }
            });
        }

        private static void SetLineCoordinates(
            IList<string> lines,
            int start,
            int endExclusive,
            DxfLineSegment2D segment)
        {
            SetGroupValue(lines, start, endExclusive, "10", FormatDxfNumber(segment.Start.X));
            SetGroupValue(lines, start, endExclusive, "20", FormatDxfNumber(segment.Start.Y));
            SetGroupValue(lines, start, endExclusive, "11", FormatDxfNumber(segment.End.X));
            SetGroupValue(lines, start, endExclusive, "21", FormatDxfNumber(segment.End.Y));
        }

        private static string FormatDxfNumber(double value)
        {
            return value.ToString(
                "0.0###############",
                CultureInfo.InvariantCulture);
        }

        #region Entity tessellation

        private struct LightweightVertex
        {
            public double X;
            public double Y;
            public double Bulge;
        }

        /// <summary>
        /// Converts one DXF curve entity to polylines whose vertices lie on
        /// the curve. The angular step keeps the chord error below
        /// <paramref name="chordTolerance"/>; <paramref name="maxSpacing"/>
        /// additionally caps the distance between samples.
        /// </summary>
        private static bool TryTessellateEntity(
            IList<string> lines,
            DxfRecord record,
            double chordTolerance,
            double maxSpacing,
            out List<List<DxfPoint2D>> polylines)
        {
            polylines = new List<List<DxfPoint2D>>();
            string type = (record.Type ?? "").Trim().ToUpperInvariant();
            int start = record.Start;
            int end = record.EndExclusive;
            try
            {
                switch (type)
                {
                    case "LINE":
                    {
                        DxfLineSegment2D segment;
                        if (!TryGetLineSegment(lines, record, out segment))
                            return false;
                        var points = new List<DxfPoint2D>();
                        AppendStraight(points, segment.Start, segment.End, maxSpacing, true);
                        polylines.Add(points);
                        return true;
                    }
                    case "CIRCLE":
                    case "ARC":
                    {
                        double centerX, centerY, radius;
                        if (!TryGetDoubleGroupValue(lines, start, end, "10", out centerX) ||
                            !TryGetDoubleGroupValue(lines, start, end, "20", out centerY) ||
                            !TryGetDoubleGroupValue(lines, start, end, "40", out radius) ||
                            radius <= 0)
                            return false;

                        double startAngle = 0;
                        double sweep = 2 * Math.PI;
                        if (type == "ARC")
                        {
                            double startDegrees, endDegrees;
                            if (!TryGetDoubleGroupValue(lines, start, end, "50", out startDegrees) ||
                                !TryGetDoubleGroupValue(lines, start, end, "51", out endDegrees))
                                return false;
                            double sweepDegrees = endDegrees - startDegrees;
                            while (sweepDegrees <= 0) sweepDegrees += 360;
                            while (sweepDegrees > 360) sweepDegrees -= 360;
                            startAngle = startDegrees * Math.PI / 180.0;
                            sweep = sweepDegrees * Math.PI / 180.0;
                        }

                        var points = new List<DxfPoint2D>();
                        AppendArc(points, centerX, centerY, radius, startAngle, sweep,
                            chordTolerance, maxSpacing, true);
                        polylines.Add(ToWorld(points, GetNormalZ(lines, start, end)));
                        return true;
                    }
                    case "ELLIPSE":
                        return TryTessellateEllipse(lines, start, end,
                            chordTolerance, maxSpacing, polylines);
                    case "LWPOLYLINE":
                    {
                        List<LightweightVertex> vertices;
                        bool closed;
                        double normalZ;
                        if (!TryReadLightweightPolyline(lines, record,
                                out vertices, out closed, out normalZ))
                            return false;

                        var points = new List<DxfPoint2D>
                        {
                            new DxfPoint2D { X = vertices[0].X, Y = vertices[0].Y }
                        };
                        int count = closed ? vertices.Count : vertices.Count - 1;
                        for (int index = 0; index < count; index++)
                        {
                            AppendBulgeSegment(points, vertices[index],
                                vertices[(index + 1) % vertices.Count],
                                chordTolerance, maxSpacing);
                        }
                        polylines.Add(ToWorld(points, normalZ));
                        return true;
                    }
                    case "SPLINE":
                        return TryTessellateSpline(lines, start, end,
                            maxSpacing, polylines);
                    default:
                        return false;
                }
            }
            catch (Exception)
            {
                polylines.Clear();
                return false;
            }
        }

        private static bool TryTessellateEllipse(
            IList<string> lines,
            int start,
            int end,
            double chordTolerance,
            double maxSpacing,
            ICollection<List<DxfPoint2D>> polylines)
        {
            double centerX, centerY, majorX, majorY, ratio;
            if (!TryGetDoubleGroupValue(lines, start, end, "10", out centerX) ||
                !TryGetDoubleGroupValue(lines, start, end, "20", out centerY) ||
                !TryGetDoubleGroupValue(lines, start, end, "11", out majorX) ||
                !TryGetDoubleGroupValue(lines, start, end, "21", out majorY) ||
                !TryGetDoubleGroupValue(lines, start, end, "40", out ratio))
                return false;

            double startParameter;
            double endParameter;
            if (!TryGetDoubleGroupValue(lines, start, end, "41", out startParameter))
                startParameter = 0;
            if (!TryGetDoubleGroupValue(lines, start, end, "42", out endParameter))
                endParameter = 2 * Math.PI;
            double sweep = endParameter - startParameter;
            while (sweep <= 0) sweep += 2 * Math.PI;
            while (sweep > 2 * Math.PI + 1e-12) sweep -= 2 * Math.PI;

            double majorRadius = Math.Sqrt((majorX * majorX) + (majorY * majorY));
            if (majorRadius <= 0) return false;
            double minorX = -majorY * ratio;
            double minorY = majorX * ratio;
            int steps = GetArcStepCount(majorRadius, sweep, chordTolerance, maxSpacing);
            var points = new List<DxfPoint2D>(steps + 1);
            for (int index = 0; index <= steps; index++)
            {
                double parameter = startParameter + (sweep * index / steps);
                double cos = Math.Cos(parameter);
                double sin = Math.Sin(parameter);
                points.Add(new DxfPoint2D
                {
                    X = centerX + (majorX * cos) + (minorX * sin),
                    Y = centerY + (majorY * cos) + (minorY * sin)
                });
            }
            polylines.Add(points);
            return true;
        }

        /// <summary>
        /// Evaluates a (rational) B-spline from its control points and knots
        /// with de Boor's algorithm. SPLINE entities written with fit points
        /// only fall back to the fit-point polyline.
        /// </summary>
        private static bool TryTessellateSpline(
            IList<string> lines,
            int start,
            int end,
            double maxSpacing,
            ICollection<List<DxfPoint2D>> polylines)
        {
            int degree = 3;
            var knots = new List<double>();
            var weights = new List<double>();
            var controlX = new List<double>();
            var controlY = new List<double>();
            var fitX = new List<double>();
            var fitY = new List<double>();

            for (int index = start; index + 1 < end; index += 2)
            {
                string code = lines[index].Trim();
                double value;
                if (code == "71")
                {
                    int parsedDegree;
                    if (int.TryParse(lines[index + 1].Trim(), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out parsedDegree))
                        degree = parsedDegree;
                    continue;
                }
                if (!double.TryParse(lines[index + 1].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out value))
                    continue;
                switch (code)
                {
                    case "40": knots.Add(value); break;
                    case "41": weights.Add(value); break;
                    case "10": controlX.Add(value); break;
                    case "20": controlY.Add(value); break;
                    case "11": fitX.Add(value); break;
                    case "21": fitY.Add(value); break;
                }
            }

            int controlCount = Math.Min(controlX.Count, controlY.Count);
            bool canEvaluate =
                degree >= 1 &&
                controlCount >= degree + 1 &&
                knots.Count == controlCount + degree + 1;
            if (!canEvaluate)
            {
                int fitCount = Math.Min(fitX.Count, fitY.Count);
                if (fitCount < 2) return false;
                var fitPoints = new List<DxfPoint2D>();
                for (int index = 0; index < fitCount; index++)
                {
                    var point = new DxfPoint2D { X = fitX[index], Y = fitY[index] };
                    if (fitPoints.Count == 0)
                        fitPoints.Add(point);
                    else
                        AppendStraight(fitPoints, fitPoints[fitPoints.Count - 1],
                            point, maxSpacing, false);
                }
                polylines.Add(fitPoints);
                return true;
            }

            bool rational = weights.Count == controlCount;
            double polygonLength = 0;
            for (int index = 1; index < controlCount; index++)
            {
                double dx = controlX[index] - controlX[index - 1];
                double dy = controlY[index] - controlY[index - 1];
                polygonLength += Math.Sqrt((dx * dx) + (dy * dy));
            }

            int samples = Math.Max(64, controlCount * 32);
            if (!double.IsInfinity(maxSpacing) && maxSpacing > 0)
                samples = Math.Max(samples, (int)Math.Ceiling(polygonLength / maxSpacing));
            samples = Math.Min(samples, MaxTessellationPoints);

            double low = knots[degree];
            double high = knots[controlCount];
            if (high <= low) return false;

            var points = new List<DxfPoint2D>(samples + 1);
            var workX = new double[degree + 1];
            var workY = new double[degree + 1];
            var workW = new double[degree + 1];
            for (int sample = 0; sample <= samples; sample++)
            {
                double parameter = low + ((high - low) * sample / samples);
                int span = degree;
                while (span < controlCount - 1 && parameter >= knots[span + 1])
                    span++;

                for (int j = 0; j <= degree; j++)
                {
                    int controlIndex = span - degree + j;
                    double weight = rational ? weights[controlIndex] : 1.0;
                    workX[j] = controlX[controlIndex] * weight;
                    workY[j] = controlY[controlIndex] * weight;
                    workW[j] = weight;
                }

                for (int r = 1; r <= degree; r++)
                {
                    for (int j = degree; j >= r; j--)
                    {
                        int knotIndex = span - degree + j;
                        double denominator =
                            knots[knotIndex + degree - r + 1] - knots[knotIndex];
                        double alpha = Math.Abs(denominator) < 1e-15
                            ? 0
                            : (parameter - knots[knotIndex]) / denominator;
                        workX[j] = ((1 - alpha) * workX[j - 1]) + (alpha * workX[j]);
                        workY[j] = ((1 - alpha) * workY[j - 1]) + (alpha * workY[j]);
                        workW[j] = ((1 - alpha) * workW[j - 1]) + (alpha * workW[j]);
                    }
                }

                double w = Math.Abs(workW[degree]) < 1e-15 ? 1.0 : workW[degree];
                points.Add(new DxfPoint2D
                {
                    X = workX[degree] / w,
                    Y = workY[degree] / w
                });
            }

            polylines.Add(points);
            return true;
        }

        private static bool TryReadLightweightPolyline(
            IList<string> lines,
            DxfRecord record,
            out List<LightweightVertex> vertices,
            out bool closed,
            out double normalZ)
        {
            vertices = new List<LightweightVertex>();
            closed = false;
            normalZ = GetNormalZ(lines, record.Start, record.EndExclusive);
            for (int index = record.Start;
                 index + 1 < record.EndExclusive;
                 index += 2)
            {
                string code = lines[index].Trim();
                string raw = lines[index + 1].Trim();
                if (code == "70")
                {
                    int flags;
                    if (int.TryParse(raw, NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out flags))
                        closed = (flags & 1) != 0;
                    continue;
                }

                double value;
                if (!double.TryParse(raw, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out value))
                    continue;
                if (code == "10")
                {
                    vertices.Add(new LightweightVertex { X = value });
                }
                else if (vertices.Count > 0)
                {
                    LightweightVertex last = vertices[vertices.Count - 1];
                    if (code == "20") last.Y = value;
                    else if (code == "42") last.Bulge = value;
                    else continue;
                    vertices[vertices.Count - 1] = last;
                }
            }
            return vertices.Count >= 2;
        }

        private static void AppendBulgeSegment(
            List<DxfPoint2D> points,
            LightweightVertex start,
            LightweightVertex end,
            double chordTolerance,
            double maxSpacing)
        {
            var startPoint = new DxfPoint2D { X = start.X, Y = start.Y };
            var endPoint = new DxfPoint2D { X = end.X, Y = end.Y };
            if (points.Count == 0) points.Add(startPoint);

            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double chord = Math.Sqrt((dx * dx) + (dy * dy));
            if (Math.Abs(start.Bulge) < 1e-12 || chord <= 1e-12)
            {
                AppendStraight(points, startPoint, endPoint, maxSpacing, false);
                return;
            }

            double sweep = 4 * Math.Atan(start.Bulge);
            double radius = chord / (2 * Math.Sin(Math.Abs(sweep) / 2));
            double centerOffset = (chord / 2) / Math.Tan(sweep / 2);
            double centerX = ((start.X + end.X) / 2) + (-dy / chord * centerOffset);
            double centerY = ((start.Y + end.Y) / 2) + (dx / chord * centerOffset);
            double startAngle = Math.Atan2(start.Y - centerY, start.X - centerX);
            AppendArc(points, centerX, centerY, radius, startAngle, sweep,
                chordTolerance, maxSpacing, false);
        }

        private static void AppendStraight(
            List<DxfPoint2D> points,
            DxfPoint2D start,
            DxfPoint2D end,
            double maxSpacing,
            bool includeStart)
        {
            if (includeStart) points.Add(start);
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Sqrt((dx * dx) + (dy * dy));
            int steps = double.IsInfinity(maxSpacing) || maxSpacing <= 0
                ? 1
                : Math.Max(1, Math.Min(MaxTessellationPoints,
                    (int)Math.Ceiling(length / maxSpacing)));
            for (int index = 1; index <= steps; index++)
            {
                points.Add(new DxfPoint2D
                {
                    X = start.X + (dx * index / steps),
                    Y = start.Y + (dy * index / steps)
                });
            }
        }

        private static void AppendArc(
            List<DxfPoint2D> points,
            double centerX,
            double centerY,
            double radius,
            double startAngle,
            double sweep,
            double chordTolerance,
            double maxSpacing,
            bool includeStart)
        {
            int steps = GetArcStepCount(radius, Math.Abs(sweep), chordTolerance, maxSpacing);
            for (int index = includeStart ? 0 : 1; index <= steps; index++)
            {
                double angle = startAngle + (sweep * index / steps);
                points.Add(new DxfPoint2D
                {
                    X = centerX + (radius * Math.Cos(angle)),
                    Y = centerY + (radius * Math.Sin(angle))
                });
            }
        }

        private static int GetArcStepCount(
            double radius,
            double sweep,
            double chordTolerance,
            double maxSpacing)
        {
            double step = sweep;
            if (radius > chordTolerance && chordTolerance > 0)
                step = Math.Min(step, 2 * Math.Acos(1 - (chordTolerance / radius)));
            if (!double.IsInfinity(maxSpacing) && maxSpacing > 0)
                step = Math.Min(step, maxSpacing / radius);
            if (step <= 1e-9) return MaxTessellationPoints;
            return Math.Max(8, Math.Min(MaxTessellationPoints,
                (int)Math.Ceiling(sweep / step)));
        }

        private static double GetNormalZ(IList<string> lines, int start, int end)
        {
            double normalZ;
            return TryGetDoubleGroupValue(lines, start, end, "230", out normalZ)
                ? normalZ
                : 1.0;
        }

        // ARC/CIRCLE/LWPOLYLINE coordinates are in the entity OCS. For the 2D
        // DXF written from a Drawing the only other normal in practice is
        // (0,0,-1), whose OCS X axis is mirrored.
        private static List<DxfPoint2D> ToWorld(List<DxfPoint2D> points, double normalZ)
        {
            if (normalZ >= 0) return points;
            for (int index = 0; index < points.Count; index++)
                points[index] = ToWorld(points[index], normalZ);
            return points;
        }

        private static DxfPoint2D ToWorld(DxfPoint2D point, double normalZ)
        {
            return normalZ >= 0
                ? point
                : new DxfPoint2D { X = -point.X, Y = point.Y };
        }

        #endregion

        private sealed class LaserSegmentIndex
        {
            private readonly List<LaserSegment> _segments;
            private readonly Dictionary<long, List<int>> _cells =
                new Dictionary<long, List<int>>();
            private readonly double _cellSize;

            public LaserSegmentIndex(List<LaserSegment> segments, double tolerance)
            {
                _segments = segments;
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                foreach (LaserSegment laser in segments)
                {
                    minX = Math.Min(minX, Math.Min(laser.Segment.Start.X, laser.Segment.End.X));
                    minY = Math.Min(minY, Math.Min(laser.Segment.Start.Y, laser.Segment.End.Y));
                    maxX = Math.Max(maxX, Math.Max(laser.Segment.Start.X, laser.Segment.End.X));
                    maxY = Math.Max(maxY, Math.Max(laser.Segment.Start.Y, laser.Segment.End.Y));
                }
                double diagonal = Math.Sqrt(
                    ((maxX - minX) * (maxX - minX)) +
                    ((maxY - minY) * (maxY - minY)));
                _cellSize = Math.Max(1.0, Math.Max(tolerance * 4, diagonal / 512.0));

                for (int index = 0; index < segments.Count; index++)
                {
                    foreach (long key in WalkCells(segments[index].Segment))
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            for (int dy = -1; dy <= 1; dy++)
                                Add(Offset(key, dx, dy), index);
                        }
                    }
                }
            }

            public bool IsNear(DxfPoint2D point, double tolerance)
            {
                List<int> bucket;
                if (!_cells.TryGetValue(KeyOf(point.X, point.Y), out bucket))
                    return false;
                double toleranceSquared = tolerance * tolerance;
                foreach (int index in bucket)
                {
                    if (DistanceToSegmentSquared(point, _segments[index].Segment) <=
                        toleranceSquared)
                        return true;
                }
                return false;
            }

            /// <summary>
            /// Khoảng cách từ <paramref name="point"/> tới đoạn GẦN NHẤT (chỉ xét đoạn trong ô của điểm, đủ
            /// cho mọi khoảng cách ≤ dung sai lúc dựng chỉ mục); +∞ khi không có đoạn nào trong
            /// <paramref name="tolerance"/>.
            /// </summary>
            public double NearestDistance(DxfPoint2D point, double tolerance)
            {
                List<int> bucket;
                if (!_cells.TryGetValue(KeyOf(point.X, point.Y), out bucket))
                    return double.PositiveInfinity;
                double best = double.PositiveInfinity;
                foreach (int index in bucket)
                {
                    double distance = DistanceToSegmentSquared(point, _segments[index].Segment);
                    if (distance < best) best = distance;
                }
                return best <= tolerance * tolerance ? Math.Sqrt(best) : double.PositiveInfinity;
            }

            public IEnumerable<LaserSegment> QueryAlong(DxfLineSegment2D line)
            {
                var seen = new HashSet<int>();
                foreach (long key in WalkCells(line))
                {
                    List<int> bucket;
                    if (!_cells.TryGetValue(key, out bucket)) continue;
                    foreach (int index in bucket)
                    {
                        if (seen.Add(index)) yield return _segments[index];
                    }
                }
            }

            private IEnumerable<long> WalkCells(DxfLineSegment2D segment)
            {
                double dx = segment.End.X - segment.Start.X;
                double dy = segment.End.Y - segment.Start.Y;
                double length = Math.Sqrt((dx * dx) + (dy * dy));
                int steps = Math.Max(1, (int)Math.Ceiling(length / (_cellSize * 0.5)));
                long previous = long.MinValue;
                for (int index = 0; index <= steps; index++)
                {
                    long key = KeyOf(
                        segment.Start.X + (dx * index / steps),
                        segment.Start.Y + (dy * index / steps));
                    if (key == previous) continue;
                    previous = key;
                    yield return key;
                }
            }

            private void Add(long key, int index)
            {
                List<int> bucket;
                if (!_cells.TryGetValue(key, out bucket))
                {
                    bucket = new List<int>();
                    _cells[key] = bucket;
                }
                if (bucket.Count == 0 || bucket[bucket.Count - 1] != index)
                    bucket.Add(index);
            }

            private long KeyOf(double x, double y)
            {
                return Pack(
                    (int)Math.Floor(x / _cellSize),
                    (int)Math.Floor(y / _cellSize));
            }

            private static long Offset(long key, int dx, int dy)
            {
                return Pack((int)(key >> 32) + dx, (int)key + dy);
            }

            private static long Pack(int x, int y)
            {
                return ((long)x << 32) | (uint)y;
            }
        }

        /// <summary>
        /// A trimmed LINE split into two pieces needs a new, unique entity
        /// handle; $HANDSEED must stay above every handle in the file.
        /// </summary>
        private sealed class DxfHandleAllocator
        {
            private int _seedValueIndex = -1;
            private ulong _next;
            private bool _changed;

            public DxfHandleAllocator(IList<string> lines)
            {
                ulong maximum = 0;
                for (int index = 0; index + 1 < lines.Count; index += 2)
                {
                    string code = lines[index].Trim();
                    if (code == "9" &&
                        string.Equals(lines[index + 1].Trim(), "$HANDSEED",
                            StringComparison.OrdinalIgnoreCase) &&
                        index + 3 < lines.Count &&
                        lines[index + 2].Trim() == "5")
                    {
                        _seedValueIndex = index + 3;
                        continue;
                    }
                    if (code != "5" && code != "105") continue;

                    ulong handle;
                    if (ulong.TryParse(lines[index + 1].Trim(),
                            NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture,
                            out handle))
                        maximum = Math.Max(maximum, handle);
                }

                ulong seed = 0;
                if (_seedValueIndex >= 0)
                    ulong.TryParse(lines[_seedValueIndex].Trim(),
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out seed);
                _next = Math.Max(seed, maximum + 1);
            }

            public void AssignNewHandle(IList<string> record)
            {
                for (int index = 0; index + 1 < record.Count; index += 2)
                {
                    if (record[index].Trim() != "5") continue;
                    record[index + 1] = _next.ToString("X", CultureInfo.InvariantCulture);
                    _next++;
                    _changed = true;
                    return;
                }
            }

            public void WriteHandleSeed(IList<string> lines)
            {
                if (!_changed || _seedValueIndex < 0 ||
                    _seedValueIndex >= lines.Count)
                    return;
                // Header precedes ENTITIES, so its index is unaffected by the
                // entity insertions/removals made after construction.
                lines[_seedValueIndex] = _next.ToString("X", CultureInfo.InvariantCulture);
            }
        }
    }
}
