using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public static class RepairDimCandidateFinder
    {
        public static string GetComponentOccurrenceKey(Component2 comp)
        {
            if (comp == null)
                return null;

            string occurrenceName = "";
            string modelPath = "";

            try
            {
                occurrenceName = comp.Name2 ?? "";
            }
            catch {}

            try
            {
                modelPath = comp.GetPathName() ?? "";
            }
            catch {}

            occurrenceName = occurrenceName.Trim();
            modelPath = modelPath.Trim();

            if (string.IsNullOrEmpty(occurrenceName) && string.IsNullOrEmpty(modelPath))
            {
                return null;
            }

            return occurrenceName + "|" + modelPath;
        }

        public static bool IsSameComponent(Component2 anchor, Component2 candidate)
        {
            if (anchor == null || candidate == null)
            {
                return false;
            }

            string anchorKey = GetComponentOccurrenceKey(anchor);
            string candidateKey = GetComponentOccurrenceKey(candidate);

            if (string.IsNullOrWhiteSpace(anchorKey) || string.IsNullOrWhiteSpace(candidateKey))
            {
                return false;
            }

            return string.Equals(anchorKey, candidateKey, StringComparison.OrdinalIgnoreCase);
        }

        public static ViewGeometryInfo EnumerateViewGeometry(
            ISldWorks swApp,
            SolidWorks.Interop.sldworks.View view,
            string viewScanPrefix = null)
        {
            if (view == null) return null;

            string vPrefix = viewScanPrefix;

            ViewGeometryInfo geomInfo = new ViewGeometryInfo
            {
                ViewName = view.Name,
                ViewType = view.Type,
                ViewTypeString = ((swDrawingViewTypes_e)view.Type).ToString()
            };

            try { geomInfo.ReferencedDoc = view.ReferencedDocument?.GetTitle() ?? "<unavailable>"; } catch { geomInfo.ReferencedDoc = "<unavailable>"; }
            try { geomInfo.ReferencedConfig = view.ReferencedConfiguration ?? "<unavailable>"; } catch { geomInfo.ReferencedConfig = "<unavailable>"; }
            try
            {
                double[] scale = view.ScaleRatio as double[];
                if (scale != null && scale.Length >= 2)
                    geomInfo.ScaleRatio = $"{scale[0]}:{scale[1]}";
                else
                    geomInfo.ScaleRatio = $"{view.ScaleDecimal:F3}";
            }
            catch
            {
                geomInfo.ScaleRatio = "<unavailable>";
            }

            // Extract View ScaleDecimal
            double scaleDecimal = 1.0;
            try
            {
                scaleDecimal = view.ScaleDecimal;
            }
            catch
            {
                scaleDecimal = 1.0;
            }

            if (scaleDecimal <= 0.0 || double.IsNaN(scaleDecimal) || double.IsInfinity(scaleDecimal))
            {
                scaleDecimal = 1.0;
            }
            geomInfo.ScaleDecimal = scaleDecimal;

            try
            {
                object outlineObj = view.GetOutline();
                if (outlineObj is double[] o && o.Length >= 4)
                {
                    geomInfo.Outline = new double[] { o[0], o[1], o[2], o[3] };
                }
            }
            catch {}

            // Enumerate Components in View
            if (!string.IsNullOrEmpty(vPrefix))
            {
                RepairDanglingDimensions.LogDebug($"{vPrefix} H ABOUT_TO_GET_VISIBLE_COMPONENTS");
            }

            object[] comps = null;
            try
            {
                object compsObj = view.GetVisibleComponents();
                if (compsObj is object[] arr) comps = arr;
            }
            catch {}

            if (!string.IsNullOrEmpty(vPrefix))
            {
                RepairDanglingDimensions.LogDebug($"{vPrefix} I GET_VISIBLE_COMPONENTS_RETURNED (Count={(comps != null ? comps.Length : 0)})");
            }

            HashSet<string> seenSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (comps != null && comps.Length > 0)
            {
                geomInfo.VisibleComponentCount = comps.Length;

                if (!string.IsNullOrEmpty(vPrefix))
                {
                    RepairDanglingDimensions.LogDebug($"{vPrefix} J ABOUT_TO_GET_VISIBLE_ENTITIES");
                }

                foreach (object compObj in comps)
                {
                    Component2 comp = compObj as Component2;
                    if (comp == null) continue;

                    // 1. Visible Normal Edges
                    try
                    {
                        object edgesObj = view.GetVisibleEntities2(comp, (int)swViewEntityType_e.swViewEntityType_Edge);
                        if (edgesObj is object[] edgeArr)
                        {
                            geomInfo.VisibleEdgeCount += edgeArr.Length;
                            foreach (object edge in edgeArr)
                            {
                                ExtractedEdgeInfo edgeInfo = RepairDimGeometry.ExtractEdgeGeometry(swApp, edge, comp, view, null, false);
                                if (edgeInfo != null && !string.IsNullOrEmpty(edgeInfo.Signature))
                                {
                                    if (seenSignatures.Add(edgeInfo.Signature))
                                    {
                                        geomInfo.Edges.Add(edgeInfo);
                                    }
                                }
                            }
                        }
                    }
                    catch {}

                    // 2. Visible Silhouette Edges
                    try
                    {
                        object silObj = view.GetVisibleEntities2(comp, (int)swViewEntityType_e.swViewEntityType_SilhouetteEdge);
                        if (silObj is object[] silArr)
                        {
                            geomInfo.VisibleSilhouetteCount += silArr.Length;
                            foreach (object sil in silArr)
                            {
                                ExtractedEdgeInfo edgeInfo = RepairDimGeometry.ExtractEdgeGeometry(swApp, sil, comp, view, null, true);
                                if (edgeInfo != null && !string.IsNullOrEmpty(edgeInfo.Signature))
                                {
                                    if (seenSignatures.Add(edgeInfo.Signature))
                                    {
                                        geomInfo.Edges.Add(edgeInfo);
                                    }
                                }
                            }
                        }
                    }
                    catch {}

                    // 3. Vertices (for stats)
                    try
                    {
                        object vertObj = view.GetVisibleEntities2(comp, (int)swViewEntityType_e.swViewEntityType_Vertex);
                        if (vertObj is object[] vertArr)
                        {
                            geomInfo.VisibleVertexCount += vertArr.Length;
                        }
                    }
                    catch {}
                }

                if (!string.IsNullOrEmpty(vPrefix))
                {
                    RepairDanglingDimensions.LogDebug($"{vPrefix} K GET_VISIBLE_ENTITIES_RETURNED (Count={geomInfo.VisibleEdgeCount})");
                }
            }
            else
            {
                // Fallback for Part Drawing or views without component array
                geomInfo.VisibleComponentCount = 1;

                if (!string.IsNullOrEmpty(vPrefix))
                {
                    RepairDanglingDimensions.LogDebug($"{vPrefix} J ABOUT_TO_GET_VISIBLE_ENTITIES");
                }

                // 1. Normal Edges
                try
                {
                    object edgesObj = view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge);
                    if (edgesObj is object[] edgeArr)
                    {
                        geomInfo.VisibleEdgeCount += edgeArr.Length;
                        foreach (object edge in edgeArr)
                        {
                            ExtractedEdgeInfo edgeInfo = RepairDimGeometry.ExtractEdgeGeometry(swApp, edge, null, view, null, false);
                            if (edgeInfo != null && !string.IsNullOrEmpty(edgeInfo.Signature))
                            {
                                if (seenSignatures.Add(edgeInfo.Signature))
                                {
                                    geomInfo.Edges.Add(edgeInfo);
                                }
                            }
                        }
                    }
                }
                catch {}

                // 2. Silhouette Edges
                try
                {
                    object silObj = view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_SilhouetteEdge);
                    if (silObj is object[] silArr)
                    {
                        geomInfo.VisibleSilhouetteCount += silArr.Length;
                        foreach (object sil in silArr)
                        {
                            ExtractedEdgeInfo edgeInfo = RepairDimGeometry.ExtractEdgeGeometry(swApp, sil, null, view, null, true);
                            if (edgeInfo != null && !string.IsNullOrEmpty(edgeInfo.Signature))
                            {
                                if (seenSignatures.Add(edgeInfo.Signature))
                                {
                                    geomInfo.Edges.Add(edgeInfo);
                                }
                            }
                        }
                    }
                }
                catch {}

                // 3. Vertices
                try
                {
                    object vertObj = view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Vertex);
                    if (vertObj is object[] vertArr)
                    {
                        geomInfo.VisibleVertexCount += vertArr.Length;
                    }
                }
                catch {}

                if (!string.IsNullOrEmpty(vPrefix))
                {
                    RepairDanglingDimensions.LogDebug($"{vPrefix} K GET_VISIBLE_ENTITIES_RETURNED (Count={geomInfo.VisibleEdgeCount})");
                }
            }

            // STEP 8D-FIX1A / FIX3E: Enumerate Drawing Polylines & Diagnostic
            try
            {
                geomInfo.Polylines = RepairDimGeometry.EnumerateDrawingPolylines(swApp, view, geomInfo, vPrefix);
                foreach (var p in geomInfo.Polylines)
                {
                    if (p.ModelEntity is IEdge) geomInfo.MappedPolylineEdgeCount++;
                    else if (p.ModelEntity is ISilhouetteEdge) geomInfo.SilhouettePolylineCount++;
                    else geomInfo.UnmappedPolylineCount++;
                }
            }
            catch (Exception ex)
            {
                geomInfo.PolylineApiStatus = "EXCEPTION: " + ex.GetType().FullName + " | " + ex.Message;
            }

            if (!string.IsNullOrEmpty(vPrefix))
            {
                RepairDanglingDimensions.LogDebug($"{vPrefix} T VIEW_GEOMETRY_COMPLETE");
            }

            return geomInfo;
        }

        // STEP 8E-FIX2: DISPLAY-WITNESS EVIDENCE + SAFE ROUTE-C CANDIDATE ENGINE
        public static void AnalyzeCandidatesForDimension(
            ISldWorks swApp,
            DanglingDimensionInfo dimInfo,
            ViewGeometryInfo viewGeom,
            SolidWorks.Interop.sldworks.View view,
            DisplayDimension dispDim)
        {
            if (dimInfo == null || viewGeom == null || view == null) return;

            List<DisplayDimLine> displayLineSegments = new List<DisplayDimLine>();
            List<double[]> displayEndpoints = new List<double[]>();

            // Extract Display Dimension lines for debugging, segment analysis & witness proximity validation
            if (dispDim != null)
            {
                try
                {
                    DisplayData dd = dispDim.GetDisplayData() as DisplayData;
                    if (dd != null)
                    {
                        int lCount = dd.GetLineCount();
                        for (int li = 0; li < lCount; li++)
                        {
                            object lObj = dd.GetLineAtIndex3(li);
                            if (lObj is double[] lArr && lArr.Length >= 10)
                            {
                                int lType = Convert.ToInt32(lArr[1]);
                                double sx = lArr[4];
                                double sy = lArr[5];
                                double sz = lArr[6];
                                double ex = lArr[7];
                                double ey = lArr[8];
                                double ez = lArr[9];

                                displayLineSegments.Add(new DisplayDimLine
                                {
                                    LineIndex = li,
                                    LineType = lType,
                                    StartX = sx,
                                    StartY = sy,
                                    StartZ = sz,
                                    EndX = ex,
                                    EndY = ey,
                                    EndZ = ez
                                });

                                dimInfo.DisplayLines.Add($"Line[{li}]: Type={lType}, Start=({sx:F4}, {sy:F4}), End=({ex:F4}, {ey:F4})");
                                displayEndpoints.Add(new double[] { sx, sy, sz });
                                displayEndpoints.Add(new double[] { ex, ey, ez });
                            }
                        }
                    }
                }
                catch {}
            }

            dimInfo.DisplayLineSegments = displayLineSegments;
            dimInfo.DisplayEndpoints = displayEndpoints;

            // 1. Check Priority A Eligibility: Exactly 1 lost ref and 1 live anchor ref
            if (dimInfo.AnchorReferenceIndex == -1 || dimInfo.AttachedEntityCount < 2)
            {
                dimInfo.CandidateDecision = "DEFERRED_FULLY_LOST";
                return;
            }

            // 2. Check Anchor Type
            if (dimInfo.AnchorEntityType == (int)swSelectType_e.swSelSKETCHPOINTS)
            {
                AnalyzePointAnchorCandidates(swApp, dimInfo, viewGeom, view, dispDim);
                return;
            }

            if (dimInfo.AnchorEntityType != (int)swSelectType_e.swSelEDGES)
            {
                dimInfo.CandidateDecision = "UNSUPPORTED_ANCHOR_TYPE";
                return;
            }

            // 3. Giới hạn Dimension Type trong giai đoạn này (Linear only)
            bool isLinearType =
                dimInfo.DimensionType == swDimensionType_e.swLinearDimension ||
                dimInfo.DimensionType == swDimensionType_e.swHorLinearDimension ||
                dimInfo.DimensionType == swDimensionType_e.swVertLinearDimension;

            if (!isLinearType)
            {
                dimInfo.CandidateDecision = "UNSUPPORTED_TYPE";
                return;
            }

            // 4. Parser Safety Guard
            bool isParserVerified = (viewGeom.PolylineParserStatus == "PASS" || viewGeom.PolylineParserStatus == "PASS_WITH_AUX_GEOMETRY_TAIL") &&
                                    (viewGeom.PolylineApiStatus == "POLYLINE_API_OK");
            if (!isParserVerified)
            {
                dimInfo.CandidateDecision = "GEOMETRY_UNVERIFIED";
                dimInfo.DiagnosticNotes.Add($"GEOMETRY_UNVERIFIED: ParserStatus={viewGeom.PolylineParserStatus}, ApiStatus={viewGeom.PolylineApiStatus}");
                return;
            }

            if (!dimInfo.SystemValue.HasValue || dimInfo.SystemValue.Value <= 1e-7)
            {
                dimInfo.CandidateDecision = "NO_CANDIDATE";
                return;
            }

            double targetDimensionMm = Math.Abs(dimInfo.SystemValue.Value) * 1000.0;

            // Extract Anchor Leaf Component2 Occurrence from IEntity.GetDrawingComponent
            IEntity anchorIEntity = dimInfo.AnchorEntity as IEntity;
            DrawingComponent dc = null;
            try
            {
                dc = anchorIEntity?.GetDrawingComponent(view);
            }
            catch {}

            Component2 anchorComp = null;
            if (dc != null)
            {
                try { anchorComp = dc.Component; } catch {}
            }
            if (anchorComp == null && anchorIEntity != null)
            {
                try { anchorComp = anchorIEntity.GetComponent() as Component2; } catch {}
            }

            dimInfo.AnchorComponent = anchorComp;
            if (anchorComp != null)
            {
                try { dimInfo.AnchorComponentName = anchorComp.Name2; } catch {}
                try { dimInfo.AnchorComponentPath = anchorComp.GetPathName(); } catch {}
            }
            else if (dc != null)
            {
                try { dimInfo.AnchorComponentName = dc.Name; } catch {}
            }

            dimInfo.AnchorOccurrenceKey = GetComponentOccurrenceKey(dimInfo.AnchorComponent);

            // Populate Diagnostic Route A / B for Comparison & Logging ONLY
            ExtractedEdgeInfo legacyAnchorInfo = RepairDimGeometry.ExtractEdgeGeometry(swApp, dimInfo.AnchorEntity, anchorComp, view, displayEndpoints, false);
            if (legacyAnchorInfo != null)
            {
                dimInfo.AnchorRawStartPt = legacyAnchorInfo.RawStartPt;
                dimInfo.AnchorRawEndPt = legacyAnchorInfo.RawEndPt;
                dimInfo.AnchorAssemblyStartPt = legacyAnchorInfo.AssemblyStartPt;
                dimInfo.AnchorAssemblyEndPt = legacyAnchorInfo.AssemblyEndPt;
                dimInfo.AnchorDrawingStartPt = legacyAnchorInfo.StartSheetPt;
                dimInfo.AnchorDrawingEndPt = legacyAnchorInfo.EndSheetPt;
                dimInfo.AnchorDirectViewStartPt = legacyAnchorInfo.DirectViewStartPt;
                dimInfo.AnchorDirectViewEndPt = legacyAnchorInfo.DirectViewEndPt;
                dimInfo.AnchorCoordinateMethod = legacyAnchorInfo.CoordinateMethod;
                dimInfo.AnchorOrientation = legacyAnchorInfo.Orientation;

                if (displayEndpoints.Count > 0)
                {
                    double dStartA = RepairDimGeometry.ComputeMinDistanceToPoints(legacyAnchorInfo.StartSheetPt, displayEndpoints);
                    double dEndA = RepairDimGeometry.ComputeMinDistanceToPoints(legacyAnchorInfo.EndSheetPt, displayEndpoints);
                    dimInfo.AnchorDisplayProximityRouteA = Math.Min(dStartA, dEndA);

                    double dStartB = RepairDimGeometry.ComputeMinDistanceToPoints(legacyAnchorInfo.DirectViewStartPt, displayEndpoints);
                    double dEndB = RepairDimGeometry.ComputeMinDistanceToPoints(legacyAnchorInfo.DirectViewEndPt, displayEndpoints);
                    dimInfo.AnchorDisplayProximityRouteB = Math.Min(dStartB, dEndB);
                }
            }

            // STEP 8E: Resolve Live Anchor via Route C Polyline Ground Truth
            dimInfo.AnchorPolylineMatches = RepairDimGeometry.FindAnchorPolylineMatches(dimInfo, viewGeom.Polylines);
            foreach (var match in dimInfo.AnchorPolylineMatches)
            {
                if (displayEndpoints.Count > 0 && match.SheetPoints != null && match.SheetPoints.Count > 0)
                {
                    double minD = double.MaxValue;
                    foreach (var pt in match.SheetPoints)
                    {
                        double d = RepairDimGeometry.ComputeMinDistanceToPoints(pt, displayEndpoints);
                        if (d < minD) minD = d;
                    }
                    match.DisplayProximityMm = minD;
                }
            }

            if (dimInfo.AnchorPolylineMatches.Count == 0)
            {
                dimInfo.CandidateDecision = "NO_CANDIDATE_DIAGNOSTIC";
                dimInfo.DiagnosticNotes.Add("ROUTE_C_ANCHOR_UNRESOLVED");
                return;
            }

            // Sort matches by DisplayProximityMm ascending
            dimInfo.AnchorPolylineMatches.Sort((a, b) => a.DisplayProximityMm.CompareTo(b.DisplayProximityMm));

            DrawingPolylineEdgeInfo anchorPolyline = null;
            if (dimInfo.AnchorPolylineMatches.Count == 1)
            {
                anchorPolyline = dimInfo.AnchorPolylineMatches[0];
            }
            else
            {
                var bestMatch = dimInfo.AnchorPolylineMatches[0];
                var secondMatch = dimInfo.AnchorPolylineMatches[1];

                // Nếu 2 match có proximity gần bằng nhau trong tolerance (0.15mm) -> AMBIGUOUS_ANCHOR_ROUTE_C
                if (Math.Abs(bestMatch.DisplayProximityMm - secondMatch.DisplayProximityMm) <= RepairDimGeometry.AbsoluteDistanceToleranceMm)
                {
                    dimInfo.CandidateDecision = "AMBIGUOUS_ANCHOR_ROUTE_C";
                    dimInfo.DiagnosticNotes.Add($"AMBIGUOUS_ANCHOR_ROUTE_C: Match #1 (Rec #{bestMatch.RawRecordIndex}, Prox={bestMatch.DisplayProximityMm:F2}mm) vs Match #2 (Rec #{secondMatch.RawRecordIndex}, Prox={secondMatch.DisplayProximityMm:F2}mm)");
                    return;
                }
                else
                {
                    anchorPolyline = bestMatch;
                }
            }

            if (anchorPolyline == null || anchorPolyline.SheetStart == null || anchorPolyline.SheetEnd == null)
            {
                dimInfo.CandidateDecision = "NO_CANDIDATE_DIAGNOSTIC";
                dimInfo.DiagnosticNotes.Add("ROUTE_C_ANCHOR_GEOMETRY_INVALID");
                return;
            }

            double[] anchorSheetStart = anchorPolyline.SheetStart;
            double[] anchorSheetEnd = anchorPolyline.SheetEnd;
            string anchorOrientation = anchorPolyline.Orientation;
            dimInfo.AnchorOrientation = anchorOrientation;

            double anchorMidX = (anchorSheetStart[0] + anchorSheetEnd[0]) * 0.5;
            double anchorMidY = (anchorSheetStart[1] + anchorSheetEnd[1]) * 0.5;

            double anchorDx = anchorSheetEnd[0] - anchorSheetStart[0];
            double anchorDy = anchorSheetEnd[1] - anchorSheetStart[1];
            double anchorLenM = Math.Sqrt(anchorDx * anchorDx + anchorDy * anchorDy);
            double anchorUx = (anchorLenM > 1e-7) ? anchorDx / anchorLenM : 1.0;
            double anchorUy = (anchorLenM > 1e-7) ? anchorDy / anchorLenM : 0.0;
            double anchorNx = -anchorUy;
            double anchorNy = anchorUx;

            // Exclude all anchor records & anchor model entities
            HashSet<int> anchorRecordIndices = new HashSet<int>();
            foreach (var m in dimInfo.AnchorPolylineMatches)
            {
                anchorRecordIndices.Add(m.RawRecordIndex);
            }

            // Determine Annotation side relative to anchor line in Sheet Space
            double annSignedOffsetM = 0.0;
            if (dimInfo.Position != null && dimInfo.Position.Length >= 2)
            {
                if (anchorOrientation == "HORIZONTAL")
                {
                    annSignedOffsetM = dimInfo.Position[1] - anchorMidY;
                }
                else if (anchorOrientation == "VERTICAL")
                {
                    annSignedOffsetM = dimInfo.Position[0] - anchorMidX;
                }
                else
                {
                    double annVx = dimInfo.Position[0] - anchorMidX;
                    double annVy = dimInfo.Position[1] - anchorMidY;
                    annSignedOffsetM = annVx * anchorNx + annVy * anchorNy;
                }
            }

            // Candidate Source DUY NHẤT -> viewGeom.RepairLineRecords
            List<RepairCandidate> candidateList = new List<RepairCandidate>();
            double targetToleranceMm = Math.Max(RepairDimGeometry.AbsoluteDistanceToleranceMm, Math.Abs(targetDimensionMm) * RepairDimGeometry.RelativeDistanceTolerance);

            foreach (DrawingPolylineEdgeInfo cand in viewGeom.RepairLineRecords)
            {
                if (cand == null || cand.SheetStart == null || cand.SheetEnd == null)
                    continue;

                // 1. Exclude all anchor records & anchor model entities
                if (anchorRecordIndices.Contains(cand.RawRecordIndex))
                    continue;

                if (object.ReferenceEquals(cand.ModelEntity, dimInfo.AnchorEntity))
                    continue;

                double candMidX = (cand.SheetStart[0] + cand.SheetEnd[0]) * 0.5;
                double candMidY = (cand.SheetStart[1] + cand.SheetEnd[1]) * 0.5;

                double dMidX = Math.Abs(anchorMidX - candMidX);
                double dMidY = Math.Abs(anchorMidY - candMidY);
                if (dMidX < 1e-5 && dMidY < 1e-5)
                    continue;

                // 2. Parallel Orientation Check in Sheet Space
                double candDx = cand.SheetEnd[0] - cand.SheetStart[0];
                double candDy = cand.SheetEnd[1] - cand.SheetStart[1];
                double candLenM = Math.Sqrt(candDx * candDx + candDy * candDy);
                if (candLenM < 1e-7)
                    continue;

                double candUx = candDx / candLenM;
                double candUy = candDy / candLenM;

                bool isParallel = false;
                if (anchorOrientation == "HORIZONTAL" && cand.Orientation == "HORIZONTAL")
                {
                    isParallel = true;
                }
                else if (anchorOrientation == "VERTICAL" && cand.Orientation == "VERTICAL")
                {
                    isParallel = true;
                }
                else
                {
                    double dot = Math.Abs(anchorUx * candUx + anchorUy * candUy);
                    isParallel = (dot >= RepairDimGeometry.ParallelToleranceDot);
                }

                if (!isParallel)
                {
                    continue;
                }

                // 3. Compute Distance in SHEET SPACE
                double sheetDistanceMm = 0.0;
                double signedOffsetSheetM = 0.0;

                if (anchorOrientation == "VERTICAL")
                {
                    sheetDistanceMm = Math.Abs(candMidX - anchorMidX) * 1000.0;
                    signedOffsetSheetM = candMidX - anchorMidX;
                }
                else if (anchorOrientation == "HORIZONTAL")
                {
                    sheetDistanceMm = Math.Abs(candMidY - anchorMidY) * 1000.0;
                    signedOffsetSheetM = candMidY - anchorMidY;
                }
                else
                {
                    double vx = candMidX - anchorMidX;
                    double vy = candMidY - anchorMidY;
                    double perpM = Math.Abs(vx * anchorNx + vy * anchorNy);
                    sheetDistanceMm = perpM * 1000.0;
                    signedOffsetSheetM = vx * anchorNx + vy * anchorNy;
                }

                // 4. Convert Sheet Distance -> Model Distance via ScaleDecimal
                double modelDistanceMm = RepairDimGeometry.SheetDistanceToModelMm(sheetDistanceMm, viewGeom.ScaleDecimal);
                double signedOffsetMm = RepairDimGeometry.SheetDistanceToModelMm(signedOffsetSheetM * 1000.0, viewGeom.ScaleDecimal);

                // 5. HARD DISTANCE GATE
                double distanceErrorMm = Math.Abs(modelDistanceMm - targetDimensionMm);
                if (distanceErrorMm > targetToleranceMm)
                {
                    // HARD GATE: Candidate fails distance match, skip immediately!
                    continue;
                }

                // 6. SAME COMPONENT CHỈ DÙNG EXACT KEY
                string candCompKey = cand.ComponentOccurrenceKey;
                string anchorCompKey = dimInfo.AnchorOccurrenceKey;
                bool sameComponent =
                    !string.IsNullOrEmpty(candCompKey) &&
                    !string.IsNullOrEmpty(anchorCompKey) &&
                    string.Equals(candCompKey, anchorCompKey, StringComparison.OrdinalIgnoreCase);

                // 7. PreferredSide Check in Sheet Coordinates
                bool isPreferredSide = false;
                if (Math.Abs(annSignedOffsetM) < 1e-4 || Math.Sign(annSignedOffsetM) == Math.Sign(signedOffsetSheetM))
                {
                    isPreferredSide = true;
                }

                // 8. STEP 8E-FIX2: DISPLAY WITNESS EVIDENCE
                double witnessProxMm = RepairDimGeometry.ComputeCandidateToDisplayWitnessProximityMm(
                    cand,
                    displayLineSegments,
                    displayEndpoints);

                string witnessCategory = "FAR";
                double witnessScore = 0.0;
                if (witnessProxMm <= 1.5)
                {
                    witnessCategory = "VERY_CLOSE";
                    witnessScore = 20.0;
                }
                else if (witnessProxMm <= 3.0)
                {
                    witnessCategory = "CLOSE";
                    witnessScore = 10.0;
                }
                else
                {
                    witnessCategory = "FAR";
                    witnessScore = 0.0;
                }

                // 9. STEP 8E-FIX2 SCORING TABLE
                double score = 0.0;
                List<string> reasons = new List<string>();

                // Same View (+20)
                score += 20.0;
                reasons.Add("SameView(+20)");

                // Visible (+10)
                score += 10.0;
                reasons.Add("Visible(+10)");

                // Line Geometry (+10)
                score += 10.0;
                reasons.Add("LineGeometry(+10)");

                // Parallel (+15)
                score += 15.0;
                reasons.Add("Parallel(+15)");

                // Distance Match (+30 with accuracy bonus)
                double distScore = 30.0 * (1.0 - 0.5 * (distanceErrorMm / targetToleranceMm));
                score += distScore;
                reasons.Add($"DistanceMatch(+{distScore:F1})");

                // Same Component (+10)
                if (sameComponent)
                {
                    score += 10.0;
                    reasons.Add("SameComponent(+10)");
                }

                // Preferred Side (+20)
                if (isPreferredSide)
                {
                    score += 20.0;
                    reasons.Add("PreferredSide(+20)");
                }
                else
                {
                    reasons.Add("OppositeSide(0)");
                }

                // Display Witness Evidence (+20, +10, or +0)
                if (witnessScore > 0.0)
                {
                    score += witnessScore;
                    reasons.Add($"DisplayWitness_{witnessCategory}(+{witnessScore:F0}, {witnessProxMm:F2}mm)");
                }
                else
                {
                    reasons.Add($"DisplayWitness_FAR(0, {witnessProxMm:F2}mm)");
                }

                double annotDistMm = 0.0;
                if (dimInfo.Position != null && dimInfo.Position.Length >= 2)
                {
                    double dx = (candMidX - dimInfo.Position[0]) * 1000.0;
                    double dy = (candMidY - dimInfo.Position[1]) * 1000.0;
                    annotDistMm = Math.Sqrt(dx * dx + dy * dy);
                }

                RepairCandidate candidate = new RepairCandidate
                {
                    Rank = 0,
                    RawRecordIndex = cand.RawRecordIndex,
                    EntityArrayIndex = cand.EntityArrayIndex,

                    Entity = cand.ModelEntity,
                    EntityType = (int)swViewEntityType_e.swViewEntityType_Edge,
                    EntityTypeName = "Edge",

                    EnumerationComponent = cand.Component,
                    EnumerationComponentName = cand.ComponentName ?? "<none>",
                    EnumerationComponentPath = cand.ComponentPath ?? "<none>",
                    EnumerationOccurrenceKey = cand.ComponentOccurrenceKey ?? "<none>",

                    Component = cand.Component,
                    ComponentName = cand.ComponentName ?? "<none>",
                    ComponentPath = cand.ComponentPath ?? "<none>",
                    ComponentOccurrenceKey = candCompKey,
                    SameComponentAsAnchor = sameComponent,

                    DrawingStartPt = cand.SheetStart,
                    DrawingEndPt = cand.SheetEnd,
                    CoordinateMethod = "ROUTE_C_POLYLINE_SHEET",
                    HasComponentTransform = true,
                    HasViewTransform = true,

                    Score = Math.Round(score, 1),
                    MeasuredSheetDistanceMm = sheetDistanceMm,
                    MeasuredModelDistanceMm = modelDistanceMm,
                    MeasuredDistanceMm = modelDistanceMm, // Compatibility: Model Distance
                    ViewScaleDecimal = viewGeom.ScaleDecimal,
                    TargetDimensionMm = targetDimensionMm,
                    DistanceMatched = true,
                    DistanceErrorMm = distanceErrorMm,
                    SignedOffsetMm = signedOffsetMm,
                    PreferredSide = isPreferredSide,
                    AnnotationDistanceMm = annotDistMm,

                    DisplayWitnessProximityMm = witnessProxMm,
                    DisplayWitnessCategory = witnessCategory,

                    Orientation = cand.Orientation,
                    GeometryType = "LINE",
                    Reason = string.Join("; ", reasons)
                };

                candidateList.Add(candidate);
            }

            // Sort candidates by Score descending, then by DistanceError ascending, then by DisplayWitnessProximity ascending
            candidateList = candidateList
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.DistanceErrorMm)
                .ThenBy(c => c.DisplayWitnessProximityMm)
                .ToList();

            for (int i = 0; i < candidateList.Count; i++)
            {
                candidateList[i].Rank = i + 1;
            }

            dimInfo.Candidates = candidateList;

            // STEP 8E-FIX2 Decision Logic
            if (candidateList.Count == 0)
            {
                dimInfo.CandidateDecision = "NO_CANDIDATE_DIAGNOSTIC";

                if (anchorSheetStart != null && anchorSheetEnd != null)
                {
                    dimInfo.DiagnosticNotes.Add($"Anchor Sheet Start=({anchorSheetStart[0]:F4}, {anchorSheetStart[1]:F4}), End=({anchorSheetEnd[0]:F4}, {anchorSheetEnd[1]:F4}), Orient={anchorOrientation}");
                }
                dimInfo.DiagnosticNotes.Add($"Anchor Occurrence Key={dimInfo.AnchorOccurrenceKey ?? "<none>"}");

                // Diagnostic: check nearest lines in RepairLineRecords
                int nearCount = 0;
                foreach (var cand in viewGeom.RepairLineRecords)
                {
                    if (cand == null || cand.SheetStart == null || cand.SheetEnd == null) continue;
                    double cMidX = (cand.SheetStart[0] + cand.SheetEnd[0]) * 0.5;
                    double cMidY = (cand.SheetStart[1] + cand.SheetEnd[1]) * 0.5;
                    double sDistMm = 0.0;
                    if (anchorOrientation == "VERTICAL") sDistMm = Math.Abs(cMidX - anchorMidX) * 1000.0;
                    else if (anchorOrientation == "HORIZONTAL") sDistMm = Math.Abs(cMidY - anchorMidY) * 1000.0;
                    else
                    {
                        double vx = cMidX - anchorMidX;
                        double vy = cMidY - anchorMidY;
                        sDistMm = Math.Abs(vx * anchorNx + vy * anchorNy) * 1000.0;
                    }
                    double mDistMm = RepairDimGeometry.SheetDistanceToModelMm(sDistMm, viewGeom.ScaleDecimal);
                    if (mDistMm <= 15.0)
                    {
                        nearCount++;
                        if (nearCount <= 5)
                        {
                            dimInfo.DiagnosticNotes.Add($"NearRepairLine #{nearCount} (Rec #{cand.RawRecordIndex}): Comp={cand.ComponentName}, Orient={cand.Orientation}, SheetDist={sDistMm:F4}mm, ModelDist={mDistMm:F2}mm");
                        }
                    }
                }
            }
            else if (candidateList.Count == 1)
            {
                dimInfo.CandidateDecision = "HIGH_CONFIDENCE";
            }
            else
            {
                var best = candidateList[0];
                var second = candidateList[1];

                bool scoreGap = (best.Score - second.Score >= 10.0);
                bool hasStrongGeometricEvidence = best.PreferredSide || (best.DisplayWitnessProximityMm <= 3.0);

                if (scoreGap && hasStrongGeometricEvidence)
                {
                    dimInfo.CandidateDecision = "HIGH_CONFIDENCE";
                }
                else
                {
                    dimInfo.CandidateDecision = "AMBIGUOUS";
                }
            }
        }

        public static bool IsGeometricallyIdentical(DrawingPolylineEdgeInfo e1, DrawingPolylineEdgeInfo e2)
        {
            if (e1 == null || e2 == null) return false;
            if (object.ReferenceEquals(e1.ModelEntity, e2.ModelEntity) && e1.ModelEntity != null) return true;
            if (e1.SheetStart == null || e1.SheetEnd == null || e2.SheetStart == null || e2.SheetEnd == null) return false;

            // Direct start-start, end-end
            double d1 = Math.Sqrt(Math.Pow(e1.SheetStart[0] - e2.SheetStart[0], 2) + Math.Pow(e1.SheetStart[1] - e2.SheetStart[1], 2)) * 1000.0;
            double d2 = Math.Sqrt(Math.Pow(e1.SheetEnd[0] - e2.SheetEnd[0], 2) + Math.Pow(e1.SheetEnd[1] - e2.SheetEnd[1], 2)) * 1000.0;
            if (d1 <= 0.05 && d2 <= 0.05) return true;

            // Reversed start-end, end-start
            double d3 = Math.Sqrt(Math.Pow(e1.SheetStart[0] - e2.SheetEnd[0], 2) + Math.Pow(e1.SheetStart[1] - e2.SheetEnd[1], 2)) * 1000.0;
            double d4 = Math.Sqrt(Math.Pow(e1.SheetEnd[0] - e2.SheetStart[0], 2) + Math.Pow(e1.SheetEnd[1] - e2.SheetStart[1], 2)) * 1000.0;
            if (d3 <= 0.05 && d4 <= 0.05) return true;

            return false;
        }

        public static FullyLostPairDecision FindFullyLostPairCandidate(
            ISldWorks swApp,
            DanglingDimensionInfo dimInfo,
            ViewGeometryInfo viewGeom,
            SolidWorks.Interop.sldworks.View view,
            DisplayDimension dispDim)
        {
            FullyLostPairDecision decision = new FullyLostPairDecision();
            if (dimInfo == null || viewGeom == null || view == null)
            {
                decision.Decision = "NO_CANDIDATE";
                decision.PairUniqueness = "NO_PAIR";
                decision.RecommendedAction = "MANUAL_REVIEW";
                return decision;
            }

            DisplayWitnessProfile profile = RepairDimGeometry.BuildDisplayWitnessProfile(dimInfo.DisplayLineSegments, dimInfo.Position);
            decision.WitnessProfile = profile;

            if (profile == null || !profile.IsValid)
            {
                decision.Decision = "NO_CANDIDATE";
                decision.PairUniqueness = "NO_PAIR";
                decision.RecommendedAction = "MANUAL_REVIEW";
                return decision;
            }

            if (!dimInfo.SystemValue.HasValue || dimInfo.SystemValue.Value <= 1e-7)
            {
                decision.Decision = "NO_CANDIDATE";
                decision.PairUniqueness = "NO_PAIR";
                decision.RecommendedAction = "MANUAL_REVIEW";
                return decision;
            }

            double targetDimensionMm = Math.Abs(dimInfo.SystemValue.Value) * 1000.0;
            double targetToleranceMm = Math.Max(RepairDimGeometry.AbsoluteDistanceToleranceMm, targetDimensionMm * RepairDimGeometry.RelativeDistanceTolerance);

            List<FullyLostSideCandidate> side1List = new List<FullyLostSideCandidate>();
            List<FullyLostSideCandidate> side2List = new List<FullyLostSideCandidate>();

            // Side candidate search using 2D closest point on segment
            foreach (DrawingPolylineEdgeInfo cand in viewGeom.RepairLineRecords)
            {
                if (cand == null || cand.SheetStart == null || cand.SheetEnd == null)
                    continue;

                double cdx = cand.SheetEnd[0] - cand.SheetStart[0];
                double cdy = cand.SheetEnd[1] - cand.SheetStart[1];
                double clen = Math.Sqrt(cdx * cdx + cdy * cdy);
                if (clen < 1e-6) continue;

                double cux = cdx / clen;
                double cuy = cdy / clen;

                double dot = Math.Abs(cux * profile.WitnessDirectionUnitVector[0] + cuy * profile.WitnessDirectionUnitVector[1]);
                bool isParallel = (dot >= 0.985);

                if (!isParallel) continue;

                // Side 1 Closest Point & Ray Consistency
                var res1 = RepairDimGeometry.ClosestPointOnSegment2D(
                    profile.Witness1GeometryPoint[0], profile.Witness1GeometryPoint[1],
                    cand.SheetStart[0], cand.SheetStart[1],
                    cand.SheetEnd[0], cand.SheetEnd[1]);

                if (res1.DistanceMm <= 1.5)
                {
                    double r1_dx = profile.Witness1DimensionPoint[0] - res1.Point[0];
                    double r1_dy = profile.Witness1DimensionPoint[1] - res1.Point[1];
                    double r1_len = Math.Sqrt(r1_dx * r1_dx + r1_dy * r1_dy);
                    double old_r1_dx = profile.Witness1DimensionPoint[0] - profile.Witness1GeometryPoint[0];
                    double old_r1_dy = profile.Witness1DimensionPoint[1] - profile.Witness1GeometryPoint[1];
                    double old_r1_len = Math.Sqrt(old_r1_dx * old_r1_dx + old_r1_dy * old_r1_dy);

                    double angErr1 = 0.0;
                    bool rayConsistent1 = true;
                    if (r1_len > 1e-6 && old_r1_len > 1e-6)
                    {
                        double dotRay = (r1_dx * old_r1_dx + r1_dy * old_r1_dy) / (r1_len * old_r1_len);
                        if (dotRay > 1.0) dotRay = 1.0; else if (dotRay < -1.0) dotRay = -1.0;
                        angErr1 = Math.Acos(dotRay) * 180.0 / Math.PI;
                        if (dotRay < 0.5) rayConsistent1 = false;
                    }

                    double s1Score = 100.0 - (res1.DistanceMm * 20.0) - (angErr1 * 0.5);
                    side1List.Add(new FullyLostSideCandidate
                    {
                        SideIndex = 1,
                        EdgeInfo = cand,
                        RawRecordIndex = cand.RawRecordIndex,
                        EntityArrayIndex = cand.EntityArrayIndex,
                        ComponentName = cand.ComponentName,
                        ComponentOccurrenceKey = cand.ComponentOccurrenceKey,
                        Orientation = cand.Orientation,
                        SheetStart = cand.SheetStart,
                        SheetEnd = cand.SheetEnd,
                        AttachPoint = res1.Point,
                        AttachParamT = res1.ParamT,
                        WitnessProximityMm = res1.DistanceMm,
                        WitnessRayDirection = r1_len > 1e-6 ? new double[] { r1_dx / r1_len, r1_dy / r1_len } : null,
                        WitnessRayAngularErrorDeg = angErr1,
                        WitnessRayConsistency = rayConsistent1,
                        Score = s1Score
                    });
                }

                // Side 2 Closest Point & Ray Consistency
                var res2 = RepairDimGeometry.ClosestPointOnSegment2D(
                    profile.Witness2GeometryPoint[0], profile.Witness2GeometryPoint[1],
                    cand.SheetStart[0], cand.SheetStart[1],
                    cand.SheetEnd[0], cand.SheetEnd[1]);

                if (res2.DistanceMm <= 1.5)
                {
                    double r2_dx = profile.Witness2DimensionPoint[0] - res2.Point[0];
                    double r2_dy = profile.Witness2DimensionPoint[1] - res2.Point[1];
                    double r2_len = Math.Sqrt(r2_dx * r2_dx + r2_dy * r2_dy);
                    double old_r2_dx = profile.Witness2DimensionPoint[0] - profile.Witness2GeometryPoint[0];
                    double old_r2_dy = profile.Witness2DimensionPoint[1] - profile.Witness2GeometryPoint[1];
                    double old_r2_len = Math.Sqrt(old_r2_dx * old_r2_dx + old_r2_dy * old_r2_dy);

                    double angErr2 = 0.0;
                    bool rayConsistent2 = true;
                    if (r2_len > 1e-6 && old_r2_len > 1e-6)
                    {
                        double dotRay = (r2_dx * old_r2_dx + r2_dy * old_r2_dy) / (r2_len * old_r2_len);
                        if (dotRay > 1.0) dotRay = 1.0; else if (dotRay < -1.0) dotRay = -1.0;
                        angErr2 = Math.Acos(dotRay) * 180.0 / Math.PI;
                        if (dotRay < 0.5) rayConsistent2 = false;
                    }

                    double s2Score = 100.0 - (res2.DistanceMm * 20.0) - (angErr2 * 0.5);
                    side2List.Add(new FullyLostSideCandidate
                    {
                        SideIndex = 2,
                        EdgeInfo = cand,
                        RawRecordIndex = cand.RawRecordIndex,
                        EntityArrayIndex = cand.EntityArrayIndex,
                        ComponentName = cand.ComponentName,
                        ComponentOccurrenceKey = cand.ComponentOccurrenceKey,
                        Orientation = cand.Orientation,
                        SheetStart = cand.SheetStart,
                        SheetEnd = cand.SheetEnd,
                        AttachPoint = res2.Point,
                        AttachParamT = res2.ParamT,
                        WitnessProximityMm = res2.DistanceMm,
                        WitnessRayDirection = r2_len > 1e-6 ? new double[] { r2_dx / r2_len, r2_dy / r2_len } : null,
                        WitnessRayAngularErrorDeg = angErr2,
                        WitnessRayConsistency = rayConsistent2,
                        Score = s2Score
                    });
                }
            }

            decision.Side1Candidates = side1List;
            decision.Side2Candidates = side2List;

            BrokenViewInfo brokenInfo = RepairDimGeometry.ExtractBrokenViewInfo(view);
            decision.BrokenViewInfo = brokenInfo;

            List<FullyLostPairCandidate> rawValidPairs = new List<FullyLostPairCandidate>();

            // Evaluate all Pair combinations with exhaustive diagnostics
            foreach (var s1 in side1List)
            {
                foreach (var s2 in side2List)
                {
                    double sx = s2.AttachPoint[0] - s1.AttachPoint[0];
                    double sy = s2.AttachPoint[1] - s1.AttachPoint[1];

                    // Generic 2D projection onto measurement axis vector using attach points
                    double sheetDistM = Math.Abs(sx * profile.DimensionAxisUnitVector[0] + sy * profile.DimensionAxisUnitVector[1]);
                    double sheetDistMm = sheetDistM * 1000.0;

                    double perpResidualM = Math.Abs(sx * profile.WitnessDirectionUnitVector[0] + sy * profile.WitnessDirectionUnitVector[1]);
                    double perpResidualMm = perpResidualM * 1000.0;

                    double modelDistMm = RepairDimGeometry.SheetDistanceToModelMm(sheetDistMm, viewGeom.ScaleDecimal);
                    double distErrorMm = Math.Abs(modelDistMm - targetDimensionMm);

                    int crossingCount = 0;
                    bool crossesBreak = RepairDimGeometry.CheckIfDimensionCrossesBreak(
                        profile,
                        brokenInfo,
                        s1.AttachPoint,
                        s2.AttachPoint,
                        modelDistMm,
                        targetDimensionMm,
                        out crossingCount);

                    DistanceVerificationMode distMode = DistanceVerificationMode.NORMAL_SHEET_SCALE;
                    if (brokenInfo.IsBroken)
                    {
                        distMode = crossesBreak ? DistanceVerificationMode.BROKEN_VIEW_CROSS_BREAK : DistanceVerificationMode.BROKEN_VIEW_LOCAL;
                    }

                    EvaluatedPairDiagnostics diag = new EvaluatedPairDiagnostics
                    {
                        Side1RawIndex = s1.RawRecordIndex,
                        Side2RawIndex = s2.RawRecordIndex,
                        Side1ComponentName = s1.ComponentName,
                        Side2ComponentName = s2.ComponentName,
                        AttachPoint1 = s1.AttachPoint,
                        AttachPoint2 = s2.AttachPoint,
                        SheetSeparationMm = sheetDistMm,
                        ModelDistanceMm = modelDistMm,
                        TargetDistanceMm = targetDimensionMm,
                        DistanceErrorMm = distErrorMm,
                        DistanceToleranceMm = targetToleranceMm,
                        PerpendicularResidualMm = perpResidualMm,
                        WitnessError1Mm = s1.WitnessProximityMm,
                        WitnessError2Mm = s2.WitnessProximityMm,
                        TotalWitnessErrorMm = s1.WitnessProximityMm + s2.WitnessProximityMm,
                        MaxWitnessErrorMm = Math.Max(s1.WitnessProximityMm, s2.WitnessProximityMm),
                        RayAngularError1Deg = s1.WitnessRayAngularErrorDeg,
                        RayAngularError2Deg = s2.WitnessRayAngularErrorDeg,
                        DistanceMode = distMode,
                        CrossesActiveBreak = crossesBreak,
                        BreakCrossingCount = crossingCount
                    };

                    if (s1.RawRecordIndex == s2.RawRecordIndex ||
                        object.ReferenceEquals(s1.EdgeInfo.ModelEntity, s2.EdgeInfo.ModelEntity) ||
                        IsGeometricallyIdentical(s1.EdgeInfo, s2.EdgeInfo))
                    {
                        diag.RejectionReasons.Add("SAME_PHYSICAL_EDGE");
                    }

                    if (s1.EdgeInfo.ModelEntity == null || s2.EdgeInfo.ModelEntity == null)
                    {
                        diag.RejectionReasons.Add("INVALID_ENTITY");
                    }

                    diag.AxisMatched = true;

                    if (distMode == DistanceVerificationMode.BROKEN_VIEW_CROSS_BREAK)
                    {
                        diag.PreCreateDistanceComparable = false;
                        diag.PreCreateDistanceReason = "DISPLAY_COMPRESSED_BY_ACTIVE_BREAK";
                        diag.DistanceMatched = true; // Deferred to post-create true SolidWorks measurement
                    }
                    else
                    {
                        diag.PreCreateDistanceComparable = true;
                        diag.DistanceMatched = (distErrorMm <= targetToleranceMm);
                        if (!diag.DistanceMatched)
                        {
                            diag.RejectionReasons.Add("DISTANCE_MISMATCH");
                        }
                    }

                    if (!s1.WitnessRayConsistency || !s2.WitnessRayConsistency)
                    {
                        diag.RejectionReasons.Add("RAY_DIRECTION_INVERTED");
                    }

                    if (perpResidualMm > 10.0)
                    {
                        diag.RejectionReasons.Add("PERP_RESIDUAL_TOO_LARGE");
                    }

                    diag.IsAccepted = (diag.RejectionReasons.Count == 0);

                    double pairScore = s1.Score + s2.Score + 40.0 + (diag.DistanceMatched ? 40.0 : 0.0) + (distErrorMm <= 0.05 ? 10.0 : 0.0) - (diag.TotalWitnessErrorMm * 10.0) - (diag.RayAngularError1Deg + diag.RayAngularError2Deg) * 0.5;
                    diag.PairScore = pairScore;

                    decision.EvaluatedCombinations.Add(diag);

                    if (diag.IsAccepted)
                    {
                        rawValidPairs.Add(new FullyLostPairCandidate
                        {
                            Side1 = s1,
                            Side2 = s2,
                            AttachPoint1 = s1.AttachPoint,
                            AttachPoint2 = s2.AttachPoint,
                            MeasuredSheetDistanceMm = sheetDistMm,
                            MeasuredModelDistanceMm = modelDistMm,
                            TargetDimensionMm = targetDimensionMm,
                            DistanceErrorMm = distErrorMm,
                            PerpendicularResidualMm = perpResidualMm,
                            WitnessError1Mm = s1.WitnessProximityMm,
                            WitnessError2Mm = s2.WitnessProximityMm,
                            TotalWitnessErrorMm = diag.TotalWitnessErrorMm,
                            MaxWitnessErrorMm = diag.MaxWitnessErrorMm,
                            RayAngularError1Deg = s1.WitnessRayAngularErrorDeg,
                            RayAngularError2Deg = s2.WitnessRayAngularErrorDeg,
                            DistanceMode = distMode,
                            CrossesActiveBreak = crossesBreak,
                            BreakCrossingCount = crossingCount,
                            PreCreateDistanceComparable = diag.PreCreateDistanceComparable,
                            PreCreateDistanceReason = diag.PreCreateDistanceReason,
                            DistanceMatched = diag.DistanceMatched,
                            MeasurementAxisMatched = true,
                            SideAssignment = "DIRECT",
                            TotalWitnessProximityMm = diag.TotalWitnessErrorMm,
                            PairScore = pairScore,
                            Reason = (distMode == DistanceVerificationMode.BROKEN_VIEW_CROSS_BREAK)
                                ? $"CrossBreak (NaiveSheet={sheetDistMm:F2}mm), Prox1={s1.WitnessProximityMm:F2}mm, Prox2={s2.WitnessProximityMm:F2}mm"
                                : $"ModelDist={modelDistMm:F4}mm (Err={distErrorMm:F4}mm), Prox1={s1.WitnessProximityMm:F2}mm, Prox2={s2.WitnessProximityMm:F2}mm"
                        });
                    }
                }
            }

            decision.RawPairCount = rawValidPairs.Count;

            // Physical Pair Deduplication
            List<FullyLostPairCandidate> uniquePairs = new List<FullyLostPairCandidate>();
            foreach (var p in rawValidPairs)
            {
                bool isDup = false;
                for (int ui = 0; ui < uniquePairs.Count; ui++)
                {
                    var existing = uniquePairs[ui];
                    bool matchDirect = (IsGeometricallyIdentical(existing.Side1.EdgeInfo, p.Side1.EdgeInfo) && IsGeometricallyIdentical(existing.Side2.EdgeInfo, p.Side2.EdgeInfo));
                    bool matchSwapped = (IsGeometricallyIdentical(existing.Side1.EdgeInfo, p.Side2.EdgeInfo) && IsGeometricallyIdentical(existing.Side2.EdgeInfo, p.Side1.EdgeInfo));

                    if (matchDirect || matchSwapped)
                    {
                        isDup = true;
                        string reason = (object.ReferenceEquals(existing.Side1.EdgeInfo.ModelEntity, p.Side1.EdgeInfo.ModelEntity) || object.ReferenceEquals(existing.Side2.EdgeInfo.ModelEntity, p.Side2.EdgeInfo.ModelEntity)) ? "SAME_DRAWING_ENTITY" : "GEOMETRICALLY_IDENTICAL_SEGMENTS";
                        decision.DuplicatePairLogs.Add($"Pair (Rec #{p.Side1.RawRecordIndex}+#{p.Side2.RawRecordIndex}) is duplicate of (Rec #{existing.Side1.RawRecordIndex}+#{existing.Side2.RawRecordIndex}) [Reason: {reason}]");

                        if (p.TotalWitnessErrorMm < existing.TotalWitnessErrorMm)
                        {
                            uniquePairs[ui] = p; // Replace with lower error representative
                        }
                        break;
                    }
                }

                if (!isDup)
                {
                    uniquePairs.Add(p);
                }
            }

            decision.PhysicalUniquePairCount = uniquePairs.Count;

            // Continuous Geometric Ranking
            uniquePairs.Sort((a, b) =>
            {
                int cmp = a.MaxWitnessErrorMm.CompareTo(b.MaxWitnessErrorMm);
                if (cmp != 0) return cmp;
                cmp = a.TotalWitnessErrorMm.CompareTo(b.TotalWitnessErrorMm);
                if (cmp != 0) return cmp;
                cmp = (a.RayAngularError1Deg + a.RayAngularError2Deg).CompareTo(b.RayAngularError1Deg + b.RayAngularError2Deg);
                if (cmp != 0) return cmp;
                cmp = a.PerpendicularResidualMm.CompareTo(b.PerpendicularResidualMm);
                if (cmp != 0) return cmp;
                return b.PairScore.CompareTo(a.PairScore);
            });

            for (int r = 0; r < uniquePairs.Count; r++)
            {
                uniquePairs[r].Rank = r + 1;
            }

            decision.PairCandidates = uniquePairs;

            if (uniquePairs.Count == 0)
            {
                decision.Decision = "NO_CANDIDATE";
                decision.PairUniqueness = "NO_PAIR";
                decision.RecommendedAction = "MANUAL_REVIEW";
            }
            else if (uniquePairs.Count == 1)
            {
                decision.BestPair = uniquePairs[0];
                decision.PairUniqueness = (decision.RawPairCount > 1) ? "PHYSICALLY_DEDUPLICATED_UNIQUE" : "UNIQUE_PAIR";
                decision.DistanceMode = uniquePairs[0].DistanceMode;
                decision.CrossesActiveBreak = uniquePairs[0].CrossesActiveBreak;
                decision.BreakCrossingCount = uniquePairs[0].BreakCrossingCount;

                if (uniquePairs[0].DistanceMode == DistanceVerificationMode.BROKEN_VIEW_CROSS_BREAK)
                {
                    decision.Decision = "BROKEN_VIEW_PROVISIONAL_HIGH_CONFIDENCE";
                    decision.PreCreateDecision = "BROKEN_VIEW_PROVISIONAL_HIGH_CONFIDENCE";
                    decision.RecommendedAction = "PROVISIONAL_CREATE_AND_VERIFY";
                }
                else
                {
                    decision.Decision = "FULLY_LOST_HIGH_CONFIDENCE";
                    decision.RecommendedAction = "RECREATE_DIMENSION_REQUIRED";
                }
            }
            else
            {
                decision.BestPair = uniquePairs[0];
                decision.SecondPair = uniquePairs[1];
                decision.ScoreGap = decision.BestPair.PairScore - decision.SecondPair.PairScore;
                decision.WitnessErrorGap = decision.SecondPair.TotalWitnessErrorMm - decision.BestPair.TotalWitnessErrorMm;
                decision.PairUniqueness = "COMPETITIVE_PAIR";
                decision.DistanceMode = decision.BestPair.DistanceMode;
                decision.CrossesActiveBreak = decision.BestPair.CrossesActiveBreak;
                decision.BreakCrossingCount = decision.BestPair.BreakCrossingCount;

                if (decision.WitnessErrorGap >= 0.2 || decision.ScoreGap >= 20.0)
                {
                    if (decision.BestPair.DistanceMode == DistanceVerificationMode.BROKEN_VIEW_CROSS_BREAK)
                    {
                        decision.Decision = "BROKEN_VIEW_PROVISIONAL_HIGH_CONFIDENCE";
                        decision.PreCreateDecision = "BROKEN_VIEW_PROVISIONAL_HIGH_CONFIDENCE";
                        decision.RecommendedAction = "PROVISIONAL_CREATE_AND_VERIFY";
                    }
                    else
                    {
                        decision.Decision = "FULLY_LOST_HIGH_CONFIDENCE";
                        decision.RecommendedAction = "RECREATE_DIMENSION_REQUIRED";
                    }
                }
                else
                {
                    decision.Decision = "FULLY_LOST_AMBIGUOUS";
                    decision.AmbiguityReason = (Math.Abs(decision.WitnessErrorGap) < 1e-4) ? "IDENTICAL_GEOMETRIC_SCORE" : "WITNESS_ERROR_GAP_TOO_SMALL";
                    decision.RecommendedAction = "MANUAL_REVIEW";
                }
            }

            return decision;
        }

        public static void AnalyzePointAnchorCandidates(
            ISldWorks swApp,
            DanglingDimensionInfo dimInfo,
            ViewGeometryInfo viewGeom,
            SolidWorks.Interop.sldworks.View view,
            DisplayDimension dispDim)
        {
            if (dimInfo == null) return;

            PointAnchorProbeDecision probeDecision = DiscoverPointAnchorProbeCandidates(swApp, dimInfo, viewGeom, view, dispDim);
            dimInfo.PointProbeDecision = probeDecision;
            dimInfo.CandidateDecision = probeDecision.Decision;

            if (probeDecision.Decision == "POINT_ANCHOR_PROBE_CANDIDATES_AVAILABLE")
            {
                dimInfo.RouteCCandidateAvailable = true;
                dimInfo.RequiresDimensionRecreate = true;
                dimInfo.RecommendedAction = "EXECUTE_PROVISIONAL_PROBE";
            }
            else
            {
                dimInfo.RouteCCandidateAvailable = false;
                dimInfo.RequiresDimensionRecreate = false;
                dimInfo.RecommendedAction = "MANUAL_REVIEW";
            }
        }

        public static PointAnchorProbeDecision DiscoverPointAnchorProbeCandidates(
            ISldWorks swApp,
            DanglingDimensionInfo info,
            ViewGeometryInfo viewGeom,
            SolidWorks.Interop.sldworks.View view,
            DisplayDimension dispDim)
        {
            PointAnchorProbeDecision decision = new PointAnchorProbeDecision();

            if (info == null || viewGeom == null || view == null || dispDim == null)
            {
                decision.Decision = "POINT_ANCHOR_OBJECT_INVALID";
                return decision;
            }

            // 1. Build Witness Profile from old DisplayData
            DisplayWitnessProfile profile = (info.DisplayLineSegments != null && info.DisplayLineSegments.Count > 0)
                ? RepairDimGeometry.BuildDisplayWitnessProfile(info.DisplayLineSegments, info.Position)
                : null;

            decision.WitnessProfile = profile;

            if (profile == null || !profile.IsValid)
            {
                decision.Decision = "POINT_ANCHOR_PROFILE_INVALID";
                decision.AmbiguityReason = profile != null ? profile.ErrorReason : "NO_DISPLAY_DATA";
                return decision;
            }

            // 2. Discover candidates around BOTH historical witness origins W1 and W2
            List<PointAnchorProbeCandidate> discovered = new List<PointAnchorProbeCandidate>();

            foreach (DrawingPolylineEdgeInfo cand in viewGeom.RepairLineRecords)
            {
                if (cand == null || cand.SheetStart == null || cand.SheetEnd == null || cand.ModelEntity == null)
                    continue;

                // Candidate orientation check: approx parallel to witness direction
                double candDx = cand.SheetEnd[0] - cand.SheetStart[0];
                double candDy = cand.SheetEnd[1] - cand.SheetStart[1];
                double candLenM = Math.Sqrt(candDx * candDx + candDy * candDy);
                if (candLenM < 1e-7) continue;

                double candUx = candDx / candLenM;
                double candUy = candDy / candLenM;

                double dotWit = Math.Abs(candUx * profile.WitnessDirectionUnitVector[0] + candUy * profile.WitnessDirectionUnitVector[1]);
                if (dotWit < 0.85) // Not parallel to witness lines
                    continue;

                // Check proximity to W1 and W2
                var res1 = RepairDimGeometry.ClosestPointOnSegment2D(
                    profile.Witness1GeometryPoint[0], profile.Witness1GeometryPoint[1],
                    cand.SheetStart[0], cand.SheetStart[1],
                    cand.SheetEnd[0], cand.SheetEnd[1]);

                var res2 = RepairDimGeometry.ClosestPointOnSegment2D(
                    profile.Witness2GeometryPoint[0], profile.Witness2GeometryPoint[1],
                    cand.SheetStart[0], cand.SheetStart[1],
                    cand.SheetEnd[0], cand.SheetEnd[1]);

                double d1 = res1.DistanceMm;
                double d2 = res2.DistanceMm;

                if (d1 > 2.0 && d2 > 2.0)
                    continue;

                int closerSide = (d1 <= d2) ? 1 : 2;
                double minProx = (closerSide == 1) ? d1 : d2;
                var closerRes = (closerSide == 1) ? res1 : res2;
                double[] targetDimPt = (closerSide == 1) ? profile.Witness1DimensionPoint : profile.Witness2DimensionPoint;
                double[] targetGeomPt = (closerSide == 1) ? profile.Witness1GeometryPoint : profile.Witness2GeometryPoint;

                // Witness Ray Consistency check
                double r_dx = targetDimPt[0] - closerRes.Point[0];
                double r_dy = targetDimPt[1] - closerRes.Point[1];
                double r_len = Math.Sqrt(r_dx * r_dx + r_dy * r_dy);

                double old_r_dx = targetDimPt[0] - targetGeomPt[0];
                double old_r_dy = targetDimPt[1] - targetGeomPt[1];
                double old_r_len = Math.Sqrt(old_r_dx * old_r_dx + old_r_dy * old_r_dy);

                double angErr = 0.0;
                bool rayConsistent = true;
                if (r_len > 1e-6 && old_r_len > 1e-6)
                {
                    double dotRay = (r_dx * old_r_dx + r_dy * old_r_dy) / (r_len * old_r_len);
                    if (dotRay > 1.0) dotRay = 1.0; else if (dotRay < -1.0) dotRay = -1.0;
                    angErr = Math.Acos(dotRay) * 180.0 / Math.PI;
                    if (dotRay < 0.5) rayConsistent = false;
                }

                if (!rayConsistent)
                    continue;

                discovered.Add(new PointAnchorProbeCandidate
                {
                    EdgeInfo = cand,
                    RawRecordIndex = cand.RawRecordIndex,
                    ComponentName = cand.ComponentName,
                    SheetStart = cand.SheetStart,
                    SheetEnd = cand.SheetEnd,
                    HistoricalSide = closerSide,
                    W1ProximityMm = d1,
                    W2ProximityMm = d2,
                    MinProximityMm = minProx,
                    RayAngularErrorDeg = angErr,
                    RayConsistent = rayConsistent,
                    AttachPoint = closerRes.Point,
                    AttachParamT = closerRes.ParamT
                });
            }

            decision.DiscoveredCandidates = discovered;

            // 3. Physical Edge Deduplication
            List<PointAnchorProbeCandidate> physical = new List<PointAnchorProbeCandidate>();
            foreach (var c in discovered)
            {
                bool isDup = false;
                for (int pi = 0; pi < physical.Count; pi++)
                {
                    var ex = physical[pi];
                    if (IsGeometricallyIdentical(ex.EdgeInfo, c.EdgeInfo))
                    {
                        isDup = true;
                        decision.DuplicateLogs.Add($"Edge (Rec #{c.RawRecordIndex}) duplicate of (Rec #{ex.RawRecordIndex})");
                        if (c.MinProximityMm < ex.MinProximityMm)
                        {
                            physical[pi] = c;
                        }
                        break;
                    }
                }
                if (!isDup) physical.Add(c);
            }

            physical.Sort((a, b) => a.MinProximityMm.CompareTo(b.MinProximityMm));
            for (int i = 0; i < physical.Count; i++)
            {
                physical[i].CandidateIndex = i + 1;
            }

            decision.PhysicalProbeCandidates = physical;

            if (physical.Count == 0)
            {
                decision.Decision = "POINT_ANCHOR_NO_PROBE_CANDIDATE";
                decision.RecommendedAction = "MANUAL_REVIEW";
            }
            else if (physical.Count > 12)
            {
                decision.Decision = "POINT_ANCHOR_PROBE_SET_TOO_LARGE";
                decision.AmbiguityReason = $"Probe set size {physical.Count} exceeds safety cap 12";
                decision.RecommendedAction = "MANUAL_REVIEW";
            }
            else
            {
                decision.Decision = "POINT_ANCHOR_PROBE_CANDIDATES_AVAILABLE";
                decision.RecommendedAction = "EXECUTE_PROVISIONAL_PROBE";
            }

            return decision;
        }

        public static PointAnchorDecision FindPointAnchorEdgeCandidate(
            ISldWorks swApp,
            DanglingDimensionInfo info,
            ViewGeometryInfo viewGeom,
            SolidWorks.Interop.sldworks.View view,
            DisplayDimension dispDim)
        {
            PointAnchorDecision decision = new PointAnchorDecision();

            if (info == null || viewGeom == null || view == null || dispDim == null)
            {
                decision.Decision = "POINT_ANCHOR_OBJECT_INVALID";
                return decision;
            }

            // 1. Build Witness Profile from old DisplayData
            DisplayWitnessProfile profile = (info.DisplayLineSegments != null && info.DisplayLineSegments.Count > 0)
                ? RepairDimGeometry.BuildDisplayWitnessProfile(info.DisplayLineSegments, info.Position)
                : null;

            decision.WitnessProfile = profile;

            if (profile == null || !profile.IsValid)
            {
                decision.Decision = "POINT_ANCHOR_PROFILE_INVALID";
                decision.AmbiguityReason = profile != null ? profile.ErrorReason : "NO_DISPLAY_DATA";
                return decision;
            }

            // 2. Resolve Live SketchPoint
            SketchPoint sp = info.AnchorEntity as SketchPoint;
            if (sp == null)
            {
                decision.Decision = "POINT_ANCHOR_OBJECT_INVALID";
                return decision;
            }

            PointAnchorInfo ptInfo = RepairDimGeometry.ResolveSketchPointSheetPosition(swApp, view, sp, profile);
            decision.PointInfo = ptInfo;

            if (!ptInfo.IsResolved)
            {
                decision.Decision = ptInfo.ResolutionStatus; // e.g. POINT_ANCHOR_POSITION_UNRESOLVED, POINT_ANCHOR_WITNESS_AMBIGUOUS
                return decision;
            }

            // 3. Extract Broken View Info
            BrokenViewInfo brokenInfo = RepairDimGeometry.ExtractBrokenViewInfo(view);
            decision.BrokenViewInfo = brokenInfo;

            // 4. Identify Missing Witness Origin & Direction
            double[] missingWitnessOrigin = (ptInfo.LivePointWitnessSide == 1) ? profile.Witness2GeometryPoint : profile.Witness1GeometryPoint;
            double[] missingWitnessDimPt = (ptInfo.LivePointWitnessSide == 1) ? profile.Witness2DimensionPoint : profile.Witness1DimensionPoint;
            double targetDimensionMm = info.SystemValue.HasValue ? Math.Abs(info.SystemValue.Value) * 1000.0 : 0.0;
            double targetToleranceMm = Math.Max(RepairDimGeometry.AbsoluteDistanceToleranceMm, Math.Abs(targetDimensionMm) * RepairDimGeometry.RelativeDistanceTolerance);

            List<PointAnchorEdgeCandidate> rawCandidates = new List<PointAnchorEdgeCandidate>();

            // 5. Search Missing Counterpart in viewGeom.RepairLineRecords (Route C)
            foreach (DrawingPolylineEdgeInfo cand in viewGeom.RepairLineRecords)
            {
                if (cand == null || cand.SheetStart == null || cand.SheetEnd == null || cand.ModelEntity == null)
                    continue;

                // Orientation Check: Must be approximately parallel to witness direction
                double candDx = cand.SheetEnd[0] - cand.SheetStart[0];
                double candDy = cand.SheetEnd[1] - cand.SheetStart[1];
                double candLenM = Math.Sqrt(candDx * candDx + candDy * candDy);
                if (candLenM < 1e-7) continue;

                double candUx = candDx / candLenM;
                double candUy = candDy / candLenM;

                double dotWit = Math.Abs(candUx * profile.WitnessDirectionUnitVector[0] + candUy * profile.WitnessDirectionUnitVector[1]);
                if (dotWit < 0.85) // Not parallel to witness lines
                    continue;

                // Attachment Point on Edge closest to missing witness origin
                var res = RepairDimGeometry.ClosestPointOnSegment2D(
                    missingWitnessOrigin[0], missingWitnessOrigin[1],
                    cand.SheetStart[0], cand.SheetStart[1],
                    cand.SheetEnd[0], cand.SheetEnd[1]);

                if (res.DistanceMm > 1.5)
                    continue;

                // Witness Ray Consistency
                double r_dx = missingWitnessDimPt[0] - res.Point[0];
                double r_dy = missingWitnessDimPt[1] - res.Point[1];
                double r_len = Math.Sqrt(r_dx * r_dx + r_dy * r_dy);

                double old_r_dx = missingWitnessDimPt[0] - missingWitnessOrigin[0];
                double old_r_dy = missingWitnessDimPt[1] - missingWitnessOrigin[1];
                double old_r_len = Math.Sqrt(old_r_dx * old_r_dx + old_r_dy * old_r_dy);

                double angErr = 0.0;
                bool rayConsistent = true;
                if (r_len > 1e-6 && old_r_len > 1e-6)
                {
                    double dotRay = (r_dx * old_r_dx + r_dy * old_r_dy) / (r_len * old_r_len);
                    if (dotRay > 1.0) dotRay = 1.0; else if (dotRay < -1.0) dotRay = -1.0;
                    angErr = Math.Acos(dotRay) * 180.0 / Math.PI;
                    if (dotRay < 0.5) rayConsistent = false;
                }

                if (!rayConsistent)
                    continue;

                // Point-to-Edge Separation
                double sx = res.Point[0] - ptInfo.ResolvedSheetXY[0];
                double sy = res.Point[1] - ptInfo.ResolvedSheetXY[1];

                double sheetDistM = Math.Abs(sx * profile.DimensionAxisUnitVector[0] + sy * profile.DimensionAxisUnitVector[1]);
                double sheetDistMm = sheetDistM * 1000.0;

                double perpResidualM = Math.Abs(sx * profile.WitnessDirectionUnitVector[0] + sy * profile.WitnessDirectionUnitVector[1]);
                double perpResidualMm = perpResidualM * 1000.0;

                if (perpResidualMm > 10.0)
                    continue;

                double modelDistMm = RepairDimGeometry.SheetDistanceToModelMm(sheetDistMm, viewGeom.ScaleDecimal);
                double distErrorMm = Math.Abs(modelDistMm - targetDimensionMm);

                int crossingCount = 0;
                bool crossesBreak = RepairDimGeometry.CheckIfDimensionCrossesBreak(
                    profile,
                    brokenInfo,
                    ptInfo.ResolvedSheetXY,
                    res.Point,
                    modelDistMm,
                    targetDimensionMm,
                    out crossingCount);

                DistanceVerificationMode distMode = DistanceVerificationMode.NORMAL_SHEET_SCALE;
                if (brokenInfo != null && brokenInfo.IsBroken)
                {
                    distMode = crossesBreak ? DistanceVerificationMode.BROKEN_VIEW_CROSS_BREAK : DistanceVerificationMode.BROKEN_VIEW_LOCAL;
                }

                bool distMatched = false;
                bool preCreateComparable = true;
                string preCreateReason = "";

                if (distMode == DistanceVerificationMode.BROKEN_VIEW_CROSS_BREAK)
                {
                    preCreateComparable = false;
                    preCreateReason = "DISPLAY_COMPRESSED_BY_ACTIVE_BREAK";
                    distMatched = true; // Verified post-create via AddDimension2!
                }
                else
                {
                    preCreateComparable = true;
                    distMatched = (distErrorMm <= targetToleranceMm);
                    if (!distMatched)
                        continue;
                }

                double score = 100.0 - (res.DistanceMm * 20.0) - (angErr * 0.5) - (perpResidualMm * 2.0) + (distMatched ? 40.0 : 0.0) + (distErrorMm <= 0.05 ? 10.0 : 0.0);

                rawCandidates.Add(new PointAnchorEdgeCandidate
                {
                    EdgeInfo = cand,
                    RawRecordIndex = cand.RawRecordIndex,
                    EntityArrayIndex = cand.EntityArrayIndex,
                    ComponentName = cand.ComponentName,
                    Orientation = cand.Orientation,
                    SheetStart = cand.SheetStart,
                    SheetEnd = cand.SheetEnd,
                    AttachPoint = res.Point,
                    AttachParamT = res.ParamT,
                    WitnessProximityMm = res.DistanceMm,
                    RayAngularErrorDeg = angErr,
                    WitnessRayConsistency = rayConsistent,
                    ProjectedSheetDistanceMm = sheetDistMm,
                    ModelDistanceMm = modelDistMm,
                    TargetDistanceMm = targetDimensionMm,
                    DistanceErrorMm = distErrorMm,
                    PerpendicularResidualMm = perpResidualMm,
                    DistanceMode = distMode,
                    CrossesActiveBreak = crossesBreak,
                    BreakCrossingCount = crossingCount,
                    PreCreateDistanceComparable = preCreateComparable,
                    PreCreateDistanceReason = preCreateReason,
                    DistanceMatched = distMatched,
                    Score = score,
                    Reason = (distMode == DistanceVerificationMode.BROKEN_VIEW_CROSS_BREAK)
                        ? $"CrossBreak (NaiveSheet={sheetDistMm:F2}mm), Prox={res.DistanceMm:F2}mm, PerpRes={perpResidualMm:F2}mm"
                        : $"ModelDist={modelDistMm:F4}mm (Err={distErrorMm:F4}mm), Prox={res.DistanceMm:F2}mm, PerpRes={perpResidualMm:F2}mm"
                });
            }

            // 6. Deduplicate and Rank
            List<PointAnchorEdgeCandidate> uniqueCandidates = new List<PointAnchorEdgeCandidate>();
            foreach (var c in rawCandidates)
            {
                bool isDup = false;
                for (int ui = 0; ui < uniqueCandidates.Count; ui++)
                {
                    var ex = uniqueCandidates[ui];
                    if (IsGeometricallyIdentical(ex.EdgeInfo, c.EdgeInfo))
                    {
                        isDup = true;
                        decision.DuplicateLogs.Add($"Edge (Rec #{c.RawRecordIndex}) duplicate of (Rec #{ex.RawRecordIndex})");
                        if (c.WitnessProximityMm < ex.WitnessProximityMm)
                        {
                            uniqueCandidates[ui] = c;
                        }
                        break;
                    }
                }
                if (!isDup) uniqueCandidates.Add(c);
            }

            uniqueCandidates.Sort((a, b) =>
            {
                int cmp = a.WitnessProximityMm.CompareTo(b.WitnessProximityMm);
                if (cmp != 0) return cmp;
                cmp = a.RayAngularErrorDeg.CompareTo(b.RayAngularErrorDeg);
                if (cmp != 0) return cmp;
                cmp = a.PerpendicularResidualMm.CompareTo(b.PerpendicularResidualMm);
                if (cmp != 0) return cmp;
                return b.Score.CompareTo(a.Score);
            });

            for (int r = 0; r < uniqueCandidates.Count; r++)
            {
                uniqueCandidates[r].Rank = r + 1;
            }

            decision.EdgeCandidates = uniqueCandidates;

            if (uniqueCandidates.Count == 0)
            {
                decision.Decision = "POINT_ANCHOR_NO_EDGE_CANDIDATE";
                decision.RecommendedAction = "MANUAL_REVIEW";
            }
            else if (uniqueCandidates.Count == 1)
            {
                decision.BestEdge = uniqueCandidates[0];
                decision.DistanceMode = uniqueCandidates[0].DistanceMode;
                decision.CrossesActiveBreak = uniqueCandidates[0].CrossesActiveBreak;
                decision.BreakCrossingCount = uniqueCandidates[0].BreakCrossingCount;

                if (uniqueCandidates[0].DistanceMode == DistanceVerificationMode.BROKEN_VIEW_CROSS_BREAK)
                {
                    decision.Decision = "POINT_ANCHOR_PROVISIONAL_HIGH_CONFIDENCE";
                    decision.RecommendedAction = "PROVISIONAL_CREATE_AND_VERIFY";
                }
                else
                {
                    decision.Decision = "POINT_ANCHOR_HIGH_CONFIDENCE";
                    decision.RecommendedAction = "RECREATE_POINT_EDGE_DIMENSION_REQUIRED";
                }
            }
            else
            {
                decision.BestEdge = uniqueCandidates[0];
                decision.SecondEdge = uniqueCandidates[1];
                decision.ScoreGap = decision.BestEdge.Score - decision.SecondEdge.Score;
                decision.WitnessErrorGap = decision.SecondEdge.WitnessProximityMm - decision.BestEdge.WitnessProximityMm;
                decision.DistanceMode = decision.BestEdge.DistanceMode;
                decision.CrossesActiveBreak = decision.BestEdge.CrossesActiveBreak;
                decision.BreakCrossingCount = decision.BestEdge.BreakCrossingCount;

                if (decision.WitnessErrorGap >= 0.2 || decision.ScoreGap >= 20.0)
                {
                    if (decision.BestEdge.DistanceMode == DistanceVerificationMode.BROKEN_VIEW_CROSS_BREAK)
                    {
                        decision.Decision = "POINT_ANCHOR_PROVISIONAL_HIGH_CONFIDENCE";
                        decision.RecommendedAction = "PROVISIONAL_CREATE_AND_VERIFY";
                    }
                    else
                    {
                        decision.Decision = "POINT_ANCHOR_HIGH_CONFIDENCE";
                        decision.RecommendedAction = "RECREATE_POINT_EDGE_DIMENSION_REQUIRED";
                    }
                }
                else
                {
                    decision.Decision = "POINT_ANCHOR_EDGE_AMBIGUOUS";
                    decision.AmbiguityReason = (Math.Abs(decision.WitnessErrorGap) < 1e-4) ? "IDENTICAL_GEOMETRIC_SCORE" : "WITNESS_ERROR_GAP_TOO_SMALL";
                    decision.RecommendedAction = "MANUAL_REVIEW";
                }
            }

            return decision;
        }
    }
}
