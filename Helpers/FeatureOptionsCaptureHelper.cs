using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Helpers
{
    public class ExtrudeOptionsSnapshot
    {
        public string FeatureName { get; set; }
        public bool IsCut { get; set; }
        public bool FlipSideToCut { get; set; }
        public bool ReverseDirection { get; set; }
        public bool BothDirections { get; set; }
        public int EndCondition1 { get; set; }
        public double Depth1 { get; set; }
        public int EndCondition2 { get; set; }
        public double Depth2 { get; set; }
        public bool NormalCut { get; set; }
        public bool LinkToThickness { get; set; }
        public bool IsOpenProfile { get; set; }
        public double[] ExtrusionDirection { get; set; }
        public double[] SketchPlaneNormal { get; set; }
    }

    public class EdgeFlangeOptionsSnapshot
    {
        public string FeatureName { get; set; }
        public double BendAngle { get; set; }
        public int PositionType { get; set; }
        public int OffsetType { get; set; }
        public double OffsetDistance { get; set; }
        public bool ReverseOffset { get; set; }
        public bool UseDefaultBendRadius { get; set; }
        public double BendRadius { get; set; }
        public bool UsePositionOffset { get; set; }
        public int PositionOffsetType { get; set; }
        public double PositionOffsetDistance { get; set; }
        public bool ReversePositionOffset { get; set; }
        public bool UseDefaultBendAllowance { get; set; }
        public bool UseDefaultBendRelief { get; set; }
        public int AutoReliefType { get; set; }
        public double ReliefWidth { get; set; }
        public double ReliefDepth { get; set; }
        public double ReliefRatio { get; set; }
        public bool LockAngle { get; set; }
        public double[] StartVertex { get; set; }
        public double[] EndVertex { get; set; }
        public double[] MidPoint { get; set; }
        public string Face1Creator { get; set; }
        public string Face2Creator { get; set; }
    }

    public class PatternOptionsSnapshot
    {
        public string FeatureName { get; set; }
        public string PatternType { get; set; } // Linear, Curve, Circular
        public bool D1ReverseDirection { get; set; }
        public double[] D1Vector { get; set; }
        public double D1Spacing { get; set; }
        public int D1Instances { get; set; }
        public bool D2ReverseDirection { get; set; }
        public double[] D2Vector { get; set; }
        public double D2Spacing { get; set; }
        public int D2Instances { get; set; }
        public bool GeometryPattern { get; set; }
    }

    public class FeatureOptionsCaptureHelper
    {
        public Dictionary<string, ExtrudeOptionsSnapshot> ExtrudeSnapshots { get; } = new Dictionary<string, ExtrudeOptionsSnapshot>();
        public Dictionary<string, EdgeFlangeOptionsSnapshot> EdgeFlangeSnapshots { get; } = new Dictionary<string, EdgeFlangeOptionsSnapshot>();
        public Dictionary<string, PatternOptionsSnapshot> PatternSnapshots { get; } = new Dictionary<string, PatternOptionsSnapshot>();

        public void Clear()
        {
            ExtrudeSnapshots.Clear();
            EdgeFlangeSnapshots.Clear();
            PatternSnapshots.Clear();
        }

        public void CaptureFromModel(ModelDoc2 swModel)
        {
            Clear();
            if (swModel == null) return;

            Feature feat = swModel.FirstFeature() as Feature;
            while (feat != null)
            {
                string typeName = feat.GetTypeName2();
                if (typeName == "EdgeFlange")
                {
                    CaptureEdgeFlange(swModel, feat);
                }
                else if (typeName == "Extrusion" || typeName == "Cut" || typeName == "ICE")
                {
                    CaptureExtrude(swModel, feat);
                }
                else if (typeName == "LPattern" || typeName == "LinearPattern")
                {
                    CaptureLinearPattern(swModel, feat);
                }
                else if (typeName == "CirPattern" || typeName == "CircularPattern")
                {
                    CaptureCircularPattern(swModel, feat);
                }
                else if (typeName == "CurvePattern")
                {
                    CaptureCurvePattern(swModel, feat);
                }

                feat = feat.GetNextFeature() as Feature;
            }
        }

        private void CaptureEdgeFlange(ModelDoc2 swModel, Feature feat)
        {
            try
            {
                IEdgeFlangeFeatureData ef = feat.GetDefinition() as IEdgeFlangeFeatureData;
                if (ef != null && ef.AccessSelections(swModel, null))
                {
                    EdgeFlangeOptionsSnapshot snap = new EdgeFlangeOptionsSnapshot
                    {
                        FeatureName = feat.Name,
                        BendAngle = ef.BendAngle,
                        PositionType = ef.PositionType,
                        OffsetType = ef.OffsetType,
                        OffsetDistance = ef.OffsetDistance,
                        ReverseOffset = ef.ReverseOffset,
                        UseDefaultBendRadius = ef.UseDefaultBendRadius,
                        BendRadius = ef.BendRadius,
                        UsePositionOffset = ef.UsePositionOffset,
                        PositionOffsetType = ef.PositionOffsetType,
                        PositionOffsetDistance = ef.PositionOffsetDistance,
                        ReversePositionOffset = ef.ReversePositionOffset,
                        UseDefaultBendAllowance = ef.UseDefaultBendAllowance,
                        UseDefaultBendRelief = ef.UseDefaultBendRelief,
                        AutoReliefType = ef.AutoReliefType,
                        ReliefWidth = ef.ReliefWidth,
                        ReliefDepth = ef.ReliefDepth,
                        ReliefRatio = ef.ReliefRatio,
                        LockAngle = ef.LockAngle
                    };

                    object[] edges = ef.Edges as object[];
                    if (edges != null && edges.Length > 0)
                    {
                        Edge ed = edges[0] as Edge;
                        if (ed != null)
                        {
                            Vertex v1 = ed.GetStartVertex() as Vertex;
                            Vertex v2 = ed.GetEndVertex() as Vertex;
                            if (v1 != null && v2 != null)
                            {
                                snap.StartVertex = v1.GetPoint() as double[];
                                snap.EndVertex = v2.GetPoint() as double[];
                                if (snap.StartVertex != null && snap.EndVertex != null &&
                                    snap.StartVertex.Length >= 3 && snap.EndVertex.Length >= 3)
                                {
                                    snap.MidPoint = new double[]
                                    {
                                        (snap.StartVertex[0] + snap.EndVertex[0]) / 2.0,
                                        (snap.StartVertex[1] + snap.EndVertex[1]) / 2.0,
                                        (snap.StartVertex[2] + snap.EndVertex[2]) / 2.0
                                    };
                                }
                            }

                            if (snap.MidPoint == null)
                            {
                                Curve c = ed.GetCurve() as Curve;
                                double[] se = ed.GetCurveParams2() as double[];
                                if (c != null && se != null && se.Length >= 2)
                                {
                                    snap.MidPoint = c.Evaluate2((se[0] + se[1]) / 2.0, 1) as double[];
                                }
                            }

                            object[] faces = ed.GetTwoAdjacentFaces2() as object[];
                            if (faces != null && faces.Length == 2)
                            {
                                Feature f1 = ((Face2)faces[0]).GetFeature() as Feature;
                                Feature f2 = ((Face2)faces[1]).GetFeature() as Feature;
                                snap.Face1Creator = f1 != null ? f1.Name : "";
                                snap.Face2Creator = f2 != null ? f2.Name : "";
                            }
                        }
                    }

                    EdgeFlangeSnapshots[feat.Name] = snap;
                    ef.ReleaseSelectionAccess();
                }
            }
            catch { }
        }

        private void CaptureExtrude(ModelDoc2 swModel, Feature feat)
        {
            try
            {
                ExtrudeFeatureData2 ext = feat.GetDefinition() as ExtrudeFeatureData2;
                if (ext != null && ext.AccessSelections(swModel, null))
                {
                    ExtrudeOptionsSnapshot snap = new ExtrudeOptionsSnapshot
                    {
                        FeatureName = feat.Name,
                        IsCut = string.Equals(feat.GetTypeName2(), "Cut", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(feat.GetTypeName2(), "ICE", StringComparison.OrdinalIgnoreCase),
                        FlipSideToCut = ext.FlipSideToCut,
                        ReverseDirection = ext.ReverseDirection,
                        BothDirections = ext.BothDirections,
                        EndCondition1 = ext.GetEndCondition(true),
                        Depth1 = ext.GetDepth(true),
                        EndCondition2 = ext.BothDirections ? ext.GetEndCondition(false) : 0,
                        Depth2 = ext.BothDirections ? ext.GetDepth(false) : 0,
                        NormalCut = ext.NormalCut,
                        LinkToThickness = ext.LinkToThickness
                    };

                    // Check if sketch is open profile
                    Feature subFeat = feat.GetFirstSubFeature() as Feature;
                    while (subFeat != null)
                    {
                        if (subFeat.GetTypeName2() == "ProfileFeature")
                        {
                            Sketch sk = subFeat.GetSpecificFeature2() as Sketch;
                            if (sk != null)
                            {
                                snap.IsOpenProfile = SketchOperationsHelper.IsOpenProfileSketch(sk);
                            }
                            break;
                        }
                        subFeat = subFeat.GetNextSubFeature() as Feature;
                    }

                    ExtrudeSnapshots[feat.Name] = snap;
                    ext.ReleaseSelectionAccess();
                }
            }
            catch { }
        }

        private void CaptureLinearPattern(ModelDoc2 swModel, Feature feat)
        {
            try
            {
                ILinearPatternFeatureData lpd = feat.GetDefinition() as ILinearPatternFeatureData;
                if (lpd != null && lpd.AccessSelections(swModel, null))
                {
                    PatternOptionsSnapshot snap = new PatternOptionsSnapshot
                    {
                        FeatureName = feat.Name,
                        PatternType = "Linear",
                        D1ReverseDirection = lpd.D1ReverseDirection,
                        D1Spacing = lpd.D1Spacing,
                        D1Instances = lpd.D1TotalInstances,
                        D2ReverseDirection = lpd.IsDirection2Specified() && lpd.D2ReverseDirection,
                        D2Spacing = lpd.IsDirection2Specified() ? lpd.D2Spacing : 0,
                        D2Instances = lpd.IsDirection2Specified() ? lpd.D2TotalInstances : 0,
                        GeometryPattern = lpd.GeometryPattern
                    };
                    PatternSnapshots[feat.Name] = snap;
                    lpd.ReleaseSelectionAccess();
                }
            }
            catch { }
        }

        private void CaptureCircularPattern(ModelDoc2 swModel, Feature feat)
        {
            try
            {
                ICircularPatternFeatureData cpd = feat.GetDefinition() as ICircularPatternFeatureData;
                if (cpd != null && cpd.AccessSelections(swModel, null))
                {
                    PatternOptionsSnapshot snap = new PatternOptionsSnapshot
                    {
                        FeatureName = feat.Name,
                        PatternType = "Circular",
                        D1ReverseDirection = cpd.ReverseDirection,
                        D1Spacing = cpd.Spacing,
                        D1Instances = cpd.TotalInstances,
                        D2ReverseDirection = cpd.Direction2 && cpd.Symmetric,
                        D2Spacing = cpd.Direction2 ? cpd.Spacing2 : 0,
                        D2Instances = cpd.Direction2 ? cpd.TotalInstances2 : 0,
                        GeometryPattern = cpd.GeometryPattern
                    };
                    PatternSnapshots[feat.Name] = snap;
                    cpd.ReleaseSelectionAccess();
                }
            }
            catch { }
        }

        private void CaptureCurvePattern(ModelDoc2 swModel, Feature feat)
        {
            try
            {
                ICurveDrivenPatternFeatureData cdp = feat.GetDefinition() as ICurveDrivenPatternFeatureData;
                if (cdp != null && cdp.AccessSelections(swModel, null))
                {
                    PatternOptionsSnapshot snap = new PatternOptionsSnapshot
                    {
                        FeatureName = feat.Name,
                        PatternType = "Curve",
                        D1ReverseDirection = cdp.D1ReverseDirection,
                        D1Spacing = cdp.D1Spacing,
                        D1Instances = cdp.D1InstanceCount,
                        D2ReverseDirection = cdp.Dir2Specified && cdp.D2ReverseDirection,
                        D2Spacing = cdp.Dir2Specified ? cdp.D2Spacing : 0,
                        D2Instances = cdp.Dir2Specified ? cdp.D2InstanceCount : 0
                    };
                    PatternSnapshots[feat.Name] = snap;
                    cdp.ReleaseSelectionAccess();
                }
            }
            catch { }
        }
    }
}
