using System;
using System.Collections.Generic;
using System.Linq;

namespace ADDIN.Commands.DxfChecking
{
    [Flags]
    internal enum DxfCheckCondition
    {
        None = 0, Overlap = 1, DeformedHole = 2, Ellipse = 4, ShortEdge = 8,
        IntersectingHoles = 16, Closure = 32, RoundHoleToEdge = 64, SplineRoundHole = 128,
        // Bit 512 is reserved for old callers; it is excluded from all supported checks.
        YellowLayers = 256, SimilarParts = 512, DuplicateNames = 1024,
        SplineEntity = 2048, PolylineEntity = 4096,
        Geometry = Overlap | DeformedHole | Ellipse | ShortEdge | IntersectingHoles | Closure | RoundHoleToEdge | SplineRoundHole | SplineEntity | PolylineEntity,
        All = Geometry | YellowLayers | DuplicateNames
    }

    internal sealed class DxfCheckRule
    {
        public readonly DxfCheckCondition Condition;
        public readonly string Name, Hint;
        public readonly bool Geometry;
        public DxfCheckRule(DxfCheckCondition condition, string name, string hint, bool geometry)
        { Condition = condition; Name = name; Hint = hint; Geometry = geometry; }
    }

    internal sealed class DxfCheckOptions
    {
        public static readonly DxfCheckOptions All = new DxfCheckOptions(DxfCheckCondition.All);
        public static readonly DxfCheckOptions Default = new DxfCheckOptions(DxfCheckCondition.All & ~(DxfCheckCondition.SplineEntity | DxfCheckCondition.PolylineEntity));
        public static readonly IReadOnlyList<DxfCheckRule> Rules = Array.AsReadOnly(new[] {
            new DxfCheckRule(DxfCheckCondition.Overlap, "Đường trùng / chồng biên dạng", "Báo khi chồng toàn bộ hoặc một phần, kể cả nét đảo chiều và khác layer.", true),
            new DxfCheckRule(DxfCheckCondition.DeformedHole, "Lỗ tròn bên trong bị méo", "Lỗ không tròn: NG. Giữ logic cho phép slot và rãnh cắt hợp lệ.", true),
            new DxfCheckRule(DxfCheckCondition.Ellipse, "Đối tượng ELLIPSE", "Báo NG tất cả đối tượng ELLIPSE.", true),
            new DxfCheckRule(DxfCheckCondition.ShortEdge, "Đoạn biên dạng ngắn < 0,5 mm", "Kiểm tra chiều dài từng đoạn biên dạng, kể cả đoạn trong Polyline.", true),
            new DxfCheckRule(DxfCheckCondition.IntersectingHoles, "Lỗ / rãnh cắt khuyết đan vào nhau", "Báo khi lỗ/rãnh bên trong chạm, cắt hoặc tự cắt nhau.", true),
            new DxfCheckRule(DxfCheckCondition.Closure, "Biên kín và đầu nét nối đúng", "Kiểm tra biên ngoài trước, rồi lỗ/rãnh bên trong; ngưỡng nối 0,001 mm. Nét vượt/chạm nhưng chưa nối được báo riêng.", true),
            new DxfCheckRule(DxfCheckCondition.RoundHoleToEdge, "Lỗ tròn bị khuyết thông ra mép ngoài", "Phát hiện lỗ tròn bị cắt khuyết ra mép kể cả khi đường bao ngoài vẫn kín.", true),
            new DxfCheckRule(DxfCheckCondition.SplineRoundHole, "Lỗ bên trong dạng tròn bằng SPLINE", "Lỗ tròn có SPLINE: NG, kể cả hình học tròn chính xác.", true),
            new DxfCheckRule(DxfCheckCondition.SplineEntity, "Báo NG mọi SPLINE", "Báo NG theo loại đối tượng SPLINE trên mọi layer, kể cả biên ngoài, lỗ và slot hợp lệ. Mặc định bỏ tick.", true),
            new DxfCheckRule(DxfCheckCondition.PolylineEntity, "Báo NG mọi POLYLINE / LWPOLYLINE", "Báo NG theo loại đối tượng POLYLINE hoặc LWPOLYLINE, kể cả biên kín hợp lệ. Mỗi đối tượng được ghi một lỗi, không đếm từng đoạn. Mặc định bỏ tick.", true),
            new DxfCheckRule(DxfCheckCondition.YellowLayers, "Thống kê layer / đối tượng màu vàng", "Báo tên layer, hình dạng và số lượng; cho phép bấm để định vị.", false),
            new DxfCheckRule(DxfCheckCondition.DuplicateNames, "Cảnh báo tên file trùng ở thư mục khác", "Liệt kê những DXF cùng tên nhưng nằm ở các thư mục khác nhau.", false)
        });
        public readonly DxfCheckCondition Conditions;
        public DxfCheckOptions(DxfCheckCondition conditions) { Conditions = conditions & DxfCheckCondition.All; }
        public bool Has(DxfCheckCondition condition) { return (Conditions & condition) == condition; }
        public bool HasAny { get { return Conditions != DxfCheckCondition.None; } }
        public int Count { get { return Rules.Count(rule => Has(rule.Condition)); } }
        public string SelectedNames { get { return string.Join("; ", Rules.Where(rule => Has(rule.Condition)).Select(rule => rule.Name)); } }
        public bool IssueEnabled(string code)
        {
            switch (code) {
                case "OVERLAP": return Has(DxfCheckCondition.Overlap);
                case "ROUND_DEFORMED": return Has(DxfCheckCondition.DeformedHole);
                case "ELLIPSE": return Has(DxfCheckCondition.Ellipse);
                case "SHORT_EDGE": return Has(DxfCheckCondition.ShortEdge);
                case "HOLE_INTERSECTION": return Has(DxfCheckCondition.IntersectingHoles);
                case "ROUND_HOLE_TO_EDGE": return Has(DxfCheckCondition.RoundHoleToEdge);
                case "SPLINE_ROUND_HOLE": return Has(DxfCheckCondition.SplineRoundHole);
                case "SPLINE": return Has(DxfCheckCondition.SplineEntity);
                case "POLYLINE": return Has(DxfCheckCondition.PolylineEntity);
                case "CONTOUR_JOIN_UNTRIMMED": return Has(DxfCheckCondition.Closure);
                default: return code == null || !code.StartsWith("OPEN_CONTOUR_", StringComparison.Ordinal) || Has(DxfCheckCondition.Closure);
            }
        }
    }

    internal struct DxfPoint
    {
        public double X, Y;
        public DxfPoint(double x, double y) { X = x; Y = y; }
        public static DxfPoint operator +(DxfPoint a, DxfPoint b) { return new DxfPoint(a.X + b.X, a.Y + b.Y); }
        public static DxfPoint operator -(DxfPoint a, DxfPoint b) { return new DxfPoint(a.X - b.X, a.Y - b.Y); }
        public static DxfPoint operator *(DxfPoint a, double b) { return new DxfPoint(a.X * b, a.Y * b); }
        public double Length { get { return Math.Sqrt(X * X + Y * Y); } }
        public double Dot(DxfPoint b) { return X * b.X + Y * b.Y; }
        public double Cross(DxfPoint b) { return X * b.Y - Y * b.X; }
        public double Distance(DxfPoint b) { return (this - b).Length; }
    }

    internal struct DxfBox
    {
        public double MinX, MinY, MaxX, MaxY;
        public double Width { get { return MaxX - MinX; } }
        public double Height { get { return MaxY - MinY; } }
        public double Size { get { return Math.Max(Width, Height); } }
        public DxfPoint Center { get { return new DxfPoint((MinX + MaxX) / 2, (MinY + MaxY) / 2); } }
        public bool Intersects(DxfBox b, double eps)
        { return MinX <= b.MaxX + eps && MaxX + eps >= b.MinX && MinY <= b.MaxY + eps && MaxY + eps >= b.MinY; }
        public static DxfBox From(IEnumerable<DxfPoint> points)
        {
            DxfBox box = new DxfBox { MinX = double.PositiveInfinity, MinY = double.PositiveInfinity, MaxX = double.NegativeInfinity, MaxY = double.NegativeInfinity };
            foreach (DxfPoint p in points) { box.MinX = Math.Min(box.MinX, p.X); box.MinY = Math.Min(box.MinY, p.Y); box.MaxX = Math.Max(box.MaxX, p.X); box.MaxY = Math.Max(box.MaxY, p.Y); }
            return box;
        }
    }

    internal struct DxfTransform
    {
        public double A, B, C, D, X, Y;
        public static DxfTransform Identity { get { return new DxfTransform { A = 1, D = 1 }; } }
        public DxfPoint Apply(DxfPoint p) { return new DxfPoint(A * p.X + C * p.Y + X, B * p.X + D * p.Y + Y); }
        public DxfPoint Vector(DxfPoint p) { return new DxfPoint(A * p.X + C * p.Y, B * p.X + D * p.Y); }
        public double Determinant { get { return A * D - B * C; } }
        public bool IsSimilarity
        {
            get { DxfPoint u = Vector(new DxfPoint(1, 0)), v = Vector(new DxfPoint(0, 1)); return Math.Abs(u.Length - v.Length) <= 1e-12 * Math.Max(1, u.Length) && Math.Abs(u.Dot(v)) <= 1e-12 * Math.Max(1, u.Length * v.Length); }
        }
        public DxfTransform Then(DxfTransform outer)
        {
            DxfPoint u = outer.Vector(new DxfPoint(A, B)), v = outer.Vector(new DxfPoint(C, D)), p = outer.Apply(new DxfPoint(X, Y));
            return new DxfTransform { A = u.X, B = u.Y, C = v.X, D = v.Y, X = p.X, Y = p.Y };
        }
    }

    internal sealed class DxfIssue
    {
        public int Id;
        public string Code, Description, Handle, Layer, EntityType, Space;
        public double? Measurement;
        public int ContourId;
        public DxfPoint Position;
        public DxfBox Bounds;
    }

    internal sealed class DxfFileResult
    {
        public string CutLayer;
        public DxfCheckOptions Options = DxfCheckOptions.Default;
        public string SourcePath, RelativeName, OutputPath, Error;
        public string CheckedPath, CleanPath;
        public DateTime? RecheckedAt;
        public DateTime? SourceUpdatedAt;
        public string SourceBackupPath;
        public List<DxfIssue> Issues = new List<DxfIssue>();
        public List<DxfEdge> Edges = new List<DxfEdge>();
        public List<DxfLayerInfo> YellowLayers = new List<DxfLayerInfo>();
        public List<DxfNonCutItem> YellowItems = new List<DxfNonCutItem>();
        public List<DxfShapeStatistic> YellowShapes = new List<DxfShapeStatistic>();
        public List<DxfContourResult> Contours = new List<DxfContourResult>();
        public List<DxfPartComparison> SimilarParts = new List<DxfPartComparison>();
        public string SimilarityNote;
        public int OpenOuterCount { get { return Contours.Count(c => !c.Closed && c.Role == "Outer"); } }
        public int OpenInnerCount { get { return Contours.Count(c => !c.Closed && c.Role == "Inner"); } }
        public int OpenOtherCount { get { return Contours.Count(c => !c.Closed && c.Role == "Unknown"); } }
        public int YellowContourCount { get { return YellowShapes.Where(s => s.IsContour).Sum(s => s.Count); } }
        public int YellowLayerWithEntitiesCount { get { return YellowShapes.Where(s => s.YellowLayer && s.EntityCount > 0).Select(s => s.Layer).Distinct(StringComparer.OrdinalIgnoreCase).Count(); } }
        public bool HasYellowContent { get { return Error == null && Options.Has(DxfCheckCondition.YellowLayers) && YellowShapes.Any(s => s.EntityCount > 0); } }
        public int GeometryCount;
        public int DuplicateNameCount;
        public double Milliseconds;
        public string Status { get { return Error != null ? "ERROR" : Issues.Count > 0 ? "NG" : "OK"; } }
        public string ReviewStatus { get { return Status; } }
        internal DxfFileResult WithoutComparisons()
        {
            DxfFileResult copy = (DxfFileResult)MemberwiseClone();
            copy.SimilarParts = new List<DxfPartComparison>(); copy.SimilarityNote = null;
            return copy;
        }
    }

    internal sealed class DxfBatchResult
    {
        public DxfCheckOptions Options = DxfCheckOptions.Default;
        public string SourceFolder, OutputFolder, ExcelPath, InputMessage;
        public List<DxfFileResult> Files = new List<DxfFileResult>();
        public List<DxfDuplicateName> DuplicateNames = new List<DxfDuplicateName>();
        public List<DxfPartComparison> PartComparisons = new List<DxfPartComparison>();
        public bool SimilarityComplete = false;
        public string SimilarityMessage = null;
        public int SimilarFileCount { get { return Files.Count(f => f.SimilarParts.Count > 0); } }
        public int DuplicateFileCount { get { return DuplicateNames.Sum(d => d.RelativeNames.Count); } }
        public string DuplicateNameWarning { get { return DuplicateNames.Count == 0 ? null : "Cảnh báo: " + DuplicateNames.Count + " tên DXF trùng ở " + DuplicateFileCount + " file"; } }
        public bool Cancelled;
        public double Seconds;
        public int NgCount { get { return Files.Count(f => f.Status == "NG"); } }
        public int ErrorCount { get { return Files.Count(f => f.Status == "ERROR"); } }
    }

    internal sealed class DxfProgress
    {
        public int Done, Total;
        public DxfFileResult File;
        public List<DxfDuplicateName> DuplicateNames;
        public string Message = null;
    }

    internal sealed class DxfDuplicateName
    {
        public string FileName;
        public List<string> RelativeNames = new List<string>();
    }

    internal sealed class DxfRecheckResult
    {
        public DxfFileResult File;
        public DxfBatchResult Batch;
        public bool Cancelled;
        public string ReportWarning;
        public bool SourceWritten;
        public string SourceWriteError;
    }

    internal sealed class DxfEdge
    {
        public string Type, Handle, Layer, Space;
        public string OriginalType;
        public int Segment;
        public bool Closed;
        public bool EntityClosed, LayerIsYellow, IsYellow;
        public DxfPoint Start, End, Center;
        public double Radius, StartAngle, Sweep, Length;
        public Func<double, DxfPoint> Evaluate;
        public Func<double, DxfPoint> Derivative;
        public List<double> Breaks = new List<double> { 0, 1 };
        public DxfBox Bounds;
        private List<DxfPoint> preview;
        public List<DxfPoint> Preview
        { get { if (preview == null) preview = DxfGeometry.Sample(this, 0.002); return preview; } }
        public DxfPoint At(double t) { return Evaluate(t); }
    }

    internal struct DxfColor
    {
        public int Aci;
        public int? Rgb;
        public bool IsYellow { get { return Rgb.HasValue ? Rgb.Value == 0xFFFF00 : Math.Abs(Aci) == 2; } }
    }

    internal sealed class DxfContourGap
    {
        public DxfPoint A, B;
        public DxfEdge First, Last;
        public double? NearestLength;
        public bool Untrimmed, Touching;
        public double Length { get { return A.Distance(B); } }
        public double Opening { get { return NearestLength ?? Length; } }
    }

    internal sealed class DxfContourResult
    {
        public int Id, Depth;
        public string Space, Role, Note;
        public bool Closed, EnclosesArea;
        public DxfContourResult Parent;
        public List<DxfEdge> Edges = new List<DxfEdge>();
        public List<DxfPoint> Points = new List<DxfPoint>();
        public List<DxfContourGap> Gaps = new List<DxfContourGap>();
        public DxfBox Bounds;
        public DxfEdge First, Last;
        public string RoleName { get { return Role == "Outer" ? "Biên ngoài" : Role == "Inner" ? "Lỗ / rãnh bên trong" : "Biên chưa phân loại"; } }
        public int OpenGapCount { get { return Gaps.Count(g => !g.Untrimmed); } }
        public string ClosureName { get { return Closed ? "Kín" : Gaps.Count > 0 && OpenGapCount == 0 ? "Chưa nối đầu" : "Hở"; } }
        public double? GapLength { get { return Closed || OpenGapCount == 0 ? (double?)null : Gaps.Where(g => !g.Untrimmed).Max(g => g.Opening); } }
        public string Layers { get { return string.Join("; ", Edges.Select(e => e.Layer).Distinct(StringComparer.OrdinalIgnoreCase)); } }
        public string Handles { get { return string.Join("; ", Edges.Select(e => e.Handle + (e.Segment > 0 ? ":" + e.Segment : "")).Distinct()); } }
    }

    internal sealed class DxfLayerInfo
    {
        public string Name;
        public DxfColor Color;
        public bool Off, Frozen;
        public string Note { get { return Off ? "Layer đang tắt" : Frozen ? "Layer đang đóng băng" : ""; } }
    }

    internal sealed class DxfNonCutItem
    {
        public string Layer, Space, Type;
        public bool LayerIsYellow, IsYellow;
    }

    internal sealed class DxfShapeStatistic
    {
        public string Layer, Space, Shape, Note;
        public bool YellowLayer;
        public int Count, EntityCount;
        public List<DxfEdge> Edges = new List<DxfEdge>();
        public string ColorSource { get { return YellowLayer ? "Layer vàng" : "Màu vàng riêng"; } }
        public bool IsContour { get { return Shape == "Circle" || Shape == "Square" || Shape == "Rectangle" || Shape == "Slot" || Shape == "Ellipse" || Shape == "Triangle" || Shape == "Polygon" || Shape == "ClosedOther"; } }
        public string ShapeName
        {
            get {
                switch (Shape) {
                    case "Circle": return "Tròn"; case "Square": return "Vuông"; case "Rectangle": return "Chữ nhật";
                    case "Slot": return "Lỗ dài / slot"; case "Ellipse": return "Ellipse"; case "Triangle": return "Tam giác";
                    case "Polygon": return "Đa giác"; case "ClosedOther": return "Biên kín khác";
                    case "Open": return "Cụm nét hở"; case "Ambiguous": return "Cụm nét có nhánh"; case "Degenerate": return "Nét suy biến";
                    case "Text": return "Văn bản / ghi chú"; case "Point": return "Điểm"; case "Empty": return "Layer trống";
                    default: return "Đối tượng khác";
                }
            }
        }
    }
}
