using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public static class RepairDimGeometry
    {
        public const double AbsoluteDistanceToleranceMm = 0.15;
        public const double RelativeDistanceTolerance = 0.001;
        public const double ParallelToleranceDot = 0.999;

        // Legacy alias for compatibility
        public const double DistanceToleranceMm = AbsoluteDistanceToleranceMm;
        public const double RelativeTolerance = RelativeDistanceTolerance;

        public static double SheetDistanceToModelMm(double sheetDistanceMm, double scaleDecimal)
        {
            if (scaleDecimal <= 0.0 || double.IsNaN(scaleDecimal) || double.IsInfinity(scaleDecimal))
            {
                return sheetDistanceMm;
            }

            return sheetDistanceMm / scaleDecimal;
        }

        public static bool IsDimensionDistanceMatch(
            double actualModelMm,
            double targetModelMm,
            out double errorMm,
            out double toleranceMm)
        {
            errorMm = Math.Abs(actualModelMm - targetModelMm);
            toleranceMm = Math.Max(
                AbsoluteDistanceToleranceMm,
                Math.Abs(targetModelMm) * RelativeDistanceTolerance);

            return errorMm <= toleranceMm;
        }

        public static double[] ViewLocalToSheet(double[] p, double viewX, double viewY, double scale)
        {
            if (p == null || p.Length < 2) return null;
            return new double[]
            {
                viewX + p[0] * scale,
                viewY + p[1] * scale,
                p.Length > 2 ? p[2] * scale : 0.0
            };
        }

        public static double[] TransformPoint(MathUtility mathUtil, double[] point, MathTransform transform)
        {
            if (point == null || point.Length < 3) return null;
            if (transform == null) return new double[] { point[0], point[1], point[2] };

            try
            {
                if (mathUtil != null)
                {
                    MathPoint pt = mathUtil.CreatePoint(new double[] { point[0], point[1], point[2] }) as MathPoint;
                    if (pt != null)
                    {
                        MathPoint res = pt.MultiplyTransform(transform) as MathPoint;
                        if (res != null && res.ArrayData is double[] arr && arr.Length >= 3)
                        {
                            return new double[] { arr[0], arr[1], arr[2] };
                        }
                    }
                }
            }
            catch {}

            // Fallback: manual matrix multiplication
            try
            {
                object dataObj = transform.ArrayData;
                if (dataObj is double[] a && a.Length >= 16)
                {
                    double x = point[0];
                    double y = point[1];
                    double z = point[2];
                    double scale = a[12];
                    if (Math.Abs(scale) < 1e-9) scale = 1.0;

                    double tx = (a[0] * x + a[1] * y + a[2] * z) * scale + a[9];
                    double ty = (a[3] * x + a[4] * y + a[5] * z) * scale + a[10];
                    double tz = (a[6] * x + a[7] * y + a[8] * z) * scale + a[11];

                    return new double[] { tx, ty, tz };
                }
            }
            catch {}

            return new double[] { point[0], point[1], point[2] };
        }

        public static double ComputeMinDistanceToPoints(double[] pt, List<double[]> points)
        {
            if (pt == null || points == null || points.Count == 0) return double.MaxValue;
            double minDist = double.MaxValue;
            for (int i = 0; i < points.Count; i++)
            {
                double[] p = points[i];
                if (p == null || p.Length < 2) continue;
                double dx = (pt[0] - p[0]) * 1000.0;
                double dy = (pt[1] - p[1]) * 1000.0;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d < minDist) minDist = d;
            }
            return minDist;
        }

        public static double PointToSegmentDistance(double px, double py, double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1;
            double dy = y2 - y1;
            double lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-12)
            {
                double ex = px - x1;
                double ey = py - y1;
                return Math.Sqrt(ex * ex + ey * ey);
            }

            double t = ((px - x1) * dx + (py - y1) * dy) / lenSq;
            if (t < 0.0) t = 0.0;
            else if (t > 1.0) t = 1.0;

            double projX = x1 + t * dx;
            double projY = y1 + t * dy;
            double rx = px - projX;
            double ry = py - projY;
            return Math.Sqrt(rx * rx + ry * ry);
        }

        public static bool DoSegmentsIntersect(double x1, double y1, double x2, double y2, double x3, double y3, double x4, double y4)
        {
            double d1 = (x4 - x3) * (y1 - y3) - (y4 - y3) * (x1 - x3);
            double d2 = (x4 - x3) * (y2 - y3) - (y4 - y3) * (x2 - x3);
            double d3 = (x2 - x1) * (y3 - y1) - (y2 - y1) * (x3 - x1);
            double d4 = (x2 - x1) * (y4 - y1) - (y2 - y1) * (x4 - x1);

            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
                ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            {
                return true;
            }
            return false;
        }

        public static double SegmentToSegmentDistance(double x1, double y1, double x2, double y2, double x3, double y3, double x4, double y4)
        {
            if (DoSegmentsIntersect(x1, y1, x2, y2, x3, y3, x4, y4))
            {
                return 0.0;
            }

            double d1 = PointToSegmentDistance(x1, y1, x3, y3, x4, y4);
            double d2 = PointToSegmentDistance(x2, y2, x3, y3, x4, y4);
            double d3 = PointToSegmentDistance(x3, y3, x1, y1, x2, y2);
            double d4 = PointToSegmentDistance(x4, y4, x1, y1, x2, y2);

            return Math.Min(Math.Min(d1, d2), Math.Min(d3, d4));
        }

        // STEP 8E-FIX2: Candidate ↔ Display Witness Geometry Minimum 2D Distance in Sheet Space (mm)
        public static double ComputeCandidateToDisplayWitnessProximityMm(
            DrawingPolylineEdgeInfo cand,
            List<DisplayDimLine> displayLines,
            List<double[]> displayEndpoints)
        {
            if (cand == null || cand.SheetStart == null || cand.SheetEnd == null)
                return double.MaxValue;

            double x1 = cand.SheetStart[0];
            double y1 = cand.SheetStart[1];
            double x2 = cand.SheetEnd[0];
            double y2 = cand.SheetEnd[1];

            double minDistMeters = double.MaxValue;

            if (displayLines != null && displayLines.Count > 0)
            {
                foreach (var dl in displayLines)
                {
                    double d = SegmentToSegmentDistance(x1, y1, x2, y2, dl.StartX, dl.StartY, dl.EndX, dl.EndY);
                    if (d < minDistMeters) minDistMeters = d;
                }
            }

            if (displayEndpoints != null && displayEndpoints.Count > 0)
            {
                foreach (var ep in displayEndpoints)
                {
                    if (ep != null && ep.Length >= 2)
                    {
                        double d = PointToSegmentDistance(ep[0], ep[1], x1, y1, x2, y2);
                        if (d < minDistMeters) minDistMeters = d;
                    }
                }
            }

            return (minDistMeters == double.MaxValue) ? double.MaxValue : minDistMeters * 1000.0;
        }

        public static int ComparePointsLexicographic(double[] a, double[] b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            int len = Math.Min(a.Length, b.Length);
            for (int i = 0; i < len; i++)
            {
                double diff = a[i] - b[i];
                if (Math.Abs(diff) > 1e-6)
                {
                    return diff > 0 ? 1 : -1;
                }
            }
            return 0;
        }

        public static string ComputeEdgeSignature(object entity, string canonicalCompKey)
        {
            if (entity == null) return null;
            string key = canonicalCompKey ?? "<none>";

            double[] pt1 = null;
            double[] pt2 = null;
            string curveType = "OTHER";

            if (entity is IEdge edge)
            {
                try
                {
                    ICurve curve = edge.GetCurve() as ICurve;
                    if (curve != null)
                    {
                        if (curve.IsLine()) curveType = "LINE";
                        else if (curve.IsCircle()) curveType = "CIRCLE";
                        else if (curve.IsEllipse()) curveType = "ELLIPSE";
                        else if (curve.IsBcurve()) curveType = "BCURVE";
                    }
                }
                catch {}

                try
                {
                    CurveParamData cpd = edge.GetCurveParams3();
                    if (cpd != null)
                    {
                        pt1 = cpd.StartPoint as double[];
                        pt2 = cpd.EndPoint as double[];
                        if (cpd.CurveType == 3001) curveType = "LINE";
                        else if (cpd.CurveType == 3002) curveType = "CIRCLE";
                        else if (cpd.CurveType == 3003) curveType = "ELLIPSE";
                    }
                }
                catch {}

                if (pt1 == null || pt2 == null)
                {
                    try
                    {
                        IVertex v1 = edge.GetStartVertex() as IVertex;
                        IVertex v2 = edge.GetEndVertex() as IVertex;
                        if (v1 != null) pt1 = v1.GetPoint() as double[];
                        if (v2 != null) pt2 = v2.GetPoint() as double[];
                    }
                    catch {}
                }
            }
            else if (entity is ISilhouetteEdge silEdge)
            {
                try
                {
                    ICurve silCurve = silEdge.GetCurve() as ICurve;
                    if (silCurve != null)
                    {
                        if (silCurve.IsLine()) curveType = "LINE";
                        else if (silCurve.IsCircle()) curveType = "CIRCLE";
                        else if (silCurve.IsEllipse()) curveType = "ELLIPSE";
                        else if (silCurve.IsBcurve()) curveType = "BCURVE";

                        double startParam, endParam;
                        bool isClosed, isPeriodic;
                        if (silCurve.GetEndParams(out startParam, out endParam, out isClosed, out isPeriodic))
                        {
                            pt1 = silCurve.Evaluate2(startParam, 0) as double[];
                            pt2 = silCurve.Evaluate2(endParam, 0) as double[];
                        }
                    }
                }
                catch {}
            }

            if (pt1 == null || pt2 == null || pt1.Length < 3 || pt2.Length < 3)
            {
                return $"{key}|{curveType}|<no_geom>";
            }

            // Lexicographic order sort for endpoints
            double[] pA = pt1;
            double[] pB = pt2;
            if (ComparePointsLexicographic(pA, pB) > 0)
            {
                pA = pt2;
                pB = pt1;
            }

            return $"{key}|{curveType}|P1=({Math.Round(pA[0], 5):F5},{Math.Round(pA[1], 5):F5},{Math.Round(pA[2], 5):F5})|P2=({Math.Round(pB[0], 5):F5},{Math.Round(pB[1], 5):F5},{Math.Round(pB[2], 5):F5})";
        }

        public static bool TryReadStructuralInteger(
            double raw,
            out int result,
            out double nearest,
            out double error)
        {
            nearest = Math.Round(raw);
            error = Math.Abs(raw - nearest);
            result = (int)nearest;
            return error <= 1e-6;
        }

        // Build Visible Edge Owner Index (for diagnostic comparison)
        public static List<VisibleEdgeOwnerEntry> BuildVisibleEdgeOwnerEntries(
            SolidWorks.Interop.sldworks.View currentView)
        {
            List<VisibleEdgeOwnerEntry> entries = new List<VisibleEdgeOwnerEntry>();
            if (currentView == null) return entries;

            try
            {
                object[] visibleComps = currentView.GetVisibleComponents() as object[];
                if (visibleComps != null)
                {
                    foreach (object compObj in visibleComps)
                    {
                        if (compObj is Component2 comp)
                        {
                            string compName = null;
                            try { compName = comp.Name2; } catch {}
                            string compKey = RepairDimCandidateFinder.GetComponentOccurrenceKey(comp);

                            object[] visibleEdges = currentView.GetVisibleEntities2(
                                comp,
                                (int)swViewEntityType_e.swViewEntityType_Edge) as object[];

                            if (visibleEdges != null)
                            {
                                foreach (object edgeObj in visibleEdges)
                                {
                                    if (edgeObj != null)
                                    {
                                        entries.Add(new VisibleEdgeOwnerEntry
                                        {
                                            Edge = edgeObj,
                                            Component = comp,
                                            CanonicalComponentName = compName,
                                            CanonicalComponentKey = compKey
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch {}

            return entries;
        }

        // STEP 8D-FIX3D / FIX3E: Polyline Component Owner Resolution Pipeline
        public static Component2 ResolvePolylineOwner(
            ISldWorks swApp,
            object modelEntity,
            SolidWorks.Interop.sldworks.View currentView,
            List<VisibleEdgeOwnerEntry> visibleEdgeEntries,
            ViewGeometryInfo viewGeom,
            out string ownerMethod,
            out string failureReason)
        {
            ownerMethod = "NONE";
            failureReason = "NO_ENTITY";
            if (!(modelEntity is IEntity modelEnt))
                return null;

            Component2 comp = null;

            // --------------------------------------------------
            // A. DIRECT MODELING / ASSEMBLY ENTITY COMPONENT
            // --------------------------------------------------
            try
            {
                comp = modelEnt.IGetComponent2();
                if (comp != null)
                {
                    ownerMethod = "IGetComponent2";
                    if (viewGeom != null) viewGeom.OwnerDirectIGetComponent2Count++;
                    failureReason = null;
                    return comp;
                }
            }
            catch {}

            try
            {
                comp = modelEnt.GetComponent() as Component2;
                if (comp != null)
                {
                    ownerMethod = "GetComponent";
                    if (viewGeom != null) viewGeom.OwnerDirectGetComponentCount++;
                    failureReason = null;
                    return comp;
                }
            }
            catch {}

            // --------------------------------------------------
            // B. PRIMARY FALLBACK: MODEL EDGE -> DRAWING VIEW ENTITY -> DRAWING COMPONENT
            // --------------------------------------------------
            if (currentView != null)
            {
                if (viewGeom != null) viewGeom.GetCorrespondingEntityAttemptCount++;
                object drawingObj = null;
                try
                {
                    drawingObj = currentView.GetCorrespondingEntity(modelEntity);
                }
                catch {}

                if (drawingObj != null)
                {
                    if (viewGeom != null) viewGeom.GetCorrespondingEntityNonNullCount++;
                    if (drawingObj is IEntity drawingEnt)
                    {
                        try
                        {
                            DrawingComponent dc = drawingEnt.GetDrawingComponent(currentView);
                            if (dc != null)
                            {
                                if (viewGeom != null) viewGeom.DrawingComponentResolvedCount++;
                                comp = dc.Component;
                                if (comp != null)
                                {
                                    ownerMethod = "VIEW_GET_CORRESPONDING_ENTITY";
                                    if (viewGeom != null) viewGeom.OwnerViewGetCorrespondingEntityCount++;
                                    failureReason = null;
                                    return comp;
                                }
                            }
                            else
                            {
                                if (viewGeom != null) viewGeom.DrawingComponentNullCount++;
                            }
                        }
                        catch {}
                    }
                }
                else
                {
                    if (viewGeom != null) viewGeom.GetCorrespondingEntityNullCount++;
                }
            }

            // --------------------------------------------------
            // C. DIAGNOSTIC FALLBACK: ISldWorks.IsSame
            // --------------------------------------------------
            if (swApp != null && visibleEdgeEntries != null && visibleEdgeEntries.Count > 0)
            {
                HashSet<string> matchedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Component2 matchedComp = null;
                int sameCount = 0;
                int unsupportedCount = 0;

                foreach (var entry in visibleEdgeEntries)
                {
                    if (entry.Edge == null) continue;
                    try
                    {
                        int eq = swApp.IsSame(modelEntity, entry.Edge);
                        if (eq == 1) // swObjectSame
                        {
                            sameCount++;
                            matchedComp = entry.Component;
                            if (!string.IsNullOrEmpty(entry.CanonicalComponentKey))
                            {
                                matchedKeys.Add(entry.CanonicalComponentKey);
                            }
                            else if (!string.IsNullOrEmpty(entry.CanonicalComponentName))
                            {
                                matchedKeys.Add(entry.CanonicalComponentName);
                            }
                        }
                        else if (eq == 2) // swObjectUnsupported
                        {
                            unsupportedCount++;
                        }
                    }
                    catch {}
                }

                if (matchedKeys.Count == 1 && matchedComp != null)
                {
                    ownerMethod = "ISldWorks.IsSame";
                    failureReason = null;
                    return matchedComp;
                }
                else if (matchedKeys.Count > 1)
                {
                    ownerMethod = "NONE";
                    failureReason = $"ISSAME_AMBIGUOUS_OWNER_{matchedKeys.Count}";
                    return null;
                }
                else if (sameCount == 0 && unsupportedCount > 0)
                {
                    ownerMethod = "NONE";
                    failureReason = $"ISSAME_UNSUPPORTED_{unsupportedCount}";
                    return null;
                }
            }

            failureReason = "ALL_METHODS_NULL";
            return null;
        }

        public static List<DrawingPolylineEdgeInfo> EnumerateDrawingPolylines(
            ISldWorks swApp,
            SolidWorks.Interop.sldworks.View currentView,
            ViewGeometryInfo viewGeom,
            string vPrefix = null)
        {
            List<DrawingPolylineEdgeInfo> list = new List<DrawingPolylineEdgeInfo>();
            if (currentView == null) return list;

            viewGeom.AllPolylineRecords.Clear();
            viewGeom.RepairLineRecords.Clear();
            viewGeom.RecordDiagnostics.Clear();
            viewGeom.RawBoundarySamples.Clear();
            viewGeom.CandidateHeaderSamples.Clear();
            viewGeom.PolylineRecordConstructionLog.Clear();
            viewGeom.Type0NonZeroRecordIndices.Clear();
            viewGeom.AuxGeometryBlocks.Clear();

            viewGeom.OwnerDirectIGetComponent2Count = 0;
            viewGeom.OwnerDirectGetComponentCount = 0;
            viewGeom.OwnerViewGetCorrespondingEntityCount = 0;
            viewGeom.DirectOwnerResolvedCount = 0;
            viewGeom.IsSameOwnerResolvedCount = 0;
            viewGeom.OwnerResolvedCount = 0;
            viewGeom.OwnerNullCount = 0;
            viewGeom.OwnerAmbiguousCount = 0;
            viewGeom.IsSameUnsupportedCount = 0;
            viewGeom.GetCorrespondingEntityAttemptCount = 0;
            viewGeom.GetCorrespondingEntityNonNullCount = 0;
            viewGeom.GetCorrespondingEntityNullCount = 0;
            viewGeom.DrawingComponentResolvedCount = 0;
            viewGeom.DrawingComponentNullCount = 0;

            viewGeom.TypeCurveMatchCount = 0;
            viewGeom.TypeCurveMismatchCount = 0;
            viewGeom.FirstMismatchRecordIndex = -1;
            viewGeom.FirstMismatchDescription = null;

            // Pre-build VisibleEdgeOwnerEntries for fallback
            List<VisibleEdgeOwnerEntry> visibleEdgeEntries = BuildVisibleEdgeOwnerEntries(currentView);

            if (!string.IsNullOrEmpty(vPrefix))
            {
                RepairDanglingDimensions.LogDebug($"{vPrefix} L ABOUT_TO_GET_XFORM");
            }

            // Read View Translation & Scale via GetXform()
            try
            {
                object xfObj = currentView.GetXform();
                if (xfObj is Array xfArr && xfArr.Length >= 3)
                {
                    viewGeom.ViewX = Convert.ToDouble(xfArr.GetValue(0));
                    viewGeom.ViewY = Convert.ToDouble(xfArr.GetValue(1));
                    viewGeom.ViewXformScale = Convert.ToDouble(xfArr.GetValue(2));
                    viewGeom.ViewXformStatus = "GET_XFORM_OK";
                }
                else
                {
                    try
                    {
                        object posObj = currentView.Position;
                        if (posObj is double[] pArr && pArr.Length >= 2)
                        {
                            viewGeom.ViewX = pArr[0];
                            viewGeom.ViewY = pArr[1];
                        }
                        viewGeom.ViewXformScale = viewGeom.ScaleDecimal;
                        viewGeom.ViewXformStatus = "FALLBACK_POSITION";
                    }
                    catch
                    {
                        viewGeom.ViewXformScale = viewGeom.ScaleDecimal;
                        viewGeom.ViewXformStatus = "SCALE_ONLY";
                    }
                }
            }
            catch (Exception ex)
            {
                viewGeom.ViewXformStatus = "EX: " + ex.Message;
                viewGeom.ViewXformScale = viewGeom.ScaleDecimal;
            }

            if (!string.IsNullOrEmpty(vPrefix))
            {
                RepairDanglingDimensions.LogDebug($"{vPrefix} M GET_XFORM_RETURNED");
            }

            // Display Mode Precheck
            try
            {
                viewGeom.DisplayModeRaw = currentView.GetDisplayMode2();
                viewGeom.DisplayModeName = ((swDisplayMode_e)viewGeom.DisplayModeRaw).ToString();
            }
            catch (Exception ex)
            {
                viewGeom.DisplayModeName = "EX: " + ex.Message;
            }

            try
            {
                viewGeom.FacettedHlr = currentView.GetFacettedHlrDisplay();
            }
            catch {}

            try
            {
                viewGeom.DisplayEdgesInShaded = currentView.GetDisplayEdgesInShadedMode();
            }
            catch {}

            // PolyLine Count Precheck (both CrossHatch 0 and 1)
            int ptCount0 = 0;
            int ptCount1 = 0;
            try
            {
                viewGeom.PolylineCountOption0 = currentView.GetPolyLineCount5((short)0, out ptCount0);
                viewGeom.PolylinePointCountOption0 = ptCount0;
            }
            catch (Exception ex)
            {
                viewGeom.PolylineApiStatus = "EXCEPTION_COUNT0: " + ex.Message;
            }

            try
            {
                viewGeom.PolylineCountOption1 = currentView.GetPolyLineCount5((short)1, out ptCount1);
                viewGeom.PolylinePointCountOption1 = ptCount1;
            }
            catch (Exception ex)
            {
                viewGeom.PolylineApiStatus = "EXCEPTION_COUNT1: " + ex.Message;
            }

            if (!string.IsNullOrEmpty(vPrefix))
            {
                RepairDanglingDimensions.LogDebug($"{vPrefix} N ABOUT_TO_GET_POLYLINES7");
            }

            // Call GetPolylines7
            object polylinesDataObj = null;
            object entitiesObj = null;

            try
            {
                entitiesObj = currentView.GetPolylines7((short)1, out polylinesDataObj);
            }
            catch (Exception ex)
            {
                viewGeom.PolylineApiStatus = "EXCEPTION_GETPOLYLINES7: " + ex.GetType().FullName + " | " + ex.Message;
                if (ex.InnerException != null)
                {
                    viewGeom.PolylineApiStatus += " [Inner: " + ex.InnerException.Message + "]";
                }
            }

            // Fallback to GetPolylines6 if GetPolylines7 failed
            if (entitiesObj == null && polylinesDataObj == null)
            {
                try
                {
                    entitiesObj = currentView.GetPolylines6((short)1, out polylinesDataObj);
                }
                catch (Exception ex)
                {
                    if (string.IsNullOrEmpty(viewGeom.PolylineApiStatus) || viewGeom.PolylineApiStatus == "NOT_RUN")
                    {
                        viewGeom.PolylineApiStatus = "EXCEPTION_GETPOLYLINES6: " + ex.Message;
                    }
                }
            }

            Array entityArr = entitiesObj as Array;
            Array polylineArr = polylinesDataObj as Array;

            if (!string.IsNullOrEmpty(vPrefix))
            {
                int entLen = (entityArr != null) ? entityArr.Length : 0;
                int polyLen = (polylineArr != null) ? polylineArr.Length : 0;
                RepairDanglingDimensions.LogDebug($"{vPrefix} O GET_POLYLINES7_RETURNED (EntityCount={entLen}, RawDoubleCount={polyLen})");
            }

            if (entityArr != null)
            {
                viewGeom.PolylineReturnedEntityCount = entityArr.Length;
                int maxSample = Math.Min(10, entityArr.Length);
                for (int i = 0; i < maxSample; i++)
                {
                    object item = entityArr.GetValue(i);
                    if (item == null)
                    {
                        viewGeom.RawEntitySample.Add($"Entity[{i}]: NULL");
                    }
                    else
                    {
                        string tName = item.GetType().FullName;
                        string eType = "UNKNOWN";
                        if (item is IEdge) eType = "EDGE";
                        else if (item is ISilhouetteEdge) eType = "SILHOUETTE";
                        viewGeom.RawEntitySample.Add($"Entity[{i}]: {tName} ({eType})");
                    }
                }
            }

            List<double> rawDoubles = new List<double>();
            if (polylineArr != null)
            {
                viewGeom.PolylineReturnedDoubleCount = polylineArr.Length;
                int len = polylineArr.Length;
                for (int i = 0; i < len; i++)
                {
                    try
                    {
                        rawDoubles.Add(Convert.ToDouble(polylineArr.GetValue(i)));
                    }
                    catch {}
                }

                int maxDbl = Math.Min(40, rawDoubles.Count);
                for (int i = 0; i < maxDbl; i++)
                {
                    viewGeom.RawDoubleSample.Add(rawDoubles[i]);
                }
            }

            // Dump Raw Doubles with Full Precision around cursor >= 4880
            if (rawDoubles.Count >= 4880)
            {
                int dumpEnd = Math.Min(4990, rawDoubles.Count - 1);
                for (int d = 4880; d <= dumpEnd; d++)
                {
                    viewGeom.RawBoundarySamples.Add($"RAW[{d}] = {rawDoubles[d].ToString("R", CultureInfo.InvariantCulture)}");
                }
            }

            // Candidate Starts around 4920
            for (int offset = 4917; offset <= 4922; offset++)
            {
                if (offset + 1 < rawDoubles.Count)
                {
                    viewGeom.CandidateHeaderSamples.Add($"Offset {offset}: TypeCandidate={rawDoubles[offset].ToString("R", CultureInfo.InvariantCulture)}, GeomSizeCandidate={rawDoubles[offset + 1].ToString("R", CultureInfo.InvariantCulture)}");
                }
            }

            if (!string.IsNullOrEmpty(vPrefix))
            {
                RepairDanglingDimensions.LogDebug($"{vPrefix} P ABOUT_TO_PARSE_POLYLINES7");
            }

            // EXPECTED-RECORDS BASED PARSER
            int expectedRecords = (entityArr != null && entityArr.Length > 0) ? entityArr.Length : viewGeom.PolylineCountOption1;
            if (expectedRecords <= 0 && rawDoubles.Count > 0)
            {
                expectedRecords = 10000;
            }

            int cursor = 0;
            double effectiveScale = (viewGeom.ViewXformScale > 1e-9) ? viewGeom.ViewXformScale : viewGeom.ScaleDecimal;

            for (int recordIndex = 0; recordIndex < expectedRecords; recordIndex++)
            {
                if (cursor >= rawDoubles.Count)
                {
                    viewGeom.PolylineParserStatus = "CURSOR_OVERFLOW";
                    break;
                }

                int cursorStart = cursor;
                PolylineRawRecordDiagnostic diag = new PolylineRawRecordDiagnostic
                {
                    RecordIndex = recordIndex,
                    CursorStart = cursorStart,
                    CorrespondingEntityIndex = recordIndex
                };

                object correspondingEntity = null;
                int expectedTypeFromCurve = -1;

                if (entityArr != null && recordIndex < entityArr.Length)
                {
                    correspondingEntity = entityArr.GetValue(recordIndex);
                    if (correspondingEntity != null)
                    {
                        diag.EntityRuntimeType = correspondingEntity.GetType().Name;

                        if (correspondingEntity is IEdge edge)
                        {
                            try
                            {
                                ICurve curve = edge.GetCurve() as ICurve;
                                if (curve != null)
                                {
                                    diag.CurveIsLine = curve.IsLine();
                                    diag.CurveIsCircle = curve.IsCircle();
                                    diag.CurveIsEllipse = curve.IsEllipse();
                                    diag.CurveIsBcurve = curve.IsBcurve();
                                    diag.CurveIsTrimmedCurve = curve.IsTrimmedCurve();

                                    if (diag.CurveIsLine == true) expectedTypeFromCurve = 0;
                                    else if (diag.CurveIsCircle == true) expectedTypeFromCurve = 1;
                                    else if (diag.CurveIsEllipse == true) expectedTypeFromCurve = 2;
                                    else if (diag.CurveIsBcurve == true) expectedTypeFromCurve = 3;
                                }
                            }
                            catch {}
                        }
                    }
                }
                diag.ExpectedTypeFromCurve = expectedTypeFromCurve;

                // 1. Read Type & GeomDataSize with Full Precision Validation
                if (cursor + 2 > rawDoubles.Count)
                {
                    diag.IsValid = false;
                    diag.Error = "TRUNCATED_RECORD_HEADER";
                    viewGeom.RecordDiagnostics.Add(diag);
                    viewGeom.PolylineParserStatus = "TRUNCATED_RECORD_HEADER";
                    break;
                }

                int typePos = cursor;
                double typeRaw = rawDoubles[cursor++];
                double typeNearest, typeErr;
                int geomType;
                bool isTypeInt = TryReadStructuralInteger(typeRaw, out geomType, out typeNearest, out typeErr);

                diag.TypeRawDouble = typeRaw;
                diag.TypeRounded = typeNearest;
                diag.TypeIntegerError = typeErr;
                diag.Type = geomType;

                int geomSizePos = cursor;
                double geomSizeRaw = rawDoubles[cursor++];
                double geomSizeNearest, geomSizeErr;
                int geomDataSize;
                bool isGeomSizeInt = TryReadStructuralInteger(geomSizeRaw, out geomDataSize, out geomSizeNearest, out geomSizeErr);

                diag.GeomSizeRawDouble = geomSizeRaw;
                diag.GeomSizeRounded = geomSizeNearest;
                diag.GeomSizeIntegerError = geomSizeErr;
                diag.GeomDataSize = geomDataSize;
                diag.CursorAfterHeader = cursor;

                // Compare RawType vs ExpectedTypeFromCurve (Diagnostic Representation Difference)
                if (expectedTypeFromCurve >= 0)
                {
                    if (geomType == expectedTypeFromCurve)
                    {
                        diag.TypeMatchesCurve = true;
                        viewGeom.TypeCurveMatchCount++;
                    }
                    else
                    {
                        diag.TypeMatchesCurve = false;
                        viewGeom.TypeCurveMismatchCount++;
                        if (viewGeom.FirstMismatchRecordIndex == -1)
                        {
                            viewGeom.FirstMismatchRecordIndex = recordIndex;
                            viewGeom.FirstMismatchDescription = $"Rec #{recordIndex:D3} @ Cursor {cursorStart}: RawRepresentation={geomType}, UnderlyingCurveType={expectedTypeFromCurve} (Line={diag.CurveIsLine}, Circ={diag.CurveIsCircle}, Ellip={diag.CurveIsEllipse}, Bcurve={diag.CurveIsBcurve})";
                        }
                    }
                }

                if (!isTypeInt || !isGeomSizeInt)
                {
                    diag.IsValid = false;
                    diag.Error = $"NON_INTEGER_STRUCTURAL_VALUE (TypeRaw={typeRaw.ToString("R", CultureInfo.InvariantCulture)}, GeomSizeRaw={geomSizeRaw.ToString("R", CultureInfo.InvariantCulture)})";
                    viewGeom.RecordDiagnostics.Add(diag);
                    viewGeom.PolylineParserStatus = "NON_INTEGER_STRUCTURAL_VALUE";
                    break;
                }

                if (geomType < 0 || geomType > 3)
                {
                    diag.IsValid = false;
                    diag.Error = $"UNEXPECTED_TYPE_{geomType}";
                    viewGeom.RecordDiagnostics.Add(diag);
                    viewGeom.UnexpectedTypeCount++;
                    viewGeom.PolylineParserStatus = "UNEXPECTED_TYPE";
                    break;
                }

                int geomStart = cursor;
                int geomDataConsumed = 0;

                // STEP 8D-FIX3D / FIX3E: MANAGED GETPOLYLINES7 TYPE-DEPENDENT FLOW
                switch (geomType)
                {
                    case 0:
                        // Type 0 (Straight Line / Tessellated Polyline) has NO explicit GeomData block in managed GetPolylines7
                        // Field GeomDataSize is advisory only: DO NOT skip geomDataSize doubles.
                        geomDataConsumed = 0;
                        if (geomDataSize != 0)
                        {
                            viewGeom.Type0NonZeroGeomDataCount++;
                            viewGeom.Type0NonZeroRecordIndices.Add(recordIndex);
                        }
                        break;

                    case 1:
                    case 2:
                    case 3:
                        // Types 1 (Arc/Circle), 2 (Ellipse), 3 (Spline) contain analytic geometry data
                        if (geomDataSize < 0 || cursor + geomDataSize > rawDoubles.Count)
                        {
                            diag.IsValid = false;
                            diag.Error = $"INVALID_GEOM_DATA_SIZE_{geomDataSize}";
                            viewGeom.RecordDiagnostics.Add(diag);
                            viewGeom.PolylineParserStatus = "INVALID_GEOM_SIZE";
                            break;
                        }
                        cursor += geomDataSize;
                        geomDataConsumed = geomDataSize;
                        break;
                }

                if (!string.IsNullOrEmpty(diag.Error))
                {
                    break;
                }

                int geomEnd = cursor;
                diag.GeomDataConsumed = geomDataConsumed;
                diag.CursorAfterGeom = cursor;

                // 2. Consume 6 Line Attributes
                if (cursor + 6 > rawDoubles.Count)
                {
                    diag.IsValid = false;
                    diag.Error = "TRUNCATED_LINE_ATTRIBUTES";
                    viewGeom.RecordDiagnostics.Add(diag);
                    viewGeom.PolylineParserStatus = "TRUNCATED_ATTRIBUTES";
                    break;
                }
                int attrStart = cursor;
                diag.LineColor = rawDoubles[cursor++];
                diag.LineStyle = rawDoubles[cursor++];
                diag.LineFont = rawDoubles[cursor++];
                diag.LineWeight = rawDoubles[cursor++];
                diag.LayerID = rawDoubles[cursor++];
                diag.LayerOverride = rawDoubles[cursor++];
                int attrEnd = cursor;
                diag.CursorBeforePoints = cursor;

                // 3. Read NumPolyPoints with Full Precision
                if (cursor + 1 > rawDoubles.Count)
                {
                    diag.IsValid = false;
                    diag.Error = "TRUNCATED_NUM_POINTS";
                    viewGeom.RecordDiagnostics.Add(diag);
                    viewGeom.PolylineParserStatus = "TRUNCATED_NUM_POINTS";
                    break;
                }

                int numPtsPos = cursor;
                double numPtsRaw = rawDoubles[cursor++];
                double numPtsNearest, numPtsErr;
                int numPolyPoints;
                bool isNumPtsInt = TryReadStructuralInteger(numPtsRaw, out numPolyPoints, out numPtsNearest, out numPtsErr);

                diag.NumPointsRawDouble = numPtsRaw;
                diag.NumPointsRounded = numPtsNearest;
                diag.NumPointsIntegerError = numPtsErr;
                diag.NumPoints = numPolyPoints;

                if (!isNumPtsInt)
                {
                    diag.IsValid = false;
                    diag.Error = $"NON_INTEGER_NUM_POINTS (Raw={numPtsRaw.ToString("R", CultureInfo.InvariantCulture)}, Nearest={numPtsNearest}, Err={numPtsErr.ToString("R", CultureInfo.InvariantCulture)})";
                    viewGeom.RecordDiagnostics.Add(diag);
                    viewGeom.PolylineParserStatus = "NON_INTEGER_NUM_POINTS";
                    break;
                }

                if (numPolyPoints < 0)
                {
                    diag.IsValid = false;
                    diag.Error = $"NEGATIVE_NUM_POLY_POINTS_{numPolyPoints}";
                    viewGeom.RecordDiagnostics.Add(diag);
                    viewGeom.PolylineParserStatus = "NEGATIVE_NUM_POINTS";
                    break;
                }

                int needed = numPolyPoints * 3;
                if (cursor + needed > rawDoubles.Count)
                {
                    diag.IsValid = false;
                    diag.Error = $"POLY_POINT_BUFFER_OVERFLOW_NEEDED_{needed}";
                    viewGeom.RecordDiagnostics.Add(diag);
                    viewGeom.PolylineParserStatus = "POLY_POINT_BUFFER_OVERFLOW";
                    break;
                }

                // 4. Read Polyline Local Points
                int ptDataStart = cursor;
                List<double[]> localPts = new List<double[]>();
                for (int p = 0; p < numPolyPoints; p++)
                {
                    localPts.Add(new double[] {
                        rawDoubles[cursor + p * 3],
                        rawDoubles[cursor + p * 3 + 1],
                        rawDoubles[cursor + p * 3 + 2]
                    });
                }
                cursor += needed;
                int ptDataEnd = cursor;
                diag.PointDataStart = ptDataStart;
                diag.PointDataEnd = ptDataEnd;
                diag.CursorEnd = cursor;
                diag.IsValid = true;

                // Log Sample Records Construction Breakdown
                if (recordIndex <= 10)
                {
                    viewGeom.PolylineRecordConstructionLog.Add($"Rec #{recordIndex:D3}: CursorStart={cursorStart}, Type={geomType}, GeomSizeField={geomDataSize}, GeomConsumed={geomDataConsumed}");
                    viewGeom.PolylineRecordConstructionLog.Add($"  Attributes=[{attrStart}->{attrEnd}] (Color={diag.LineColor}, Style={diag.LineStyle}, Font={diag.LineFont}, Wt={diag.LineWeight}, Layer={diag.LayerID}, Ovr={diag.LayerOverride})");
                    viewGeom.PolylineRecordConstructionLog.Add($"  NumPoints Pos={numPtsPos}, Raw={numPtsRaw.ToString("R", CultureInfo.InvariantCulture)}, Val={numPolyPoints}");
                    viewGeom.PolylineRecordConstructionLog.Add($"  PointData=[{ptDataStart}->{ptDataEnd}] ({needed} doubles)");
                    viewGeom.PolylineRecordConstructionLog.Add($"  CursorEnd={cursor}");
                }

                // 5. Update Structural Record Counters
                viewGeom.RawRecordCount++;
                if (geomType == 0) viewGeom.Type0StraightCount++;
                else if (geomType == 1) viewGeom.Type1ArcCircleCount++;
                else if (geomType == 2) viewGeom.Type2EllipseCount++;
                else if (geomType == 3) viewGeom.Type3SplineCount++;

                if (numPolyPoints == 0) viewGeom.ZeroPointRecordCount++;
                if (numPolyPoints == 1) viewGeom.SinglePointRecordCount++;
                if (numPolyPoints < 2) viewGeom.InsufficientPointRecordCount++;

                // 6. Component Owner Resolution Pipeline
                if (recordIndex == 0 && !string.IsNullOrEmpty(vPrefix))
                {
                    RepairDanglingDimensions.LogDebug($"{vPrefix} R ABOUT_TO_RESOLVE_CORRESPONDING_ENTITIES");
                }

                string ownerMethod;
                string failureReason;
                Component2 canonComp = ResolvePolylineOwner(swApp, correspondingEntity, currentView, visibleEdgeEntries, viewGeom, out ownerMethod, out failureReason);
                string canonCompName = null;
                string canonCompPath = null;
                string canonCompKey = null;

                diag.OwnerMethod = ownerMethod;
                if (canonComp != null)
                {
                    viewGeom.OwnerResolvedCount++;
                    if (ownerMethod == "ISldWorks.IsSame")
                    {
                        viewGeom.IsSameOwnerResolvedCount++;
                    }
                    else
                    {
                        viewGeom.DirectOwnerResolvedCount++;
                    }

                    try { canonCompName = canonComp.Name2; } catch {}
                    try { canonCompPath = canonComp.GetPathName(); } catch {}
                    canonCompKey = RepairDimCandidateFinder.GetComponentOccurrenceKey(canonComp);
                }
                else
                {
                    viewGeom.OwnerNullCount++;
                    if (failureReason != null && failureReason.StartsWith("ISSAME_AMBIGUOUS_OWNER"))
                    {
                        viewGeom.OwnerAmbiguousCount++;
                    }
                    else if (failureReason != null && failureReason.StartsWith("ISSAME_UNSUPPORTED"))
                    {
                        viewGeom.IsSameUnsupportedCount++;
                    }
                }

                diag.CanonicalComponentName = canonCompName;
                diag.CanonicalComponentKey = canonCompKey;
                diag.EdgeSignature = ComputeEdgeSignature(correspondingEntity, canonCompKey);

                // 7. Convert Points to Sheet Space (if any points exist)
                List<double[]> sheetPts = new List<double[]>();
                for (int p = 0; p < localPts.Count; p++)
                {
                    sheetPts.Add(ViewLocalToSheet(localPts[p], viewGeom.ViewX, viewGeom.ViewY, effectiveScale));
                }

                double[] localStart = (localPts.Count > 0) ? localPts[0] : null;
                double[] localEnd = (localPts.Count > 0) ? localPts[localPts.Count - 1] : null;
                double[] sheetStart = (sheetPts.Count > 0) ? sheetPts[0] : null;
                double[] sheetEnd = (sheetPts.Count > 0) ? sheetPts[sheetPts.Count - 1] : null;

                double lenLocalMm = 0.0;
                double lenSheetMm = 0.0;
                string orient = "UNKNOWN";

                if (localPts.Count >= 2 && localStart != null && localEnd != null && sheetStart != null && sheetEnd != null)
                {
                    double dxLocal = localEnd[0] - localStart[0];
                    double dyLocal = localEnd[1] - localStart[1];
                    lenLocalMm = Math.Sqrt(dxLocal * dxLocal + dyLocal * dyLocal) * 1000.0;

                    double dxSheet = sheetEnd[0] - sheetStart[0];
                    double dySheet = sheetEnd[1] - sheetStart[1];
                    double lenSheetM = Math.Sqrt(dxSheet * dxSheet + dySheet * dySheet);
                    lenSheetMm = lenSheetM * 1000.0;

                    if (lenSheetM > 1e-7)
                    {
                        if (Math.Abs(dySheet) <= 0.08 * lenSheetM || Math.Abs(dySheet) <= 0.15 * Math.Abs(dxSheet))
                        {
                            orient = "HORIZONTAL";
                        }
                        else if (Math.Abs(dxSheet) <= 0.08 * lenSheetM || Math.Abs(dxSheet) <= 0.15 * Math.Abs(dySheet))
                        {
                            orient = "VERTICAL";
                        }
                        else
                        {
                            orient = "DIAGONAL";
                        }
                    }
                }

                bool nearOutline = true;
                if (viewGeom.Outline != null && viewGeom.Outline.Length >= 4 && sheetStart != null && sheetEnd != null)
                {
                    double minX = viewGeom.Outline[0] - 0.005;
                    double minY = viewGeom.Outline[1] - 0.005;
                    double maxX = viewGeom.Outline[2] + 0.005;
                    double maxY = viewGeom.Outline[3] + 0.005;
                    nearOutline = (sheetStart[0] >= minX && sheetStart[0] <= maxX && sheetStart[1] >= minY && sheetStart[1] <= maxY) ||
                                  (sheetEnd[0] >= minX && sheetEnd[0] <= maxX && sheetEnd[1] >= minY && sheetEnd[1] <= maxY);
                }

                DrawingPolylineEdgeInfo info = new DrawingPolylineEdgeInfo
                {
                    RawRecordIndex = recordIndex,
                    EntityArrayIndex = recordIndex,
                    GeometryType = geomType,
                    GeometryDataSize = geomDataSize,
                    GeometryDataConsumed = geomDataConsumed,
                    OwnerMethod = ownerMethod,
                    ModelEntity = correspondingEntity,
                    ModelEdge = correspondingEntity as IEdge,
                    Component = canonComp,
                    ComponentName = canonCompName,
                    ComponentPath = canonCompPath,
                    ComponentOccurrenceKey = canonCompKey,
                    EdgeSignature = diag.EdgeSignature,
                    ViewLocalPoints = localPts,
                    ViewLocalStart = localStart,
                    ViewLocalEnd = localEnd,
                    SheetPoints = sheetPts,
                    SheetStart = sheetStart,
                    SheetEnd = sheetEnd,
                    LengthViewLocalMm = lenLocalMm,
                    LengthSheetMm = lenSheetMm,
                    Orientation = orient,
                    IsStraight = (localPts.Count == 2),
                    InsideOrNearViewOutline = nearOutline
                };

                // Strict Repair Eligibility (Edge + Straight Line + Points >= 2)
                bool isEdge = (correspondingEntity is IEdge);
                bool isLine = (diag.CurveIsLine == true);
                bool hasPoints = (localPts.Count >= 2);

                if (isEdge && isLine && hasPoints)
                {
                    info.IsEligibleForRepair = true;
                    diag.IsRepairEligible = true;
                    viewGeom.EligibleLinearRepairCount++;
                    viewGeom.RepairLineRecords.Add(info);
                }
                else
                {
                    info.IsEligibleForRepair = false;
                    diag.IsRepairEligible = false;
                    if (!hasPoints) diag.RepairIneligibleReason = "INSUFFICIENT_POLYLINE_POINTS";
                    else if (!isEdge) diag.RepairIneligibleReason = "NOT_EDGE_ENTITY";
                    else if (!isLine) diag.RepairIneligibleReason = "UNDERLYING_CURVE_NOT_LINE";
                }

                viewGeom.AllPolylineRecords.Add(info);
                viewGeom.RecordDiagnostics.Add(diag);

                // Add to main list
                list.Add(info);
            }

            if (!string.IsNullOrEmpty(vPrefix))
            {
                RepairDanglingDimensions.LogDebug($"{vPrefix} Q PARSE_POLYLINES7_RETURNED (LogicalRecords={viewGeom.AllPolylineRecords.Count}, RepairLineRecords={viewGeom.RepairLineRecords.Count})");
                RepairDanglingDimensions.LogDebug($"{vPrefix} S RESOLVE_CORRESPONDING_ENTITIES_RETURNED");
            }

            viewGeom.RecordCursorFinal = cursor;
            viewGeom.CursorFinal = cursor;
            viewGeom.ParsedPolylineCount = list.Count;
            viewGeom.Polylines = list;

            // STEP 8D-FIX3E: Auxiliary Geometry Tail Parser & Classification
            int tailStart = cursor;
            int trailingCount = rawDoubles.Count - cursor;
            int tailCursor = cursor;

            List<PolylineRawRecordDiagnostic> nonZeroRecords = new List<PolylineRawRecordDiagnostic>();
            int sumDeclaredGeomSize = 0;
            foreach (var r in viewGeom.RecordDiagnostics)
            {
                if (r.Type == 0 && r.GeomDataSize > 0)
                {
                    nonZeroRecords.Add(r);
                    sumDeclaredGeomSize += r.GeomDataSize;
                }
            }

            bool tailSizeMatch = (trailingCount == sumDeclaredGeomSize);
            bool allAssociatedAreEllipses = true;

            for (int b = 0; b < nonZeroRecords.Count; b++)
            {
                var recDiag = nonZeroRecords[b];
                int blockSize = recDiag.GeomDataSize;

                if (recDiag.CurveIsEllipse != true)
                {
                    allAssociatedAreEllipses = false;
                }

                if (tailCursor + blockSize <= rawDoubles.Count)
                {
                    PolylineAuxGeometryBlock block = new PolylineAuxGeometryBlock
                    {
                        BlockIndex = b + 1,
                        AssociatedRecordIndex = recDiag.RecordIndex,
                        AssociatedEntityIndex = recDiag.CorrespondingEntityIndex,
                        ComponentName = recDiag.CanonicalComponentName,
                        CanonicalComponentKey = recDiag.CanonicalComponentKey,
                        CurveIsEllipse = (recDiag.CurveIsEllipse == true),
                        CurveIsBcurve = (recDiag.CurveIsBcurve == true),
                        DeclaredGeomSize = blockSize,
                        TailOffsetStart = tailCursor,
                        TailOffsetEnd = tailCursor + blockSize
                    };

                    for (int i = 0; i < blockSize; i++)
                    {
                        block.Values.Add(rawDoubles[tailCursor + i]);
                    }

                    // Extract Ellipse params from curve if available
                    if (entityArr != null && recDiag.CorrespondingEntityIndex < entityArr.Length)
                    {
                        object entObj = entityArr.GetValue(recDiag.CorrespondingEntityIndex);
                        if (entObj is IEdge edge)
                        {
                            try
                            {
                                ICurve curve = edge.GetCurve() as ICurve;
                                if (curve != null && curve.IsEllipse())
                                {
                                    object ellObj = curve.GetEllipseParams();
                                    if (ellObj is double[] ellArr && ellArr.Length >= 11)
                                    {
                                        block.EllipseCenter = new double[] { ellArr[0], ellArr[1], ellArr[2] };
                                        block.MajorRadius = ellArr[3];
                                        block.MajorAxis = new double[] { ellArr[4], ellArr[5], ellArr[6] };
                                        block.MinorRadius = ellArr[7];
                                        block.MinorAxis = new double[] { ellArr[8], ellArr[9], ellArr[10] };
                                        block.EllipseParamsSummary = $"Center=({ellArr[0]:F5},{ellArr[1]:F5},{ellArr[2]:F5}), MajorR={ellArr[3]:F5}, MajorAxis=({ellArr[4]:F3},{ellArr[5]:F3},{ellArr[6]:F3}), MinorR={ellArr[7]:F5}, MinorAxis=({ellArr[8]:F3},{ellArr[9]:F3},{ellArr[10]:F3})";
                                    }
                                }
                            }
                            catch {}
                        }
                    }

                    viewGeom.AuxGeometryBlocks.Add(block);
                    tailCursor += blockSize;
                }
            }

            viewGeom.AuxTailStart = tailStart;
            viewGeom.AuxTailLength = trailingCount;
            viewGeom.SumDeclaredAuxGeomSize = sumDeclaredGeomSize;
            viewGeom.AuxTailSizeMatch = tailSizeMatch;
            viewGeom.AuxTailFinalCursor = tailCursor;

            // Alignment & Status Validation
            bool recordsAligned = (entityArr != null && viewGeom.RawRecordCount == entityArr.Length);
            viewGeom.RecordEntityAlignment = recordsAligned ? "PASS" : (entityArr != null ? $"MISMATCH ({viewGeom.RawRecordCount}/{entityArr.Length})" : "NO_ENTITY_ARRAY");
            viewGeom.LogicalRecordParsing = recordsAligned ? "PASS" : "FAIL";

            if (trailingCount == 0 && cursor == rawDoubles.Count)
            {
                viewGeom.AuxTailAlignment = "NONE";
                viewGeom.CursorAlignment = "PASS";
                viewGeom.FinalBufferStatus = "PASS";
                viewGeom.PolylineParserStatus = "PASS";
                viewGeom.PolylineApiStatus = "POLYLINE_API_OK";
                viewGeom.PolylineRootCause = "NONE";
            }
            else if (tailSizeMatch && allAssociatedAreEllipses && tailCursor == rawDoubles.Count)
            {
                viewGeom.AuxTailAlignment = "PASS";
                viewGeom.CursorAlignment = "PASS";
                viewGeom.FinalBufferStatus = "PASS";
                viewGeom.PolylineParserStatus = "PASS_WITH_AUX_GEOMETRY_TAIL";
                viewGeom.PolylineApiStatus = "POLYLINE_API_OK";
                viewGeom.PolylineRootCause = "NONE";
            }
            else
            {
                viewGeom.AuxTailAlignment = $"UNRESOLVED (Trailing: {trailingCount}, Declared: {sumDeclaredGeomSize}, Ellipses: {allAssociatedAreEllipses})";
                viewGeom.CursorAlignment = $"TRAILING_DATA (RecordCursor: {cursor}, AuxCursor: {tailCursor}, Total: {rawDoubles.Count})";
                viewGeom.FinalBufferStatus = "FAIL";
                viewGeom.PolylineParserStatus = "AUX_TAIL_UNRESOLVED";
                viewGeom.PolylineApiStatus = "POLYLINE_DATA_UNRESOLVED";
                viewGeom.PolylineRootCause = viewGeom.PolylineParserStatus;
            }

            return list;
        }

        public static List<DrawingPolylineEdgeInfo> FindAnchorPolylineMatches(
            DanglingDimensionInfo dimInfo,
            List<DrawingPolylineEdgeInfo> viewPolylines)
        {
            List<DrawingPolylineEdgeInfo> matches = new List<DrawingPolylineEdgeInfo>();
            if (dimInfo == null || dimInfo.AnchorEntity == null || viewPolylines == null || viewPolylines.Count == 0)
            {
                return matches;
            }

            string anchorSig = ComputeEdgeSignature(dimInfo.AnchorEntity, dimInfo.AnchorOccurrenceKey);

            // LEVEL 1: Exact Signature Match (same ComponentOccurrenceKey + same raw endpoint geometry)
            if (!string.IsNullOrEmpty(anchorSig))
            {
                foreach (var p in viewPolylines)
                {
                    if (string.Equals(p.EdgeSignature, anchorSig, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(p);
                    }
                }
            }

            // LEVEL 2: Fallback same ModelEntity COM equality
            if (matches.Count == 0)
            {
                foreach (var p in viewPolylines)
                {
                    if (object.ReferenceEquals(p.ModelEntity, dimInfo.AnchorEntity))
                    {
                        matches.Add(p);
                    }
                }
            }

            // LEVEL 3: Fallback same component name & same endpoint geometry (if OccurrenceKey differed by synthetic prefix)
            if (matches.Count == 0 && !string.IsNullOrEmpty(dimInfo.AnchorComponentName) && dimInfo.AnchorRawStartPt != null && dimInfo.AnchorRawEndPt != null)
            {
                foreach (var p in viewPolylines)
                {
                    if (!string.IsNullOrEmpty(p.ComponentName) &&
                        (string.Equals(p.ComponentName, dimInfo.AnchorComponentName, StringComparison.OrdinalIgnoreCase) ||
                         p.ComponentName.EndsWith(dimInfo.AnchorComponentName, StringComparison.OrdinalIgnoreCase) ||
                         dimInfo.AnchorComponentName.EndsWith(p.ComponentName, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (p.ModelEntity is IEdge edge)
                        {
                            try
                            {
                                CurveParamData cpd = edge.GetCurveParams3();
                                if (cpd != null && cpd.StartPoint is double[] s && cpd.EndPoint is double[] e)
                                {
                                    double d1 = Math.Sqrt(Math.Pow(s[0] - dimInfo.AnchorRawStartPt[0], 2) + Math.Pow(s[1] - dimInfo.AnchorRawStartPt[1], 2) + Math.Pow(s[2] - dimInfo.AnchorRawStartPt[2], 2)) +
                                                Math.Sqrt(Math.Pow(e[0] - dimInfo.AnchorRawEndPt[0], 2) + Math.Pow(e[1] - dimInfo.AnchorRawEndPt[1], 2) + Math.Pow(e[2] - dimInfo.AnchorRawEndPt[2], 2));
                                    double d2 = Math.Sqrt(Math.Pow(s[0] - dimInfo.AnchorRawEndPt[0], 2) + Math.Pow(s[1] - dimInfo.AnchorRawEndPt[1], 2) + Math.Pow(s[2] - dimInfo.AnchorRawEndPt[2], 2)) +
                                                Math.Sqrt(Math.Pow(e[0] - dimInfo.AnchorRawStartPt[0], 2) + Math.Pow(e[1] - dimInfo.AnchorRawStartPt[1], 2) + Math.Pow(e[2] - dimInfo.AnchorRawStartPt[2], 2));
                                    if (Math.Min(d1, d2) <= 1e-4)
                                    {
                                        matches.Add(p);
                                    }
                                }
                            }
                            catch {}
                        }
                    }
                }
            }

            return matches;
        }

        public static ExtractedEdgeInfo ExtractEdgeGeometry(
            ISldWorks swApp,
            object entity,
            Component2 comp,
            SolidWorks.Interop.sldworks.View view,
            List<double[]> displayPoints,
            bool isSilhouette)
        {
            if (entity == null) return null;

            MathUtility mathUtil = null;
            if (swApp != null)
            {
                try { mathUtil = swApp.GetMathUtility() as MathUtility; } catch {}
            }

            ExtractedEdgeInfo info = new ExtractedEdgeInfo
            {
                Entity = entity,
                Component = comp,
                IsSilhouette = isSilhouette,
                GeometryType = "OTHER",
                Orientation = "UNKNOWN"
            };

            if (comp != null)
            {
                try
                {
                    info.ComponentName = comp.Name2;
                    info.ComponentPath = comp.GetPathName();

                    string occName = (comp.Name2 ?? "").Trim();
                    string modPath = (comp.GetPathName() ?? "").Trim();
                    if (!string.IsNullOrEmpty(occName) || !string.IsNullOrEmpty(modPath))
                    {
                        info.ComponentOccurrenceKey = occName + "|" + modPath;
                    }
                }
                catch {}
            }

            double[] startModelPt = null;
            double[] endModelPt = null;

            try
            {
                if (entity is IEdge edge)
                {
                    try
                    {
                        ICurve curve = edge.GetCurve() as ICurve;
                        if (curve != null)
                        {
                            if (curve.IsLine()) info.GeometryType = "LINE";
                            else if (curve.IsCircle()) info.GeometryType = "CIRCLE";
                            else if (curve.IsEllipse()) info.GeometryType = "ELLIPSE";
                            else if (curve.IsBcurve()) info.GeometryType = "BCURVE";
                        }
                    }
                    catch {}

                    try
                    {
                        CurveParamData cpd = edge.GetCurveParams3();
                        if (cpd != null)
                        {
                            startModelPt = cpd.StartPoint as double[];
                            endModelPt = cpd.EndPoint as double[];
                            if (cpd.CurveType == 3001) // LINE_CURVE
                            {
                                info.GeometryType = "LINE";
                            }
                            else if (cpd.CurveType == 3002) // CIRC_CURVE
                            {
                                info.GeometryType = "CIRCLE";
                            }
                            else if (cpd.CurveType == 3003) // ELLIPSE_CURVE
                            {
                                info.GeometryType = "ELLIPSE";
                            }
                        }
                    }
                    catch {}

                    if (startModelPt == null || endModelPt == null)
                    {
                        try
                        {
                            IVertex vStart = edge.GetStartVertex() as IVertex;
                            IVertex vEnd = edge.GetEndVertex() as IVertex;
                            if (vStart != null) startModelPt = vStart.GetPoint() as double[];
                            if (vEnd != null) endModelPt = vEnd.GetPoint() as double[];
                        }
                        catch {}
                    }
                }
                else if (entity is ISilhouetteEdge silEdge)
                {
                    try
                    {
                        ICurve silCurve = silEdge.GetCurve() as ICurve;
                        if (silCurve != null)
                        {
                            if (silCurve.IsLine()) info.GeometryType = "LINE";
                            else if (silCurve.IsCircle()) info.GeometryType = "CIRCLE";
                            else if (silCurve.IsEllipse()) info.GeometryType = "ELLIPSE";
                            else if (silCurve.IsBcurve()) info.GeometryType = "BCURVE";

                            double startParam, endParam;
                            bool isClosed, isPeriodic;
                            if (silCurve.GetEndParams(out startParam, out endParam, out isClosed, out isPeriodic))
                            {
                                startModelPt = silCurve.Evaluate2(startParam, 0) as double[];
                                endModelPt = silCurve.Evaluate2(endParam, 0) as double[];
                            }
                        }
                    }
                    catch {}
                }
            }
            catch {}

            if (startModelPt == null || endModelPt == null)
            {
                return null;
            }

            info.RawStartPt = new double[] { startModelPt[0], startModelPt[1], startModelPt[2] };
            info.RawEndPt = new double[] { endModelPt[0], endModelPt[1], endModelPt[2] };

            // Retrieve Component & View Transformations
            MathTransform compXform = null;
            if (comp != null)
            {
                try { compXform = comp.Transform2; } catch {}
            }
            info.HasComponentTransform = (compXform != null);

            MathTransform viewXform = null;
            if (view != null)
            {
                try { viewXform = view.ModelToViewTransform; } catch {}
            }
            info.HasViewTransform = (viewXform != null);

            // ROUTE A: Raw -> Component (Assembly Space) -> View (Drawing Space)
            double[] startAssemPt = TransformPoint(mathUtil, startModelPt, compXform);
            double[] endAssemPt = TransformPoint(mathUtil, endModelPt, compXform);
            info.AssemblyStartPt = startAssemPt;
            info.AssemblyEndPt = endAssemPt;

            double[] drawingStart_RouteA = TransformPoint(mathUtil, startAssemPt, viewXform);
            double[] drawingEnd_RouteA = TransformPoint(mathUtil, endAssemPt, viewXform);

            // ROUTE B: Raw -> View directly
            double[] drawingStart_RouteB = TransformPoint(mathUtil, startModelPt, viewXform);
            double[] drawingEnd_RouteB = TransformPoint(mathUtil, endModelPt, viewXform);
            info.DirectViewStartPt = drawingStart_RouteB;
            info.DirectViewEndPt = drawingEnd_RouteB;

            // Route Evaluation against DisplayData proximity
            double distA = double.MaxValue;
            double distB = double.MaxValue;

            if (displayPoints != null && displayPoints.Count > 0)
            {
                double dStartA = ComputeMinDistanceToPoints(drawingStart_RouteA, displayPoints);
                double dEndA = ComputeMinDistanceToPoints(drawingEnd_RouteA, displayPoints);
                distA = Math.Min(dStartA, dEndA);

                double dStartB = ComputeMinDistanceToPoints(drawingStart_RouteB, displayPoints);
                double dEndB = ComputeMinDistanceToPoints(drawingEnd_RouteB, displayPoints);
                distB = Math.Min(dStartB, dEndB);
            }

            // Decide Coordinate Route:
            if (compXform != null)
            {
                if (displayPoints != null && displayPoints.Count > 0 && distB < (distA - 10.0) && distB <= 50.0)
                {
                    info.CoordinateMethod = "DIRECT_MODEL_TO_VIEW";
                    info.StartSheetPt = drawingStart_RouteB;
                    info.EndSheetPt = drawingEnd_RouteB;
                }
                else
                {
                    info.CoordinateMethod = "COMPONENT_TO_ASSEMBLY_TO_VIEW";
                    info.StartSheetPt = drawingStart_RouteA;
                    info.EndSheetPt = drawingEnd_RouteA;
                }
            }
            else
            {
                info.CoordinateMethod = "DIRECT_MODEL_TO_VIEW";
                info.StartSheetPt = drawingStart_RouteB;
                info.EndSheetPt = drawingEnd_RouteB;
            }

            if (info.StartSheetPt == null || info.EndSheetPt == null)
            {
                return null;
            }

            info.MidSheetPt = new double[]
            {
                (info.StartSheetPt[0] + info.EndSheetPt[0]) * 0.5,
                (info.StartSheetPt[1] + info.EndSheetPt[1]) * 0.5,
                (info.StartSheetPt[2] + info.EndSheetPt[2]) * 0.5
            };

            double dx = info.EndSheetPt[0] - info.StartSheetPt[0];
            double dy = info.EndSheetPt[1] - info.StartSheetPt[1];
            double lenMeters = Math.Sqrt(dx * dx + dy * dy);
            info.LengthMm = lenMeters * 1000.0;

            if (lenMeters < 1e-7)
            {
                info.Direction2D = new double[] { 0.0, 0.0 };
                info.Orientation = "UNKNOWN";
                return info;
            }

            double ux = dx / lenMeters;
            double uy = dy / lenMeters;

            // Classify 2D Orientation on Drawing Sheet AFTER coordinate transformation
            if (Math.Abs(dy) <= 0.08 * lenMeters || Math.Abs(dy) <= 0.15 * Math.Abs(dx))
            {
                info.Orientation = "HORIZONTAL";
                if (ux < 0) { ux = -ux; uy = -uy; }
            }
            else if (Math.Abs(dx) <= 0.08 * lenMeters || Math.Abs(dx) <= 0.15 * Math.Abs(dy))
            {
                info.Orientation = "VERTICAL";
                if (uy < 0) { ux = -ux; uy = -uy; }
            }
            else
            {
                info.Orientation = "DIAGONAL";
                if (ux < 0) { ux = -ux; uy = -uy; }
            }

            info.Direction2D = new double[] { ux, uy };

            // Signature for deduplication
            string compKey = info.ComponentOccurrenceKey ?? info.ComponentName ?? "Root";
            info.Signature = $"{compKey}_{info.GeometryType}_{info.Orientation}_{Math.Round(info.StartSheetPt[0], 4)}_{Math.Round(info.StartSheetPt[1], 4)}_{Math.Round(info.EndSheetPt[0], 4)}_{Math.Round(info.EndSheetPt[1], 4)}";

            return info;
        }

        public static bool IsParallel(double[] dirA, double[] dirB, double tolerance = ParallelToleranceDot)
        {
            if (dirA == null || dirB == null || dirA.Length < 2 || dirB.Length < 2) return false;
            double dot = dirA[0] * dirB[0] + dirA[1] * dirB[1];
            return Math.Abs(dot) >= tolerance;
        }

        public static double ComputePerpendicularDistanceMm(ExtractedEdgeInfo anchor, ExtractedEdgeInfo cand)
        {
            if (anchor == null || cand == null || anchor.Direction2D == null || cand.MidSheetPt == null || anchor.StartSheetPt == null)
            {
                return double.MaxValue;
            }

            if (anchor.Orientation == "HORIZONTAL" && cand.Orientation == "HORIZONTAL")
            {
                // Pure Y distance in sheet coordinates
                double dy = Math.Abs(cand.MidSheetPt[1] - anchor.StartSheetPt[1]);
                return dy * 1000.0;
            }
            else if (anchor.Orientation == "VERTICAL" && cand.Orientation == "VERTICAL")
            {
                // Pure X distance in sheet coordinates
                double dx = Math.Abs(cand.MidSheetPt[0] - anchor.StartSheetPt[0]);
                return dx * 1000.0;
            }

            double ux = anchor.Direction2D[0];
            double uy = anchor.Direction2D[1];

            // Normal vector to anchor line in 2D sheet plane
            double nx = -uy;
            double ny = ux;

            // Vector from anchor point to candidate midpoint in meters
            double vx = cand.MidSheetPt[0] - anchor.StartSheetPt[0];
            double vy = cand.MidSheetPt[1] - anchor.StartSheetPt[1];

            // Perpendicular projection
            double distMeters = Math.Abs(vx * nx + vy * ny);
            return distMeters * 1000.0;
        }

        public static double ComputeSignedOffsetMm(ExtractedEdgeInfo anchor, ExtractedEdgeInfo cand, double viewScaleDecimal)
        {
            if (anchor == null || cand == null || anchor.StartSheetPt == null || cand.MidSheetPt == null)
            {
                return 0.0;
            }

            double signedSheetOffsetM = 0.0;

            if (anchor.Orientation == "HORIZONTAL")
            {
                signedSheetOffsetM = cand.MidSheetPt[1] - anchor.StartSheetPt[1];
            }
            else if (anchor.Orientation == "VERTICAL")
            {
                signedSheetOffsetM = cand.MidSheetPt[0] - anchor.StartSheetPt[0];
            }
            else
            {
                double ux = anchor.Direction2D != null ? anchor.Direction2D[0] : 1.0;
                double uy = anchor.Direction2D != null ? anchor.Direction2D[1] : 0.0;
                double nx = -uy;
                double ny = ux;
                double vx = cand.MidSheetPt[0] - anchor.StartSheetPt[0];
                double vy = cand.MidSheetPt[1] - anchor.StartSheetPt[1];
                signedSheetOffsetM = vx * nx + vy * ny;
            }

            return SheetDistanceToModelMm(signedSheetOffsetM * 1000.0, viewScaleDecimal);
        }

        public static bool IsCloseDimension(double actualMm, double targetMm, double absTolMm = AbsoluteDistanceToleranceMm, double relTol = RelativeDistanceTolerance)
        {
            double diff = Math.Abs(actualMm - targetMm);
            double tol = Math.Max(absTolMm, Math.Abs(targetMm) * relTol);
            return diff <= tol;
        }

        public static double GetEffectiveToleranceMm(double targetMm)
        {
            return Math.Max(AbsoluteDistanceToleranceMm, Math.Abs(targetMm) * RelativeDistanceTolerance);
        }

        public static double ComputeAnnotationDistanceMm(ExtractedEdgeInfo cand, double[] annotPos)
        {
            if (cand == null || cand.MidSheetPt == null || annotPos == null || annotPos.Length < 2)
            {
                return 0.0;
            }

            double dx = (cand.MidSheetPt[0] - annotPos[0]) * 1000.0;
            double dy = (cand.MidSheetPt[1] - annotPos[1]) * 1000.0;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static ClosestPointResult2D ClosestPointOnSegment2D(double px, double py, double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1;
            double dy = y2 - y1;
            double lenSq = dx * dx + dy * dy;

            double closestX, closestY, t;
            if (lenSq < 1e-12)
            {
                closestX = x1;
                closestY = y1;
                t = 0.0;
            }
            else
            {
                t = ((px - x1) * dx + (py - y1) * dy) / lenSq;
                if (t < 0.0) t = 0.0;
                else if (t > 1.0) t = 1.0;

                closestX = x1 + t * dx;
                closestY = y1 + t * dy;
            }

            double diffX = px - closestX;
            double diffY = py - closestY;
            double distM = Math.Sqrt(diffX * diffX + diffY * diffY);

            return new ClosestPointResult2D
            {
                Point = new double[] { closestX, closestY },
                DistanceM = distM,
                DistanceMm = distM * 1000.0,
                ParamT = t
            };
        }

        public static double DistancePointToSegment(double px, double py, double x1, double y1, double x2, double y2)
        {
            return ClosestPointOnSegment2D(px, py, x1, y1, x2, y2).DistanceM;
        }

        public static string GetOrientationStringFromVector(double ux, double uy)
        {
            if (Math.Abs(uy) <= 0.087) return "HORIZONTAL"; // <= ~5 degrees with X axis
            if (Math.Abs(ux) <= 0.087) return "VERTICAL";   // <= ~5 degrees with Y axis
            return "SLANTED";
        }

        public static DisplayWitnessProfile BuildDisplayWitnessProfile(List<DisplayDimLine> lines, double[] annotPos = null)
        {
            DisplayWitnessProfile profile = new DisplayWitnessProfile();
            if (lines == null || lines.Count < 3)
            {
                profile.IsValid = false;
                profile.Status = "FAILED";
                profile.Confidence = "NONE";
                profile.ErrorReason = $"Insufficient lines in DisplayData (Count = {lines?.Count ?? 0})";
                return profile;
            }

            // Filter out micro-segments (< 0.1 mm in sheet space)
            List<DisplayDimLine> validLines = new List<DisplayDimLine>();
            foreach (var l in lines)
            {
                double dx = l.EndX - l.StartX;
                double dy = l.EndY - l.StartY;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len >= 0.0001) // >= 0.1 mm
                {
                    validLines.Add(l);
                }
            }

            if (validLines.Count < 3)
            {
                profile.IsValid = false;
                profile.Status = "FAILED";
                profile.Confidence = "NONE";
                profile.ErrorReason = $"Insufficient valid lines >= 0.1mm (Count = {validLines.Count})";
                return profile;
            }

            List<WitnessHypothesis> hypotheses = new List<WitnessHypothesis>();

            for (int i = 0; i < validLines.Count; i++)
            {
                var l1 = validLines[i];
                double dx1 = l1.EndX - l1.StartX;
                double dy1 = l1.EndY - l1.StartY;
                double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
                if (len1 < 1e-6) continue;
                double u1x = dx1 / len1;
                double u1y = dy1 / len1;

                for (int j = i + 1; j < validLines.Count; j++)
                {
                    var l2 = validLines[j];
                    double dx2 = l2.EndX - l2.StartX;
                    double dy2 = l2.EndY - l2.StartY;
                    double len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);
                    if (len2 < 1e-6) continue;
                    double u2x = dx2 / len2;
                    double u2y = dy2 / len2;

                    double dot12 = Math.Abs(u1x * u2x + u1y * u2y);
                    if (dot12 < 0.985) continue; // Parallel witness gate

                    for (int k = 0; k < validLines.Count; k++)
                    {
                        if (k == i || k == j) continue;
                        var ld = validLines[k];
                        double dxd = ld.EndX - ld.StartX;
                        double dyd = ld.EndY - ld.StartY;
                        double lend = Math.Sqrt(dxd * dxd + dyd * dyd);
                        if (lend < 1e-6) continue;
                        double udx = dxd / lend;
                        double udy = dyd / lend;

                        double dot1d = Math.Abs(u1x * udx + u1y * udy);
                        double dot2d = Math.Abs(u2x * udx + u2y * udy);

                        if (dot1d > 0.15 || dot2d > 0.15) continue; // Perpendicular dimension line gate

                        // Spacing check between L1 and L2 along dimension axis
                        double mid1x = (l1.StartX + l1.EndX) * 0.5;
                        double mid1y = (l1.StartY + l1.EndY) * 0.5;
                        double mid2x = (l2.StartX + l2.EndX) * 0.5;
                        double mid2y = (l2.StartY + l2.EndY) * 0.5;
                        double spacingM = Math.Abs((mid1x - mid2x) * udx + (mid1y - mid2y) * udy);
                        double spacingMm = spacingM * 1000.0;
                        if (spacingMm < 0.1) continue; // Must be spatially separated witness lines

                        // Proximity check of Dimension Line to L1 and L2
                        double dist_d_to_l1 = DistancePointToSegment(ld.StartX, ld.StartY, l1.StartX, l1.StartY, l1.EndX, l1.EndY);
                        double dist_d_to_l2 = DistancePointToSegment(ld.EndX, ld.EndY, l2.StartX, l2.StartY, l2.EndX, l2.EndY);
                        double dist_d_to_l1_rev = DistancePointToSegment(ld.EndX, ld.EndY, l1.StartX, l1.StartY, l1.EndX, l1.EndY);
                        double dist_d_to_l2_rev = DistancePointToSegment(ld.StartX, ld.StartY, l2.StartX, l2.StartY, l2.EndX, l2.EndY);

                        double minProximity = Math.Min(dist_d_to_l1 + dist_d_to_l2, dist_d_to_l1_rev + dist_d_to_l2_rev);
                        if (minProximity > 0.050) // within 50mm in sheet space
                        {
                            continue;
                        }

                        // Determine Geometry-side vs Dimension-side endpoints
                        double d1_s = DistancePointToSegment(l1.StartX, l1.StartY, ld.StartX, ld.StartY, ld.EndX, ld.EndY);
                        double d1_e = DistancePointToSegment(l1.EndX, l1.EndY, ld.StartX, ld.StartY, ld.EndX, ld.EndY);
                        double[] w1DimPt = (d1_s <= d1_e) ? new double[] { l1.StartX, l1.StartY, l1.StartZ } : new double[] { l1.EndX, l1.EndY, l1.EndZ };
                        double[] w1GeomPt = (d1_s <= d1_e) ? new double[] { l1.EndX, l1.EndY, l1.EndZ } : new double[] { l1.StartX, l1.StartY, l1.StartZ };

                        double d2_s = DistancePointToSegment(l2.StartX, l2.StartY, ld.StartX, ld.StartY, ld.EndX, ld.EndY);
                        double d2_e = DistancePointToSegment(l2.EndX, l2.EndY, ld.StartX, ld.StartY, ld.EndX, ld.EndY);
                        double[] w2DimPt = (d2_s <= d2_e) ? new double[] { l2.StartX, l2.StartY, l2.StartZ } : new double[] { l2.EndX, l2.EndY, l2.EndZ };
                        double[] w2GeomPt = (d2_s <= d2_e) ? new double[] { l2.EndX, l2.EndY, l2.EndZ } : new double[] { l2.StartX, l2.StartY, l2.StartZ };

                        // Witness unit direction (pointing from Dimension toward Geometry)
                        double w1_dx = w1GeomPt[0] - w1DimPt[0];
                        double w1_dy = w1GeomPt[1] - w1DimPt[1];
                        double w1_len = Math.Sqrt(w1_dx * w1_dx + w1_dy * w1_dy);
                        double uwx = (w1_len > 1e-6) ? (w1_dx / w1_len) : u1x;
                        double uwy = (w1_len > 1e-6) ? (w1_dy / w1_len) : u1y;

                        string dimOrient = GetOrientationStringFromVector(udx, udy);
                        string witOrient = GetOrientationStringFromVector(uwx, uwy);

                        // Structural Score
                        double score = 100.0;
                        score += (dot12 - 0.985) * 200.0;
                        score += (0.15 - Math.Max(dot1d, dot2d)) * 100.0;
                        score -= minProximity * 1000.0;
                        score += Math.Min(len1, len2) * 100.0;

                        if (Math.Abs(lend - spacingM) / Math.Max(lend, spacingM) < 0.3)
                        {
                            score += 15.0;
                        }

                        if (annotPos != null && annotPos.Length >= 2)
                        {
                            double dAnnot = DistancePointToSegment(annotPos[0], annotPos[1], ld.StartX, ld.StartY, ld.EndX, ld.EndY);
                            if (dAnnot <= 0.030)
                            {
                                score += 10.0;
                            }
                        }

                        hypotheses.Add(new WitnessHypothesis
                        {
                            W1 = l1,
                            W2 = l2,
                            DimLine = ld,
                            W1Start = new double[] { l1.StartX, l1.StartY, l1.StartZ },
                            W1End = new double[] { l1.EndX, l1.EndY, l1.EndZ },
                            W1GeomPt = w1GeomPt,
                            W1DimPt = w1DimPt,
                            W2Start = new double[] { l2.StartX, l2.StartY, l2.StartZ },
                            W2End = new double[] { l2.EndX, l2.EndY, l2.EndZ },
                            W2GeomPt = w2GeomPt,
                            W2DimPt = w2DimPt,
                            DimLineStart = new double[] { ld.StartX, ld.StartY, ld.StartZ },
                            DimLineEnd = new double[] { ld.EndX, ld.EndY, ld.EndZ },
                            DimAxisUnit = new double[] { udx, udy },
                            WitnessDirUnit = new double[] { uwx, uwy },
                            DimOrientation = dimOrient,
                            WitnessOrientation = witOrient,
                            SpacingMm = spacingMm,
                            ParallelDot = dot12,
                            PerpDot1 = dot1d,
                            PerpDot2 = dot2d,
                            ProximityMm = minProximity * 1000.0,
                            Score = score
                        });
                    }
                }
            }

            profile.HypothesisCount = hypotheses.Count;

            if (hypotheses.Count == 0)
            {
                profile.IsValid = false;
                profile.Status = "FAILED";
                profile.Confidence = "NONE";
                profile.ErrorReason = "Could not identify any valid (Witness1, Witness2, DimensionLine) hypothesis";
                return profile;
            }

            // Sort hypotheses descending by Score
            hypotheses.Sort((a, b) => b.Score.CompareTo(a.Score));
            WitnessHypothesis best = hypotheses[0];
            profile.BestScore = best.Score;

            if (hypotheses.Count == 1)
            {
                profile.Confidence = "HIGH";
                profile.ScoreGap = 0.0; // Single valid hypothesis
            }
            else
            {
                WitnessHypothesis second = hypotheses[1];
                profile.SecondScore = second.Score;
                profile.ScoreGap = best.Score - second.Score;

                // Check if top and second produce essentially identical witness origin coordinates
                double d1 = Math.Sqrt(Math.Pow(best.W1GeomPt[0] - second.W1GeomPt[0], 2) + Math.Pow(best.W1GeomPt[1] - second.W1GeomPt[1], 2)) * 1000.0;
                double d2 = Math.Sqrt(Math.Pow(best.W2GeomPt[0] - second.W2GeomPt[0], 2) + Math.Pow(best.W2GeomPt[1] - second.W2GeomPt[1], 2)) * 1000.0;
                bool originsIdentical = (d1 <= 0.5 && d2 <= 0.5);

                if (profile.ScoreGap >= 10.0 || originsIdentical)
                {
                    profile.Confidence = "HIGH";
                }
                else if (profile.ScoreGap >= 5.0)
                {
                    profile.Confidence = "MEDIUM";
                }
                else
                {
                    profile.Confidence = "LOW";
                    profile.Status = "AMBIGUOUS";
                    profile.ErrorReason = $"Ambiguous witness hypotheses (Top Score: {best.Score:F1}, Second Score: {second.Score:F1}, Gap: {profile.ScoreGap:F1})";
                    return profile;
                }
            }

            profile.DimensionLine = best.DimLine;
            profile.DimensionLineStart = best.DimLineStart;
            profile.DimensionLineEnd = best.DimLineEnd;
            profile.DimensionAxisUnitVector = best.DimAxisUnit;
            profile.DimensionLineOrientation = best.DimOrientation;
            profile.MeasurementAxis = best.DimOrientation;

            profile.WitnessLine1 = best.W1;
            profile.Witness1Start = best.W1Start;
            profile.Witness1End = best.W1End;
            profile.Witness1GeometryPoint = best.W1GeomPt;
            profile.Witness1DimensionPoint = best.W1DimPt;
            profile.Witness1Orientation = best.WitnessOrientation;

            profile.WitnessLine2 = best.W2;
            profile.Witness2Start = best.W2Start;
            profile.Witness2End = best.W2End;
            profile.Witness2GeometryPoint = best.W2GeomPt;
            profile.Witness2DimensionPoint = best.W2DimPt;
            profile.Witness2Orientation = best.WitnessOrientation;

            profile.WitnessDirectionUnitVector = best.WitnessDirUnit;
            profile.WitnessOrientation = best.WitnessOrientation;

            profile.IsValid = true;
            profile.Status = "VALID";
            return profile;
        }

        public static BrokenViewInfo ExtractBrokenViewInfo(SolidWorks.Interop.sldworks.View view)
        {
            BrokenViewInfo bInfo = new BrokenViewInfo();
            if (view == null) return bInfo;

            try
            {
                bInfo.IsBroken = view.IsBroken();
            }
            catch
            {
                bInfo.IsBroken = false;
            }

            if (!bInfo.IsBroken) return bInfo;

            double[] viewPos = null;
            try { viewPos = view.Position as double[]; } catch {}
            double vx = (viewPos != null && viewPos.Length >= 2) ? viewPos[0] : 0.0;
            double vy = (viewPos != null && viewPos.Length >= 2) ? viewPos[1] : 0.0;

            try
            {
                object blObj = view.GetBreakLines();
                if (blObj is object[] blArr && blArr.Length > 0)
                {
                    bInfo.BreakCount = blArr.Length;
                    for (int i = 0; i < blArr.Length; i++)
                    {
                        BreakLine bl = blArr[i] as BreakLine;
                        if (bl != null)
                        {
                            int orient = bl.Orientation;
                            int style = bl.Style;
                            double p1 = bl.GetPosition(0);
                            double p2 = bl.GetPosition(1);

                            double sheetP1 = (orient == 1) ? (vx + p1) : (vy + p1);
                            double sheetP2 = (orient == 1) ? (vx + p2) : (vy + p2);

                            bInfo.BreakLines.Add(new BreakLineInfo
                            {
                                Index = i + 1,
                                OrientationRaw = orient,
                                OrientationString = (orient == 0) ? "HORIZONTAL" : "VERTICAL",
                                Style = style,
                                Position1 = p1,
                                Position2 = p2,
                                SheetMinCoord = Math.Min(sheetP1, sheetP2),
                                SheetMaxCoord = Math.Max(sheetP1, sheetP2)
                            });
                        }
                    }
                }
                else
                {
                    int blCount2 = 0;
                    try { blCount2 = view.GetBreakLineCount2(out int size); } catch {}
                    bInfo.BreakCount = blCount2;
                }
            }
            catch {}

            return bInfo;
        }

        public static bool CheckIfDimensionCrossesBreak(
            DisplayWitnessProfile profile,
            BrokenViewInfo brokenInfo,
            double[] attachPt1,
            double[] attachPt2,
            double naiveModelDistMm,
            double targetModelDistMm,
            out int crossingCount)
        {
            crossingCount = 0;
            if (profile == null || brokenInfo == null || !brokenInfo.IsBroken)
            {
                return false;
            }

            double[] p1 = attachPt1 ?? profile.Witness1GeometryPoint;
            double[] p2 = attachPt2 ?? profile.Witness2GeometryPoint;
            if (p1 == null || p2 == null || p1.Length < 2 || p2.Length < 2)
            {
                return false;
            }

            double dimUdx = (profile.DimensionAxisUnitVector != null && profile.DimensionAxisUnitVector.Length >= 2) ? profile.DimensionAxisUnitVector[0] : 1.0;
            double dimUdy = (profile.DimensionAxisUnitVector != null && profile.DimensionAxisUnitVector.Length >= 2) ? profile.DimensionAxisUnitVector[1] : 0.0;
            bool isDimHorizontal = Math.Abs(dimUdx) >= Math.Abs(dimUdy);

            double minSpan = isDimHorizontal ? Math.Min(p1[0], p2[0]) : Math.Min(p1[1], p2[1]);
            double maxSpan = isDimHorizontal ? Math.Max(p1[0], p2[0]) : Math.Max(p1[1], p2[1]);

            if (brokenInfo.BreakLines != null && brokenInfo.BreakLines.Count > 0)
            {
                foreach (var bl in brokenInfo.BreakLines)
                {
                    bool breakAffectsDim = (isDimHorizontal && bl.OrientationRaw == 1) || (!isDimHorizontal && bl.OrientationRaw == 0);
                    if (!breakAffectsDim) continue;

                    if (bl.SheetMinCoord >= minSpan - 0.005 && bl.SheetMaxCoord <= maxSpan + 0.005)
                    {
                        crossingCount++;
                    }
                    else
                    {
                        double midBreak = (bl.SheetMinCoord + bl.SheetMaxCoord) * 0.5;
                        if (midBreak >= minSpan && midBreak <= maxSpan)
                        {
                            crossingCount++;
                        }
                    }
                }
            }

            // Fallback: If view is broken, and naive displayed model distance is significantly compressed (< 75% of target)
            // while span is non-trivial (> 10mm in sheet space)
            if (crossingCount == 0 && brokenInfo.IsBroken)
            {
                double spanSheetMm = Math.Abs(maxSpan - minSpan) * 1000.0;
                if (targetModelDistMm > 1e-4 && naiveModelDistMm < targetModelDistMm * 0.75 && spanSheetMm >= 10.0)
                {
                    crossingCount = 1;
                }
            }

            return (crossingCount > 0);
        }

        public static PointAnchorInfo ResolveSketchPointSheetPosition(
            ISldWorks swApp,
            SolidWorks.Interop.sldworks.View view,
            SketchPoint sp,
            DisplayWitnessProfile profile)
        {
            PointAnchorInfo info = new PointAnchorInfo
            {
                LivePoint = sp
            };

            if (sp == null)
            {
                info.ResolutionStatus = "POINT_ANCHOR_OBJECT_INVALID";
                return info;
            }

            try
            {
                info.RawX = sp.X;
                info.RawY = sp.Y;
                info.RawZ = sp.Z;
            }
            catch
            {
                info.ResolutionStatus = "POINT_ANCHOR_OBJECT_INVALID";
                return info;
            }

            try
            {
                object idObj = sp.GetID();
                if (idObj is int[] idArr && idArr.Length > 0) info.PointID = idArr[0];
            }
            catch {}

            Sketch liveSketch = null;
            try { liveSketch = sp.GetSketch() as Sketch; } catch {}
            info.OwnerSketch = liveSketch;

            Sketch viewSketch = null;
            try { viewSketch = view?.GetSketch() as Sketch; } catch {}

            try
            {
                Feature skFeat = liveSketch as Feature;
                info.SketchFeatureName = skFeat?.GetNameForSelection(out string _) ?? "";
            }
            catch {}

            if (liveSketch != null && viewSketch != null)
            {
                if (object.ReferenceEquals(liveSketch, viewSketch))
                {
                    info.BelongsToCurrentView = true;
                }
                else
                {
                    Feature lf = liveSketch as Feature;
                    Feature vf = viewSketch as Feature;
                    string lName = lf?.GetNameForSelection(out string _) ?? "";
                    string vName = vf?.GetNameForSelection(out string _) ?? "";
                    if (!string.IsNullOrEmpty(lName) && lName.Equals(vName, StringComparison.OrdinalIgnoreCase))
                    {
                        info.BelongsToCurrentView = true;
                    }
                }
            }

            double[] viewPos = null;
            try { viewPos = view?.Position as double[]; } catch {}
            double vx = (viewPos != null && viewPos.Length >= 2) ? viewPos[0] : 0.0;
            double vy = (viewPos != null && viewPos.Length >= 2) ? viewPos[1] : 0.0;

            double[] scaleRatio = null;
            try { scaleRatio = view?.ScaleRatio as double[]; } catch {}
            double scaleDecimal = (scaleRatio != null && scaleRatio.Length >= 2 && scaleRatio[1] > 0) ? (scaleRatio[0] / scaleRatio[1]) : 1.0;

            MathUtility mathUtil = null;
            try { mathUtil = swApp?.GetMathUtility() as MathUtility; } catch {}

            List<PointCoordinateHypothesis> rawHypotheses = new List<PointCoordinateHypothesis>();

            // Hypothesis 1: RAW_SKETCH_XYZ
            rawHypotheses.Add(new PointCoordinateHypothesis
            {
                Method = "RAW_SKETCH_XYZ",
                SheetXY = new double[] { info.RawX, info.RawY }
            });

            // Hypothesis 2: VIEW_POSITION_PLUS_RAW
            rawHypotheses.Add(new PointCoordinateHypothesis
            {
                Method = "VIEW_POSITION_PLUS_RAW",
                SheetXY = new double[] { vx + info.RawX, vy + info.RawY }
            });

            // Hypothesis 3: SKETCH_TO_MODEL -> VIEW_MODEL_TO_VIEW
            if (liveSketch != null)
            {
                try
                {
                    MathTransform skXform = liveSketch.ModelToSketchTransform;
                    if (skXform != null)
                    {
                        MathTransform invXform = skXform.Inverse() as MathTransform;
                        if (invXform != null && mathUtil != null)
                        {
                            MathPoint rawMathPt = mathUtil.CreatePoint(new double[] { info.RawX, info.RawY, info.RawZ }) as MathPoint;
                            MathPoint modelPt = rawMathPt?.MultiplyTransform(invXform) as MathPoint;
                            double[] mArr = modelPt?.ArrayData as double[];

                            if (mArr != null && mArr.Length >= 3)
                            {
                                MathTransform vXform = view?.ModelToViewTransform;
                                if (vXform != null)
                                {
                                    MathPoint viewPt = modelPt.MultiplyTransform(vXform) as MathPoint;
                                    double[] vArr = viewPt?.ArrayData as double[];
                                    if (vArr != null && vArr.Length >= 2)
                                    {
                                        rawHypotheses.Add(new PointCoordinateHypothesis
                                        {
                                            Method = "SKETCH_TO_MODEL_TO_VIEW_XFORM",
                                            SheetXY = new double[] { vArr[0], vArr[1] }
                                        });
                                    }
                                }

                                rawHypotheses.Add(new PointCoordinateHypothesis
                                {
                                    Method = "SKETCH_TO_MODEL_PLUS_VIEW_POS_SCALE",
                                    SheetXY = new double[] { vx + mArr[0] * scaleDecimal, vy + mArr[1] * scaleDecimal }
                                });
                            }
                        }
                    }
                }
                catch {}
            }

            // Hypothesis 4: VIEW_MODELTOVIEW_DIRECT
            if (mathUtil != null && view != null)
            {
                try
                {
                    MathTransform vXform = view.ModelToViewTransform;
                    if (vXform != null)
                    {
                        MathPoint rawMathPt = mathUtil.CreatePoint(new double[] { info.RawX, info.RawY, info.RawZ }) as MathPoint;
                        MathPoint viewPt = rawMathPt?.MultiplyTransform(vXform) as MathPoint;
                        double[] vArr = viewPt?.ArrayData as double[];
                        if (vArr != null && vArr.Length >= 2)
                        {
                            rawHypotheses.Add(new PointCoordinateHypothesis
                            {
                                Method = "VIEW_MODELTOVIEW_DIRECT",
                                SheetXY = new double[] { vArr[0], vArr[1] }
                            });
                        }
                    }
                }
                catch {}
            }

            // Evaluate all hypotheses against DisplayWitnessProfile
            List<PointCoordinateHypothesis> evaluated = new List<PointCoordinateHypothesis>();
            List<PointCoordinateHypothesis> matched = new List<PointCoordinateHypothesis>();

            foreach (var hyp in rawHypotheses)
            {
                if (profile != null && profile.IsValid && profile.Witness1GeometryPoint != null && profile.Witness2GeometryPoint != null)
                {
                    double d1 = Math.Sqrt(Math.Pow(hyp.SheetXY[0] - profile.Witness1GeometryPoint[0], 2) + Math.Pow(hyp.SheetXY[1] - profile.Witness1GeometryPoint[1], 2)) * 1000.0;
                    double d2 = Math.Sqrt(Math.Pow(hyp.SheetXY[0] - profile.Witness2GeometryPoint[0], 2) + Math.Pow(hyp.SheetXY[1] - profile.Witness2GeometryPoint[1], 2)) * 1000.0;

                    hyp.Witness1ErrorMm = d1;
                    hyp.Witness2ErrorMm = d2;

                    if (d1 <= 1.5 && d2 > 2.0)
                    {
                        hyp.IsMatched = true;
                        hyp.MatchedWitnessSide = 1;
                        hyp.ErrorMm = d1;
                        matched.Add(hyp);
                    }
                    else if (d2 <= 1.5 && d1 > 2.0)
                    {
                        hyp.IsMatched = true;
                        hyp.MatchedWitnessSide = 2;
                        hyp.ErrorMm = d2;
                        matched.Add(hyp);
                    }
                    else
                    {
                        hyp.IsMatched = false;
                        hyp.MatchedWitnessSide = 0;
                        hyp.ErrorMm = Math.Min(d1, d2);
                    }
                }

                evaluated.Add(hyp);
            }

            info.Hypotheses = evaluated;

            if (matched.Count == 0)
            {
                info.IsResolved = false;
                info.ResolutionStatus = "POINT_ANCHOR_POSITION_UNRESOLVED";
                return info;
            }

            // Check if matched hypotheses have conflicting witness sides
            bool hasSide1 = false, hasSide2 = false;
            foreach (var m in matched)
            {
                if (m.MatchedWitnessSide == 1) hasSide1 = true;
                if (m.MatchedWitnessSide == 2) hasSide2 = true;
            }

            if (hasSide1 && hasSide2)
            {
                info.IsResolved = false;
                info.ResolutionStatus = "POINT_ANCHOR_WITNESS_AMBIGUOUS";
                return info;
            }

            // Sort matched hypotheses by ErrorMm ascending
            matched.Sort((a, b) => a.ErrorMm.CompareTo(b.ErrorMm));
            PointCoordinateHypothesis best = matched[0];

            info.BestHypothesis = best;
            info.ResolvedSheetXY = best.SheetXY;
            info.LivePointWitnessSide = best.MatchedWitnessSide;
            info.MissingWitnessSide = (best.MatchedWitnessSide == 1) ? 2 : 1;
            info.PointWitnessErrorMm = best.ErrorMm;
            info.IsResolved = true;
            info.ResolutionStatus = "RESOLVED";

            return info;
        }

        private class WitnessHypothesis
        {
            public DisplayDimLine W1 { get; set; }
            public DisplayDimLine W2 { get; set; }
            public DisplayDimLine DimLine { get; set; }

            public double[] W1Start { get; set; }
            public double[] W1End { get; set; }
            public double[] W1GeomPt { get; set; }
            public double[] W1DimPt { get; set; }

            public double[] W2Start { get; set; }
            public double[] W2End { get; set; }
            public double[] W2GeomPt { get; set; }
            public double[] W2DimPt { get; set; }

            public double[] DimLineStart { get; set; }
            public double[] DimLineEnd { get; set; }

            public double[] DimAxisUnit { get; set; }
            public double[] WitnessDirUnit { get; set; }

            public string DimOrientation { get; set; }
            public string WitnessOrientation { get; set; }

            public double SpacingMm { get; set; }
            public double ParallelDot { get; set; }
            public double PerpDot1 { get; set; }
            public double PerpDot2 { get; set; }
            public double ProximityMm { get; set; }
            public double Score { get; set; }
        }
    }
}
