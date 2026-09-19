using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using ADDIN.Commands;

namespace ADDIN.Helpers
{
    public class EdgeFlangeRepairManager
    {
        public class EdgeRef
        {
            public string Face1Creator { get; set; }
            public string Face2Creator { get; set; }
            public double[] OriginalMidPoint { get; set; }
            public double[] OriginalStartVertex { get; set; }
            public double[] OriginalEndVertex { get; set; }
            public bool OriginalReverseOffset { get; set; }
            public double OriginalOffsetDistance { get; set; }
            public double OriginalBendAngle { get; set; }
            public int OriginalPositionType { get; set; }
            public bool UseDefaultBendRadius { get; set; }
            public double BendRadius { get; set; }
            public bool UsePositionOffset { get; set; }
            public int PositionOffsetType { get; set; }
            public double PositionOffsetDistance { get; set; }
            public bool ReversePositionOffset { get; set; }
            public bool UseDefaultBendRelief { get; set; }
            public int AutoReliefType { get; set; }
            public double ReliefWidth { get; set; }
            public double ReliefDepth { get; set; }
            public double ReliefRatio { get; set; }
        }

        private Dictionary<string, EdgeRef> flangeEdges = new Dictionary<string, EdgeRef>();

        private static double Distance(double[] p1, double[] p2)
        {
            if (p1 == null || p2 == null || p1.Length < 3 || p2.Length < 3) return double.MaxValue;
            double dx = p1[0] - p2[0];
            double dy = p1[1] - p2[1];
            double dz = p1[2] - p2[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private double[] GetEdgeMidPoint(Edge edge)
        {
            if (edge == null) return null;
            Vertex v1 = edge.GetStartVertex() as Vertex;
            Vertex v2 = edge.GetEndVertex() as Vertex;
            if (v1 != null && v2 != null)
            {
                double[] p1 = v1.GetPoint() as double[];
                double[] p2 = v2.GetPoint() as double[];
                if (p1 != null && p2 != null && p1.Length >= 3 && p2.Length >= 3)
                {
                    return new double[] { (p1[0] + p2[0]) / 2.0, (p1[1] + p2[1]) / 2.0, (p1[2] + p2[2]) / 2.0 };
                }
            }

            Curve curve = edge.GetCurve() as Curve;
            double[] se = edge.GetCurveParams2() as double[];
            if (curve != null && se != null && se.Length >= 2)
            {
                return curve.Evaluate2((se[0] + se[1]) / 2.0, 1) as double[];
            }
            return null;
        }

        public void CaptureOriginalEdges(ModelDoc2 swModel)
        {
            flangeEdges.Clear();
            if (swModel == null) return;

            Feature swFeat = swModel.FirstFeature() as Feature;
            while (swFeat != null)
            {
                if (swFeat.GetTypeName2() == "EdgeFlange")
                {
                    IEdgeFlangeFeatureData efData = swFeat.GetDefinition() as IEdgeFlangeFeatureData;
                    if (efData != null && efData.AccessSelections(swModel, null))
                    {
                        object[] edges = efData.Edges as object[];
                        if (edges != null && edges.Length > 0)
                        {
                            Edge edge = edges[0] as Edge;
                            if (edge != null)
                            {
                                object[] faces = edge.GetTwoAdjacentFaces2() as object[];
                                if (faces != null && faces.Length == 2)
                                {
                                    Feature f1 = ((Face2)faces[0]).GetFeature() as Feature;
                                    Feature f2 = ((Face2)faces[1]).GetFeature() as Feature;

                                    double[] pt = GetEdgeMidPoint(edge);
                                    Vertex v1 = edge.GetStartVertex() as Vertex;
                                    Vertex v2 = edge.GetEndVertex() as Vertex;
                                    double[] p1 = v1 != null ? v1.GetPoint() as double[] : null;
                                    double[] p2 = v2 != null ? v2.GetPoint() as double[] : null;

                                    flangeEdges[swFeat.Name] = new EdgeRef
                                    {
                                        Face1Creator = f1 != null ? f1.Name : "",
                                        Face2Creator = f2 != null ? f2.Name : "",
                                        OriginalMidPoint = pt,
                                        OriginalStartVertex = p1,
                                        OriginalEndVertex = p2,
                                        OriginalReverseOffset = efData.ReverseOffset,
                                        OriginalOffsetDistance = efData.OffsetDistance,
                                        OriginalBendAngle = efData.BendAngle,
                                        OriginalPositionType = efData.PositionType,
                                        UseDefaultBendRadius = efData.UseDefaultBendRadius,
                                        BendRadius = efData.BendRadius,
                                        UsePositionOffset = efData.UsePositionOffset,
                                        PositionOffsetType = efData.PositionOffsetType,
                                        PositionOffsetDistance = efData.PositionOffsetDistance,
                                        ReversePositionOffset = efData.ReversePositionOffset,
                                        UseDefaultBendRelief = efData.UseDefaultBendRelief,
                                        AutoReliefType = efData.AutoReliefType,
                                        ReliefWidth = efData.ReliefWidth,
                                        ReliefDepth = efData.ReliefDepth,
                                        ReliefRatio = efData.ReliefRatio
                                    };
                                    CreateMirrorPartPackage.LogDebug(string.Format(
                                        "[EDGE_FLANGE_REPAIR] Captured edge for {0} ({1}, {2}) mid=({3:F2}, {4:F2}, {5:F2})mm RevOffset={6} OffsetDist={7:F2}mm",
                                        swFeat.Name, flangeEdges[swFeat.Name].Face1Creator, flangeEdges[swFeat.Name].Face2Creator,
                                        pt != null ? pt[0] * 1000 : 0, pt != null ? pt[1] * 1000 : 0, pt != null ? pt[2] * 1000 : 0,
                                        efData.ReverseOffset, efData.OffsetDistance * 1000.0));
                                }
                            }
                        }
                        efData.ReleaseSelectionAccess();
                    }
                }
                swFeat = swFeat.GetNextFeature() as Feature;
            }
        }

        private void HealSubfeatureSketch(ModelDoc2 swModel, Feature efFeat)
        {
            if (efFeat == null || swModel == null) return;
            try
            {
                Feature subFeat = efFeat.GetFirstSubFeature() as Feature;
                while (subFeat != null)
                {
                    if (string.Equals(subFeat.GetTypeName2(), "ProfileFeature", StringComparison.OrdinalIgnoreCase))
                    {
                        bool isWarn;
                        int err = subFeat.GetErrorCode2(out isWarn);
                        if (err != 0 || isWarn)
                        {
                            CreateMirrorPartPackage.LogDebug($"[EDGE_FLANGE_REPAIR] Healing subfeature sketch {subFeat.Name} (err={err}, isWarn={isWarn})");
                            swModel.ClearSelection2(true);
                            subFeat.Select2(false, 0);
                            swModel.EditSketch();
                            Sketch sk = subFeat.GetSpecificFeature2() as Sketch;
                            if (sk != null && sk.RelationManager != null)
                            {
                                object[] dangling = sk.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swDangling) as object[];
                                if (dangling != null)
                                {
                                    foreach (object d in dangling)
                                    {
                                        SketchRelation r = d as SketchRelation;
                                        if (r != null)
                                        {
                                            try { sk.RelationManager.DeleteRelation(r); } catch { }
                                        }
                                    }
                                }
                            }
                            swModel.InsertSketch2(true);
                            swModel.ClearSelection2(true);
                        }
                        break;
                    }
                    subFeat = subFeat.GetNextSubFeature() as Feature;
                }
            }
            catch (Exception ex)
            {
                CreateMirrorPartPackage.LogDebug($"[EDGE_FLANGE_REPAIR] Exception healing subfeature sketch: {ex.Message}");
            }
        }

        public bool TryRepairEdgeFlange(ModelDoc2 swModel, Feature efFeat)
        {
            return TryRepairEdgeFlange(null, swModel, efFeat);
        }

        public bool TryRepairEdgeFlange(ISldWorks swApp, ModelDoc2 swModel, Feature efFeat)
        {
            if (efFeat == null || swModel == null) return false;
            if (!flangeEdges.ContainsKey(efFeat.Name)) return false;
            EdgeRef orig = flangeEdges[efFeat.Name];

            if (swApp == null)
            {
                try { swApp = (ISldWorks)System.Runtime.InteropServices.Marshal.GetActiveObject("SldWorks.Application"); } catch { }
            }
            MathTransform mirrorTransform = CreateMirrorPartPackage.currentMirrorTransform;

            if (mirrorTransform == null)
            {
                CreateMirrorPartPackage.LogDebug("[EDGE_FLANGE_REPAIR] currentMirrorTransform is NULL.");
                return false;
            }

            // 1. Proactively heal internal profile sketch if broken by upstream cut regenerations
            HealSubfeatureSketch(swModel, efFeat);

            // 2. Calculate expected mirrored midpoint and vertices
            MathUtility mathUtil = swApp.IGetMathUtility();
            double[] expectedPt = null;
            if (orig.OriginalMidPoint != null)
            {
                MathPoint mp = mathUtil.CreatePoint(orig.OriginalMidPoint) as MathPoint;
                MathPoint mirroredMp = mp.MultiplyTransform(mirrorTransform) as MathPoint;
                if (mirroredMp != null) expectedPt = mirroredMp.ArrayData as double[];
            }

            double[] expStart = null;
            if (orig.OriginalStartVertex != null)
            {
                MathPoint mpStart = mathUtil.CreatePoint(orig.OriginalStartVertex) as MathPoint;
                MathPoint mirroredStart = mpStart.MultiplyTransform(mirrorTransform) as MathPoint;
                if (mirroredStart != null) expStart = mirroredStart.ArrayData as double[];
            }

            double[] expEnd = null;
            if (orig.OriginalEndVertex != null)
            {
                MathPoint mpEnd = mathUtil.CreatePoint(orig.OriginalEndVertex) as MathPoint;
                MathPoint mirroredEnd = mpEnd.MultiplyTransform(mirrorTransform) as MathPoint;
                if (mirroredEnd != null) expEnd = mirroredEnd.ArrayData as double[];
            }

            // 3. Find matching edge on the mirrored part
            Edge bestEdge = null;
            double bestDist = double.MaxValue;

            object[] bodies = ((PartDoc)swModel).GetBodies2((int)swBodyType_e.swAllBodies, false) as object[];
            if (bodies != null && expectedPt != null)
            {
                foreach (object bodyObj in bodies)
                {
                    Body2 body = bodyObj as Body2;
                    if (body == null) continue;
                    object[] edges = body.GetEdges() as object[];
                    if (edges != null)
                    {
                        foreach (object edgeObj in edges)
                        {
                            Edge candEdge = edgeObj as Edge;
                            if (candEdge == null) continue;
                            object[] faces = candEdge.GetTwoAdjacentFaces2() as object[];
                            if (faces != null && faces.Length == 2)
                            {
                                Feature f1 = ((Face2)faces[0]).GetFeature() as Feature;
                                Feature f2 = ((Face2)faces[1]).GetFeature() as Feature;
                                string n1 = f1 != null ? f1.Name : "";
                                string n2 = f2 != null ? f2.Name : "";

                                if ((n1 == orig.Face1Creator && n2 == orig.Face2Creator) ||
                                    (n2 == orig.Face1Creator && n1 == orig.Face2Creator))
                                {
                                    double[] candPt = GetEdgeMidPoint(candEdge);
                                    if (candPt != null)
                                    {
                                        double d = Distance(candPt, expectedPt);
                                        if (d < bestDist)
                                        {
                                            bestDist = d;
                                            bestEdge = candEdge;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // 4. Determine deterministic ReverseOffset based on edge vertex direction
            bool inverted = false;
            if (bestEdge != null && expStart != null && expEnd != null)
            {
                Vertex cv1 = bestEdge.GetStartVertex() as Vertex;
                Vertex cv2 = bestEdge.GetEndVertex() as Vertex;
                double[] cp1 = cv1 != null ? cv1.GetPoint() as double[] : null;
                double[] cp2 = cv2 != null ? cv2.GetPoint() as double[] : null;
                if (cp1 != null && cp2 != null)
                {
                    double dDirect = Distance(cp1, expStart) + Distance(cp2, expEnd);
                    double dInvert = Distance(cp1, expEnd) + Distance(cp2, expStart);
                    if (dInvert < dDirect) inverted = true;
                    CreateMirrorPartPackage.LogDebug(string.Format(
                        "[EDGE_FLANGE_REPAIR] Edge direction check for {0}: dDirect={1:F3}mm, dInvert={2:F3}mm -> inverted={3}",
                        efFeat.Name, dDirect * 1000, dInvert * 1000, inverted));
                }
            }

            bool targetRevOffset = orig.OriginalReverseOffset;
            if (orig.OriginalOffsetDistance > 1e-6)
            {
                targetRevOffset = inverted ? !orig.OriginalReverseOffset : orig.OriginalReverseOffset;
            }

            // Priority order of reverse offset: primary deterministic value, then alternative
            bool[] testReverseOptions = new bool[] { targetRevOffset, !targetRevOffset };

            foreach (bool rev in testReverseOptions)
            {
                IEdgeFlangeFeatureData efData = efFeat.GetDefinition() as IEdgeFlangeFeatureData;
                if (efData != null && efData.AccessSelections(swModel, null))
                {
                    bool mod = false;
                    try
                    {
                        if (bestEdge != null)
                        {
                            ISelectionMgr selMgr = (ISelectionMgr)swModel.SelectionManager;
                            SelectData selData = selMgr.CreateSelectData();
                            selData.Mark = 1;
                            ((Entity)bestEdge).Select4(false, selData);
                            try { efData.Edge = bestEdge; } catch { }
                            try { efData.Edges = new object[] { bestEdge }; } catch { }
                        }

                        efData.BendAngle = orig.OriginalBendAngle;
                        efData.PositionType = orig.OriginalPositionType;
                        efData.ReverseOffset = rev;
                        if (orig.OriginalOffsetDistance > 0)
                        {
                            efData.OffsetDistance = orig.OriginalOffsetDistance;
                        }
                        if (!orig.UseDefaultBendRadius && orig.BendRadius > 0)
                        {
                            try { efData.UseDefaultBendRadius = false; efData.BendRadius = orig.BendRadius; } catch { }
                        }
                        if (orig.UsePositionOffset)
                        {
                            try
                            {
                                efData.UsePositionOffset = true;
                                efData.PositionOffsetType = orig.PositionOffsetType;
                                efData.PositionOffsetDistance = orig.PositionOffsetDistance;
                                efData.ReversePositionOffset = orig.ReversePositionOffset;
                            }
                            catch { }
                        }
                        if (!orig.UseDefaultBendRelief)
                        {
                            try
                            {
                                efData.UseDefaultBendRelief = false;
                                efData.AutoReliefType = orig.AutoReliefType;
                                efData.ReliefWidth = orig.ReliefWidth;
                                efData.ReliefDepth = orig.ReliefDepth;
                                efData.ReliefRatio = orig.ReliefRatio;
                            }
                            catch { }
                        }

                        mod = efFeat.ModifyDefinition(efData, swModel, null);
                    }
                    catch (Exception modEx)
                    {
                        CreateMirrorPartPackage.LogDebug($"[EDGE_FLANGE_REPAIR] ModifyDefinition exception: {modEx.Message}");
                    }
                    finally
                    {
                        if (!mod)
                        {
                            try { efData.ReleaseSelectionAccess(); } catch { }
                        }
                    }

                    if (mod)
                    {
                        swModel.ForceRebuild3(false);
                        bool isWarn;
                        int err = efFeat.GetErrorCode2(out isWarn);
                        CreateMirrorPartPackage.LogDebug(string.Format(
                            "[EDGE_FLANGE_REPAIR] Tested ReverseOffset={0} -> mod={1}, err={2}, warn={3}",
                            rev, mod, err, isWarn));
                        if (err == 0 || isWarn)
                        {
                            return true;
                        }
                    }
                }
            }

            CreateMirrorPartPackage.LogDebug(string.Format("[EDGE_FLANGE_REPAIR] Could not repair {0}", efFeat.Name));
            return false;
        }
    }
}
