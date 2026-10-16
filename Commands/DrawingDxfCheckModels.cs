using System;
using System.Collections.Generic;
using System.Linq;

namespace ADDIN.Commands.DxfChecking
{
    // Alignment deliberately has no scale, rotation or reflection member.
    internal struct Translation2D
    {
        public double OffsetX, OffsetY;
        public DxfPoint Apply(DxfPoint p) { return p + new DxfPoint(OffsetX, OffsetY); }
    }

    internal sealed class DrawingDxfCheckOptions
    {
        public double ToleranceMm = .20;
        public string IgnoredDxfLayers = "BEND;BENDLINES;BEND_UP;BEND_DOWN";
        public void Validate()
        {
            if (double.IsNaN(ToleranceMm) || double.IsInfinity(ToleranceMm) || ToleranceMm < .001 || ToleranceMm > 10)
                throw new ArgumentException("Dung sai phải từ 0.001 đến 10 mm.");
        }
    }

    internal sealed class GeometryLoop2D
    {
        public int Id;
        public List<DxfEdge> Entities = new List<DxfEdge>();
        public List<DxfPoint> Points = new List<DxfPoint>();
        public DxfBox Bounds;
        public DxfPoint Centroid;
        public double Area, Perimeter;
        public bool IsCircle { get { return Entities.Count == 1 && Entities[0].Type == "CIRCLE"; } }
    }

    internal sealed class DrawingGeometry2D
    {
        public List<DxfEdge> Edges = new List<DxfEdge>();
        public GeometryLoop2D Outer;
        public List<GeometryLoop2D> Inside = new List<GeometryLoop2D>();
        public List<DxfEdge> Unclosed = new List<DxfEdge>();
        public int OtherOuterCount;
        public List<GeometryReviewPoint> Reviews = new List<GeometryReviewPoint>();
    }

    internal sealed class GeometryReviewPoint
    {
        public string Type, Description;
        public DxfPoint Position;
        public double Deviation;
    }

    internal sealed class GeometryMismatch
    {
        public int Id;
        public string Type, Description;
        public bool CutQualityIssue;
        public bool ReviewIssue;
        public DxfPoint DrawingPosition, DxfPosition;
        public bool HasDrawingPosition, HasDxfPosition;
        public double Deviation;
    }

    internal sealed class DxfCheckResult
    {
        public bool ScalePassed, ScaleEvaluated, OuterContourPassed, AlignmentFound;
        public double EstimatedScale = 1, MaximumDeviation;
        public Translation2D Translation;
        public int DrawingInsideContourCount, DxfInsideContourCount, MatchedInsideContours, FailedInsideContours;
        public List<string> ScaleMeasurements = new List<string>();
        public List<GeometryMismatch> Mismatches = new List<GeometryMismatch>();
        public DrawingComparisonSource Source;
        public DrawingNormalizationReport Normalization;
        public bool GeometryPassed { get { return ScaleEvaluated && ScalePassed && AlignmentFound && OuterContourPassed && !Mismatches.Any(m => !m.CutQualityIssue && !m.ReviewIssue) && FailedInsideContours == 0; } }
        public bool CutQualityPassed { get { return !Mismatches.Any(m => m.CutQualityIssue || m.ReviewIssue); } }
        public bool ReviewRequired { get { return Mismatches.Any(m => m.ReviewIssue); } }
        public bool Passed { get { return GeometryPassed && CutQualityPassed && !ReviewRequired; } }
        public string Status { get { return !GeometryPassed ? "FAIL" : ReviewRequired ? "CHECK" : CutQualityPassed ? "PASS" : "WARN"; } }
        public void AddReview(GeometryReviewPoint review, bool drawing)
        {
            // Numerical joins are review evidence, separate from measured shape deviation.
            Mismatches.Add(new GeometryMismatch { Id = Mismatches.Count + 1, Type = review.Type,
                Description = (drawing ? "Drawing: " : "DXF: ") + review.Description, ReviewIssue = true,
                HasDrawingPosition = drawing, DrawingPosition = drawing ? review.Position : new DxfPoint(),
                HasDxfPosition = !drawing, DxfPosition = drawing ? new DxfPoint() : review.Position, Deviation = review.Deviation });
        }
        public void AddQuality(string type, string description, DxfPoint? dxf)
        {
            Add(type, description, null, dxf);
            Mismatches[Mismatches.Count - 1].CutQualityIssue = true;
        }
        public void Add(string type, string description, DxfPoint? drawing, DxfPoint? dxf, double deviation = 0)
        {
            Mismatches.Add(new GeometryMismatch { Id = Mismatches.Count + 1, Type = type, Description = description,
                HasDrawingPosition = drawing.HasValue, DrawingPosition = drawing.GetValueOrDefault(),
                HasDxfPosition = dxf.HasValue, DxfPosition = dxf.GetValueOrDefault(), Deviation = deviation });
            MaximumDeviation = Math.Max(MaximumDeviation, deviation);
        }
    }
}
