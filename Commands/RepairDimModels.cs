using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public enum RepairDimFailureMode
    {
        Unknown = 0,
        UnsupportedAnchor,
        FullyLostReference,
        ComponentReinsertedOrGeometryReplaced,
        ModelFileMissingOrUnresolved,
        GeometryChangedNoCandidate,
        RepairCandidateFound,
        SketchPointAnchorLostReference
    }

    public sealed class DocumentDependencyInfo
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string NormalizedPath { get; set; }
        public bool IsValidFilePath { get; set; }
        public bool FileExists { get; set; }
        public bool IsResolved { get; set; }
    }

    public sealed class VisibleEdgeOwnerEntry
    {
        public object Edge { get; set; }
        public Component2 Component { get; set; }
        public string CanonicalComponentName { get; set; }
        public string CanonicalComponentKey { get; set; }
    }

    public sealed class DisplayDimLine
    {
        public int LineIndex { get; set; }
        public int LineType { get; set; }
        public double StartX { get; set; }
        public double StartY { get; set; }
        public double StartZ { get; set; }
        public double EndX { get; set; }
        public double EndY { get; set; }
        public double EndZ { get; set; }
    }

    public sealed class DisplayWitnessProfile
    {
        public bool IsValid { get; set; }
        public string Status { get; set; } = "UNKNOWN"; // VALID, AMBIGUOUS, FAILED
        public string Confidence { get; set; } = "NONE"; // HIGH, MEDIUM, LOW, NONE
        public string ErrorReason { get; set; } = "";

        public int HypothesisCount { get; set; }
        public double BestScore { get; set; }
        public double SecondScore { get; set; }
        public double ScoreGap { get; set; }

        // Dimension Line
        public DisplayDimLine DimensionLine { get; set; }
        public double[] DimensionLineStart { get; set; }
        public double[] DimensionLineEnd { get; set; }
        public double[] DimensionAxisUnitVector { get; set; }
        public string DimensionLineOrientation { get; set; } // "HORIZONTAL", "VERTICAL", "SLANTED"

        // Witness Line 1
        public DisplayDimLine WitnessLine1 { get; set; }
        public double[] Witness1Start { get; set; }
        public double[] Witness1End { get; set; }
        public double[] Witness1GeometryPoint { get; set; }
        public double[] Witness1DimensionPoint { get; set; }
        public string Witness1Orientation { get; set; }

        // Witness Line 2
        public DisplayDimLine WitnessLine2 { get; set; }
        public double[] Witness2Start { get; set; }
        public double[] Witness2End { get; set; }
        public double[] Witness2GeometryPoint { get; set; }
        public double[] Witness2DimensionPoint { get; set; }
        public string Witness2Orientation { get; set; }

        // Measurement Axis & Witness Orientation
        public double[] WitnessDirectionUnitVector { get; set; }
        public string MeasurementAxis { get; set; } // "HORIZONTAL", "VERTICAL", "SLANTED"
        public string WitnessOrientation { get; set; } // "HORIZONTAL", "VERTICAL", "SLANTED"
    }

    public enum DistanceVerificationMode
    {
        NORMAL_SHEET_SCALE,
        BROKEN_VIEW_LOCAL,
        BROKEN_VIEW_CROSS_BREAK
    }

    public sealed class BreakLineInfo
    {
        public int Index { get; set; }
        public int OrientationRaw { get; set; } // 0 = Horizontal, 1 = Vertical
        public string OrientationString { get; set; } // "HORIZONTAL" or "VERTICAL"
        public int Style { get; set; }
        public double Position1 { get; set; }
        public double Position2 { get; set; }
        public double SheetMinCoord { get; set; }
        public double SheetMaxCoord { get; set; }
    }

    public sealed class BrokenViewInfo
    {
        public bool IsBroken { get; set; }
        public int BreakCount { get; set; }
        public List<BreakLineInfo> BreakLines { get; set; } = new List<BreakLineInfo>();
    }

    public sealed class ClosestPointResult2D
    {
        public double[] Point { get; set; } // [x, y]
        public double DistanceM { get; set; }
        public double DistanceMm { get; set; }
        public double ParamT { get; set; } // clamped to [0, 1]
    }

    public sealed class FullyLostSideCandidate
    {
        public int SideIndex { get; set; } // 1 or 2
        public DrawingPolylineEdgeInfo EdgeInfo { get; set; }
        public int RawRecordIndex { get; set; }
        public int EntityArrayIndex { get; set; }
        public string ComponentName { get; set; }
        public string ComponentOccurrenceKey { get; set; }
        public string Orientation { get; set; }
        public double[] SheetStart { get; set; }
        public double[] SheetEnd { get; set; }
        public double[] AttachPoint { get; set; }
        public double AttachParamT { get; set; }
        public double WitnessProximityMm { get; set; }
        public double[] WitnessRayDirection { get; set; }
        public double WitnessRayAngularErrorDeg { get; set; }
        public bool WitnessRayConsistency { get; set; } = true;
        public double Score { get; set; }
    }

    public sealed class EvaluatedPairDiagnostics
    {
        public int Side1RawIndex { get; set; }
        public int Side2RawIndex { get; set; }
        public string Side1ComponentName { get; set; }
        public string Side2ComponentName { get; set; }
        public double[] AttachPoint1 { get; set; }
        public double[] AttachPoint2 { get; set; }
        public double SheetSeparationMm { get; set; }
        public double ModelDistanceMm { get; set; }
        public double TargetDistanceMm { get; set; }
        public double DistanceErrorMm { get; set; }
        public double DistanceToleranceMm { get; set; }
        public double PerpendicularResidualMm { get; set; }
        public double WitnessError1Mm { get; set; }
        public double WitnessError2Mm { get; set; }
        public double TotalWitnessErrorMm { get; set; }
        public double MaxWitnessErrorMm { get; set; }
        public double RayAngularError1Deg { get; set; }
        public double RayAngularError2Deg { get; set; }
        public DistanceVerificationMode DistanceMode { get; set; } = DistanceVerificationMode.NORMAL_SHEET_SCALE;
        public bool CrossesActiveBreak { get; set; }
        public int BreakCrossingCount { get; set; }
        public bool PreCreateDistanceComparable { get; set; } = true;
        public string PreCreateDistanceReason { get; set; } = "";
        public bool DistanceMatched { get; set; }
        public bool AxisMatched { get; set; }
        public bool IsAccepted { get; set; }
        public List<string> RejectionReasons { get; set; } = new List<string>();
        public double PairScore { get; set; }
    }

    public sealed class FullyLostPairCandidate
    {
        public int Rank { get; set; }
        public string PhysicalPairKey { get; set; }
        public FullyLostSideCandidate Side1 { get; set; }
        public FullyLostSideCandidate Side2 { get; set; }
        public double[] AttachPoint1 { get; set; }
        public double[] AttachPoint2 { get; set; }
        public double MeasuredSheetDistanceMm { get; set; }
        public double MeasuredModelDistanceMm { get; set; }
        public double TargetDimensionMm { get; set; }
        public double DistanceErrorMm { get; set; }
        public double PerpendicularResidualMm { get; set; }
        public double WitnessError1Mm { get; set; }
        public double WitnessError2Mm { get; set; }
        public double TotalWitnessErrorMm { get; set; }
        public double MaxWitnessErrorMm { get; set; }
        public double RayAngularError1Deg { get; set; }
        public double RayAngularError2Deg { get; set; }
        public DistanceVerificationMode DistanceMode { get; set; } = DistanceVerificationMode.NORMAL_SHEET_SCALE;
        public bool CrossesActiveBreak { get; set; }
        public int BreakCrossingCount { get; set; }
        public bool PreCreateDistanceComparable { get; set; } = true;
        public string PreCreateDistanceReason { get; set; } = "";
        public bool DistanceMatched { get; set; }
        public bool MeasurementAxisMatched { get; set; }
        public string SideAssignment { get; set; } = "DIRECT"; // "DIRECT" or "SWAPPED"
        public double TotalWitnessProximityMm { get; set; }
        public double PairScore { get; set; }
        public string Reason { get; set; }
    }

    public sealed class FullyLostPairDecision
    {
        public string Decision { get; set; } = "NO_CANDIDATE"; // FULLY_LOST_HIGH_CONFIDENCE, FULLY_LOST_AMBIGUOUS, BROKEN_VIEW_PROVISIONAL_HIGH_CONFIDENCE, NO_CANDIDATE, etc.
        public string PairUniqueness { get; set; } = "NO_PAIR"; // "UNIQUE_PAIR", "PHYSICALLY_DEDUPLICATED_UNIQUE", "COMPETITIVE_PAIR", "NO_PAIR"
        public int RawPairCount { get; set; }
        public int PhysicalUniquePairCount { get; set; }
        public BrokenViewInfo BrokenViewInfo { get; set; }
        public DistanceVerificationMode DistanceMode { get; set; } = DistanceVerificationMode.NORMAL_SHEET_SCALE;
        public bool CrossesActiveBreak { get; set; }
        public int BreakCrossingCount { get; set; }
        public string PreCreateDecision { get; set; } = "";
        public DisplayWitnessProfile WitnessProfile { get; set; }
        public List<FullyLostSideCandidate> Side1Candidates { get; set; } = new List<FullyLostSideCandidate>();
        public List<FullyLostSideCandidate> Side2Candidates { get; set; } = new List<FullyLostSideCandidate>();
        public List<EvaluatedPairDiagnostics> EvaluatedCombinations { get; set; } = new List<EvaluatedPairDiagnostics>();
        public List<FullyLostPairCandidate> PairCandidates { get; set; } = new List<FullyLostPairCandidate>();
        public List<string> DuplicatePairLogs { get; set; } = new List<string>();
        public FullyLostPairCandidate BestPair { get; set; }
        public FullyLostPairCandidate SecondPair { get; set; }
        public double ScoreGap { get; set; }
        public double WitnessErrorGap { get; set; }
        public string AmbiguityReason { get; set; } = "";
        public string RecommendedAction { get; set; } = "MANUAL_REVIEW";
    }

    public sealed class PointCoordinateHypothesis
    {
        public string Method { get; set; }
        public double[] SheetXY { get; set; }
        public double Witness1ErrorMm { get; set; }
        public double Witness2ErrorMm { get; set; }
        public bool IsMatched { get; set; }
        public int MatchedWitnessSide { get; set; } // 1 or 2, 0 if none
        public double ErrorMm { get; set; }
    }

    public sealed class PointAnchorInfo
    {
        public SketchPoint LivePoint { get; set; }
        public double RawX { get; set; }
        public double RawY { get; set; }
        public double RawZ { get; set; }
        public int PointID { get; set; }
        public Sketch OwnerSketch { get; set; }
        public string SketchFeatureName { get; set; }
        public bool BelongsToCurrentView { get; set; }
        public List<PointCoordinateHypothesis> Hypotheses { get; set; } = new List<PointCoordinateHypothesis>();
        public PointCoordinateHypothesis BestHypothesis { get; set; }
        public double[] ResolvedSheetXY { get; set; }
        public int LivePointWitnessSide { get; set; } // 1 or 2
        public int MissingWitnessSide { get; set; } // 2 or 1
        public double PointWitnessErrorMm { get; set; }
        public bool IsResolved { get; set; }
        public string ResolutionStatus { get; set; } = "UNRESOLVED";
    }

    public sealed class PointAnchorEdgeCandidate
    {
        public int Rank { get; set; }
        public DrawingPolylineEdgeInfo EdgeInfo { get; set; }
        public int RawRecordIndex { get; set; }
        public int EntityArrayIndex { get; set; }
        public string ComponentName { get; set; }
        public string Orientation { get; set; }
        public double[] SheetStart { get; set; }
        public double[] SheetEnd { get; set; }
        public double[] AttachPoint { get; set; }
        public double AttachParamT { get; set; }
        public double WitnessProximityMm { get; set; }
        public double RayAngularErrorDeg { get; set; }
        public bool WitnessRayConsistency { get; set; } = true;
        public double ProjectedSheetDistanceMm { get; set; }
        public double ModelDistanceMm { get; set; }
        public double TargetDistanceMm { get; set; }
        public double DistanceErrorMm { get; set; }
        public double PerpendicularResidualMm { get; set; }
        public DistanceVerificationMode DistanceMode { get; set; } = DistanceVerificationMode.NORMAL_SHEET_SCALE;
        public bool CrossesActiveBreak { get; set; }
        public int BreakCrossingCount { get; set; }
        public bool PreCreateDistanceComparable { get; set; } = true;
        public string PreCreateDistanceReason { get; set; } = "";
        public bool DistanceMatched { get; set; }
        public double Score { get; set; }
        public string Reason { get; set; }
    }

    public sealed class PointAnchorDecision
    {
        public string Decision { get; set; } = "NO_CANDIDATE"; // POINT_ANCHOR_HIGH_CONFIDENCE, POINT_ANCHOR_PROVISIONAL_HIGH_CONFIDENCE, etc.
        public PointAnchorInfo PointInfo { get; set; }
        public DisplayWitnessProfile WitnessProfile { get; set; }
        public BrokenViewInfo BrokenViewInfo { get; set; }
        public DistanceVerificationMode DistanceMode { get; set; } = DistanceVerificationMode.NORMAL_SHEET_SCALE;
        public bool CrossesActiveBreak { get; set; }
        public int BreakCrossingCount { get; set; }
        public List<PointAnchorEdgeCandidate> EdgeCandidates { get; set; } = new List<PointAnchorEdgeCandidate>();
        public List<string> DuplicateLogs { get; set; } = new List<string>();
        public PointAnchorEdgeCandidate BestEdge { get; set; }
        public PointAnchorEdgeCandidate SecondEdge { get; set; }
        public double ScoreGap { get; set; }
        public double WitnessErrorGap { get; set; }
        public string AmbiguityReason { get; set; } = "";
        public string RecommendedAction { get; set; } = "MANUAL_REVIEW";
    }

    public sealed class PointAnchorProbeCandidate
    {
        public int CandidateIndex { get; set; }
        public DrawingPolylineEdgeInfo EdgeInfo { get; set; }
        public int RawRecordIndex { get; set; }
        public string ComponentName { get; set; }
        public double[] SheetStart { get; set; }
        public double[] SheetEnd { get; set; }
        public int HistoricalSide { get; set; } // 1 or 2
        public double W1ProximityMm { get; set; }
        public double W2ProximityMm { get; set; }
        public double MinProximityMm { get; set; }
        public double RayAngularErrorDeg { get; set; }
        public bool RayConsistent { get; set; } = true;
        public double[] AttachPoint { get; set; }
        public double AttachParamT { get; set; }
        public bool IsProbed { get; set; }
        public bool IsValidProbe { get; set; }
        public string RejectionReason { get; set; } = "";
        public string CreatedDimFullName { get; set; }
        public double? CreatedValueMm { get; set; }
        public double ValueDeltaMm { get; set; }
        public bool ValueMatch { get; set; }
        public bool PositionMatch { get; set; }
        public double PositionDeltaMm { get; set; }
        public bool WitnessPairMatch { get; set; }
        public double W1DeltaMm { get; set; }
        public double W2DeltaMm { get; set; }
        public bool PointReferenceMatch { get; set; }
        public string CleanupStatus { get; set; }
    }

    public sealed class PointAnchorProbeDecision
    {
        public string Decision { get; set; } = "NO_CANDIDATE"; // POINT_ANCHOR_PROBE_UNIQUE_HIGH_CONFIDENCE, POINT_ANCHOR_NO_VALID_PROBE, POINT_ANCHOR_PROBE_AMBIGUOUS, POINT_ANCHOR_PROBE_SET_TOO_LARGE
        public DisplayWitnessProfile WitnessProfile { get; set; }
        public List<PointAnchorProbeCandidate> DiscoveredCandidates { get; set; } = new List<PointAnchorProbeCandidate>();
        public List<PointAnchorProbeCandidate> PhysicalProbeCandidates { get; set; } = new List<PointAnchorProbeCandidate>();
        public List<PointAnchorProbeCandidate> ValidProbeCandidates { get; set; } = new List<PointAnchorProbeCandidate>();
        public List<string> DuplicateLogs { get; set; } = new List<string>();
        public PointAnchorProbeCandidate SelectedUniqueCandidate { get; set; }
        public string AmbiguityReason { get; set; } = "";
        public string RecommendedAction { get; set; } = "MANUAL_REVIEW";
    }

    public sealed class PolylineAuxGeometryBlock
    {
        public int BlockIndex { get; set; }
        public int AssociatedRecordIndex { get; set; }
        public int AssociatedEntityIndex { get; set; }
        public string ComponentName { get; set; }
        public string CanonicalComponentKey { get; set; }
        public bool CurveIsEllipse { get; set; }
        public bool CurveIsBcurve { get; set; }
        public int DeclaredGeomSize { get; set; }
        public int TailOffsetStart { get; set; }
        public int TailOffsetEnd { get; set; }
        public List<double> Values { get; set; } = new List<double>();
        public string EllipseParamsSummary { get; set; }
        public double[] EllipseCenter { get; set; }
        public double MajorRadius { get; set; }
        public double[] MajorAxis { get; set; }
        public double MinorRadius { get; set; }
        public double[] MinorAxis { get; set; }
    }

    public sealed class PolylineRawRecordDiagnostic
    {
        public int RecordIndex { get; set; }
        public int CursorStart { get; set; }
        public int CursorAfterHeader { get; set; }
        public int CursorAfterGeom { get; set; }
        public int CursorBeforePoints { get; set; }
        public int CursorEnd { get; set; }

        public int Type { get; set; }
        public int GeomDataSize { get; set; }
        public int GeomDataConsumed { get; set; }
        public int NumPoints { get; set; }

        // Line Attributes
        public double LineColor { get; set; }
        public double LineStyle { get; set; }
        public double LineFont { get; set; }
        public double LineWeight { get; set; }
        public double LayerID { get; set; }
        public double LayerOverride { get; set; }

        public int PointDataStart { get; set; }
        public int PointDataEnd { get; set; }

        // Full Precision Structural Diagnostics
        public double TypeRawDouble { get; set; }
        public double TypeRounded { get; set; }
        public double TypeIntegerError { get; set; }

        public double GeomSizeRawDouble { get; set; }
        public double GeomSizeRounded { get; set; }
        public double GeomSizeIntegerError { get; set; }

        public double NumPointsRawDouble { get; set; }
        public double NumPointsRounded { get; set; }
        public double NumPointsIntegerError { get; set; }

        public int CorrespondingEntityIndex { get; set; }
        public string EntityRuntimeType { get; set; }
        public bool IsValid { get; set; }
        public string Error { get; set; }

        // Curve Identity & Type Comparison Diagnostics
        public int ExpectedTypeFromCurve { get; set; } = -1;
        public bool TypeMatchesCurve { get; set; }
        public bool? CurveIsLine { get; set; }
        public bool? CurveIsCircle { get; set; }
        public bool? CurveIsEllipse { get; set; }
        public bool? CurveIsBcurve { get; set; }
        public bool? CurveIsTrimmedCurve { get; set; }

        // Component & Repair Eligibility
        public string OwnerMethod { get; set; }
        public string CanonicalComponentName { get; set; }
        public string CanonicalComponentKey { get; set; }
        public string EdgeSignature { get; set; }
        public bool IsRepairEligible { get; set; }
        public string RepairIneligibleReason { get; set; }
    }

    public sealed class DrawingPolylineEdgeInfo
    {
        public int RawRecordIndex { get; set; }
        public int EntityArrayIndex { get; set; }
        public int GeometryType { get; set; }
        public int GeometryDataSize { get; set; }
        public int GeometryDataConsumed { get; set; }
        public bool IsEligibleForRepair { get; set; }

        public object ModelEntity { get; set; }
        public IEdge ModelEdge { get; set; }

        public string OwnerMethod { get; set; }
        public Component2 Component { get; set; }
        public string ComponentName { get; set; }
        public string ComponentPath { get; set; }
        public string ComponentOccurrenceKey { get; set; }
        public string EdgeSignature { get; set; }

        // View-Local Coordinates (Direct from GetPolylines7)
        public double[] StartPt { get; set; }
        public double[] EndPt { get; set; }
        public List<double[]> Points { get; set; } = new List<double[]>();
        public List<double[]> ViewLocalPoints { get; set; } = new List<double[]>();
        public double[] ViewLocalStart { get; set; }
        public double[] ViewLocalEnd { get; set; }
        public double LengthViewLocalMm { get; set; }
        public bool IsStraight { get; set; }
        public bool InsideOrNearViewOutline { get; set; }

        // Sheet Space Coordinates
        public double[] SheetStart { get; set; }
        public double[] SheetEnd { get; set; }
        public List<double[]> SheetPoints { get; set; } = new List<double[]>();
        public double LengthSheetMm { get; set; }
        public string Orientation { get; set; }

        // Polyline Display Match Proximity
        public double DisplayProximityMm { get; set; } = double.MaxValue;
    }

    public sealed class ViewGeometryInfo
    {
        public string ViewName { get; set; }
        public int ViewType { get; set; }
        public string ViewTypeString { get; set; }
        public string ReferencedDoc { get; set; }
        public string ReferencedConfig { get; set; }
        public string ScaleRatio { get; set; }
        public double ScaleDecimal { get; set; } = 1.0;
        public double[] Outline { get; set; }

        public int VisibleComponentCount { get; set; }
        public int VisibleEdgeCount { get; set; }
        public int VisibleVertexCount { get; set; }
        public int VisibleSilhouetteCount { get; set; }

        public List<ExtractedEdgeInfo> Edges { get; set; } = new List<ExtractedEdgeInfo>();
        public List<DrawingPolylineEdgeInfo> Polylines { get; set; } = new List<DrawingPolylineEdgeInfo>();
        public List<DrawingPolylineEdgeInfo> AllPolylineRecords { get; set; } = new List<DrawingPolylineEdgeInfo>();
        public List<DrawingPolylineEdgeInfo> RepairLineRecords { get; set; } = new List<DrawingPolylineEdgeInfo>();
        public List<PolylineAuxGeometryBlock> AuxGeometryBlocks { get; set; } = new List<PolylineAuxGeometryBlock>();

        // View Transform & Sheet Location
        public double ViewX { get; set; }
        public double ViewY { get; set; }
        public double ViewXformScale { get; set; }
        public string ViewXformStatus { get; set; }

        // Polyline API & Buffer Status
        public int DisplayModeRaw { get; set; }
        public string DisplayModeName { get; set; }
        public bool FacettedHlr { get; set; }
        public bool DisplayEdgesInShaded { get; set; }
        public int PolylineCountOption0 { get; set; }
        public int PolylinePointCountOption0 { get; set; }
        public int PolylineCountOption1 { get; set; }
        public int PolylinePointCountOption1 { get; set; }
        public int PolylineReturnedEntityCount { get; set; }
        public int PolylineReturnedDoubleCount { get; set; }
        public int RawRecordCount { get; set; }

        // Geometry Type Counters
        public int Type0StraightCount { get; set; }
        public int Type1ArcCircleCount { get; set; }
        public int Type2EllipseCount { get; set; }
        public int Type3SplineCount { get; set; }
        public int UnexpectedTypeCount { get; set; }
        public int Type0NonZeroGeomDataCount { get; set; }
        public List<int> Type0NonZeroRecordIndices { get; set; } = new List<int>();
        public int SinglePointRecordCount { get; set; }
        public int ZeroPointRecordCount { get; set; }
        public int InsufficientPointRecordCount { get; set; }
        public int EligibleLinearRepairCount { get; set; }

        public int MappedPolylineEdgeCount { get; set; }
        public int SilhouettePolylineCount { get; set; }
        public int UnmappedPolylineCount { get; set; }

        public int TypeCurveMatchCount { get; set; }
        public int TypeCurveMismatchCount { get; set; }
        public int FirstMismatchRecordIndex { get; set; } = -1;
        public string FirstMismatchDescription { get; set; }

        public List<string> RawEntitySample { get; set; } = new List<string>();
        public List<double> RawDoubleSample { get; set; } = new List<double>();
        public List<string> RawBoundarySamples { get; set; } = new List<string>();
        public List<string> CandidateHeaderSamples { get; set; } = new List<string>();

        // Owner Resolution Pipeline Counters
        public int OwnerDirectIGetComponent2Count { get; set; }
        public int OwnerDirectGetComponentCount { get; set; }
        public int GetCorrespondingEntityAttemptCount { get; set; }
        public int GetCorrespondingEntityNonNullCount { get; set; }
        public int GetCorrespondingEntityNullCount { get; set; }
        public int DrawingComponentResolvedCount { get; set; }
        public int DrawingComponentNullCount { get; set; }
        public int OwnerViewGetCorrespondingEntityCount { get; set; }
        public int IsSameOwnerResolvedCount { get; set; }
        public int DirectOwnerResolvedCount { get; set; }
        public int OwnerResolvedCount { get; set; }
        public int OwnerNullCount { get; set; }
        public int OwnerAmbiguousCount { get; set; }
        public int IsSameUnsupportedCount { get; set; }

        // Auxiliary Geometry Tail Verification
        public int RecordCursorFinal { get; set; }
        public int CursorFinal { get; set; }
        public int ParsedPolylineCount { get; set; }
        public string CursorAlignment { get; set; }
        public int AuxTailStart { get; set; }
        public int AuxTailLength { get; set; }
        public int SumDeclaredAuxGeomSize { get; set; }
        public bool AuxTailSizeMatch { get; set; }
        public int AuxTailFinalCursor { get; set; }
        public string LogicalRecordParsing { get; set; }
        public string AuxTailAlignment { get; set; }
        public string FinalBufferStatus { get; set; }
        public string RecordEntityAlignment { get; set; }

        // Final Polyline Status
        public string PolylineParserStatus { get; set; }
        public string PolylineApiStatus { get; set; }
        public string PolylineRootCause { get; set; }

        public List<string> PolylineRecordConstructionLog { get; set; } = new List<string>();
        public List<PolylineRawRecordDiagnostic> RawRecordDiagnostics { get; set; } = new List<PolylineRawRecordDiagnostic>();
        public List<PolylineRawRecordDiagnostic> RecordDiagnostics { get; set; } = new List<PolylineRawRecordDiagnostic>();
    }

    public sealed class ExtractedEdgeInfo
    {
        public int Index { get; set; }
        public object Entity { get; set; }
        public object Edge { get; set; }
        public Component2 Component { get; set; }
        public string ComponentName { get; set; }
        public string ComponentPath { get; set; }
        public string ComponentOccurrenceKey { get; set; }
        public bool IsSilhouette { get; set; }
        public string GeometryType { get; set; }
        public string CurveType { get; set; }
        public bool IsLine { get; set; }
        public bool HasComponentTransform { get; set; }
        public bool HasViewTransform { get; set; }
        public string Signature { get; set; }

        public double[] RawStartPt { get; set; }
        public double[] RawEndPt { get; set; }
        public double[] RawMidPt { get; set; }
        public double[] AssemblyStartPt { get; set; }
        public double[] AssemblyEndPt { get; set; }
        public double[] AssemblyMidPt { get; set; }
        public double[] StartSheetPt { get; set; }
        public double[] EndSheetPt { get; set; }
        public double[] MidSheetPt { get; set; }
        public double[] DirectViewStartPt { get; set; }
        public double[] DirectViewEndPt { get; set; }
        public double[] DirectViewMidPt { get; set; }
        public double[] Direction2D { get; set; }
        public double[] Direction3D { get; set; }

        public double LengthMm { get; set; }
        public string Orientation { get; set; }
        public string CoordinateMethod { get; set; }
    }

    public sealed class RepairCandidate
    {
        public int Rank { get; set; }
        public int RawRecordIndex { get; set; }
        public int EntityArrayIndex { get; set; }
        public object Entity { get; set; }
        public int EntityType { get; set; }
        public string EntityTypeName { get; set; }
        public Component2 EnumerationComponent { get; set; }
        public string EnumerationComponentName { get; set; }
        public string EnumerationComponentPath { get; set; }
        public string EnumerationOccurrenceKey { get; set; }
        public Component2 Component { get; set; }
        public string ComponentName { get; set; }
        public string ComponentPath { get; set; }
        public string ComponentOccurrenceKey { get; set; }
        public string GeometryType { get; set; }
        public string Orientation { get; set; }
        public bool SameComponentAsAnchor { get; set; }
        public string CoordinateMethod { get; set; }
        public bool HasComponentTransform { get; set; }
        public bool HasViewTransform { get; set; }
        public double[] DrawingStartPt { get; set; }
        public double[] DrawingEndPt { get; set; }
        public double MeasuredSheetDistanceMm { get; set; }
        public double ViewScaleDecimal { get; set; }
        public double MeasuredModelDistanceMm { get; set; }
        public double MeasuredDistanceMm { get; set; }
        public double SignedOffsetMm { get; set; }
        public bool PreferredSide { get; set; }
        public double DisplayWitnessProximityMm { get; set; }
        public string DisplayWitnessCategory { get; set; }
        public double TargetDimensionMm { get; set; }
        public double DistanceErrorMm { get; set; }
        public bool DistanceMatched { get; set; }
        public double AnnotationDistanceMm { get; set; }
        public double Score { get; set; }
        public string Reason { get; set; }
    }

    public sealed class DanglingDimensionInfo
    {
        public string SheetName { get; set; }
        public string ViewName { get; set; }
        public string DimensionName { get; set; }
        public swDimensionType_e DimensionType { get; set; }
        public int DimensionTypeRaw { get; set; }
        public string DisplayText { get; set; }
        public double? SystemValue { get; set; }
        public double[] Position { get; set; }
        public int AttachedEntityCount { get; set; }
        public List<int> AttachedEntityTypes { get; set; } = new List<int>();
        public List<string> AttachedEntityDescriptions { get; set; } = new List<string>();
        public List<string> LostReferences { get; set; } = new List<string>();

        public int LostReferenceIndex { get; set; } = -1;
        public int AnchorReferenceIndex { get; set; } = -1;
        public object AnchorEntity { get; set; }
        public int AnchorEntityType { get; set; } = (int)swSelectType_e.swSelNOTHING;
        public Component2 AnchorComponent { get; set; }
        public string AnchorComponentName { get; set; }
        public string AnchorComponentPath { get; set; }
        public string AnchorOccurrenceKey { get; set; }

        public double[] AnchorRawStartPt { get; set; }
        public double[] AnchorRawEndPt { get; set; }
        public double[] AnchorAssemblyStartPt { get; set; }
        public double[] AnchorAssemblyEndPt { get; set; }
        public double[] AnchorDrawingStartPt { get; set; }
        public double[] AnchorDrawingEndPt { get; set; }
        public double[] AnchorDirectViewStartPt { get; set; }
        public double[] AnchorDirectViewEndPt { get; set; }
        public string AnchorCoordinateMethod { get; set; }
        public string AnchorOrientation { get; set; }
        public double AnchorDisplayProximityRouteA { get; set; } = double.MaxValue;
        public double AnchorDisplayProximityRouteB { get; set; } = double.MaxValue;

        public List<DrawingPolylineEdgeInfo> AnchorPolylineMatches { get; set; } = new List<DrawingPolylineEdgeInfo>();
        public List<DisplayDimLine> DisplayLineSegments { get; set; } = new List<DisplayDimLine>();
        public List<string> DisplayLines { get; set; } = new List<string>();
        public List<double[]> DisplayEndpoints { get; set; } = new List<double[]>();

        public List<RepairCandidate> Candidates { get; set; } = new List<RepairCandidate>();
        public string CandidateDecision { get; set; } = "NO_CANDIDATE";
        public PointAnchorDecision PointDecision { get; set; }
        public PointAnchorProbeDecision PointProbeDecision { get; set; }
        public List<string> DiagnosticNotes { get; set; } = new List<string>();

        // Failure Mode Classification
        public RepairDimFailureMode FailureMode { get; set; } = RepairDimFailureMode.Unknown;
        public string FailureModeReason { get; set; } = "";
        public bool CurrentViewModelResolved { get; set; } = true;
        public bool HasMissingModelReference { get; set; } = false;
        public string MissingModelPath { get; set; } = "";
        public string MissingModelName { get; set; } = "";
        public bool RouteCCandidateAvailable { get; set; } = false;
        public bool RequiresDimensionRecreate { get; set; } = false;
        public string RecommendedAction { get; set; } = "NONE";

        public string DimensionTypeString => DimensionType.ToString();
    }
}
