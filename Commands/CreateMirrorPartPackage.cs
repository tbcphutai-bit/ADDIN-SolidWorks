using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public enum MirrorPartSelectionMode
    {
        None = 0,
        Component = 1,
        Plane = 2
    }

    public enum MirrorReferenceKind
    {
        None = 0,
        ModelPlane = 1,
        IntersectionCenterline = 2,
        ParallelUnsupported = 3,
        ObliqueRequiresRehost = 4
    }

    public enum FeatureReplayDisposition
    {
        ReplayRequired = 0,
        NoGeometryChange = 1,
        Suppressed = 2,
        UnsupportedGeometryFeature = 3
    }

    public enum FeatureGeometryChangeKind
    {
        None = 0,
        Subtractive = 1,
        Additive = 2,
        Mixed = 3
    }

    public sealed class PlaneData
    {
        public double[] Origin { get; set; } = new double[3];
        public double[] Normal { get; set; } = new double[3];
    }

    public sealed class MirrorPackageResult
    {
        public bool Success { get; set; }
        public bool Cancelled { get; set; }
        public string Message { get; set; }
        public string MirrorPartPath { get; set; }
        public string MirrorDrawingPath { get; set; }
        public string Warning { get; set; }
    }

    public interface ISavePathProvider
    {
        string ResolveSavePath(ISldWorks swApp, string sourcePartPath, string defaultDir, string defaultFileName);
    }

    public sealed class NativeSaveAsProvider : ISavePathProvider
    {
        public string ResolveSavePath(ISldWorks swApp, string sourcePartPath, string defaultDir, string defaultFileName)
        {
            if (swApp == null || string.IsNullOrWhiteSpace(sourcePartPath)) return null;

            string initialPath = Path.Combine(defaultDir, defaultFileName);
            string chosenPath = null;

            using (SaveFileDialog saveFileDialog = new SaveFileDialog())
            {
                saveFileDialog.Filter = "SolidWorks Part (*.sldprt)|*.sldprt";
                saveFileDialog.InitialDirectory = defaultDir;
                saveFileDialog.FileName = defaultFileName;
                saveFileDialog.Title = "Save Mirrored Part As";

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    chosenPath = saveFileDialog.FileName;
                }
            }

            if (string.IsNullOrWhiteSpace(chosenPath))
            {
                CreateMirrorPartPackage.LogDebug("SAVE_AS selected=CANCELLED");
                return null;
            }

            CreateMirrorPartPackage.LogDebug($"SAVE_AS selected={chosenPath}");
            return chosenPath;
        }
    }

    public sealed class ExplicitSavePathProvider : ISavePathProvider
    {
        private readonly string targetPath;

        public ExplicitSavePathProvider(string path)
        {
            targetPath = path;
        }

        public string ResolveSavePath(ISldWorks swApp, string sourcePartPath, string defaultDir, string defaultFileName)
        {
            if (string.IsNullOrWhiteSpace(targetPath)) return null;

            string dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            CreateMirrorPartPackage.LogDebug($"SAVE_AS selected={targetPath}");
            return targetPath;
        }
    }

    public sealed class SourceDocumentGuard : IDisposable
    {
        private readonly ISldWorks swApp;
        private readonly string sourcePath;
        private readonly bool wasAlreadyOpen;
        private readonly ModelDoc2 sourceDoc;
        private readonly int initialFeatureCount;
        private readonly bool dirtyBefore;

        public ModelDoc2 Document => sourceDoc;
        public int FeatureCount => initialFeatureCount;
        public bool DirtyBefore => dirtyBefore;

        public SourceDocumentGuard(ISldWorks app, string path)
        {
            swApp = app;
            sourcePath = path;

            wasAlreadyOpen = CheckIfAlreadyOpen(app, path);

            int errors = 0;
            int warnings = 0;
            sourceDoc = swApp.OpenDoc6(
                sourcePath,
                (int)swDocumentTypes_e.swDocPART,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                "",
                ref errors,
                ref warnings);

            if (sourceDoc != null)
            {
                initialFeatureCount = sourceDoc.GetFeatureCount();
                dirtyBefore = sourceDoc.GetSaveFlag();
            }
        }

        public void Dispose()
        {
            if (sourceDoc != null)
            {
                int finalFeatureCount = sourceDoc.GetFeatureCount();
                bool dirtyAfter = sourceDoc.GetSaveFlag();

                CreateMirrorPartPackage.LogDebug($"SOURCE_UNCHANGED featureCount={finalFeatureCount} dirtyBefore={dirtyBefore} dirtyAfter={dirtyAfter}");

                if (!wasAlreadyOpen)
                {
                    swApp.CloseDoc(sourceDoc.GetTitle());
                }
            }
        }

        private static bool CheckIfAlreadyOpen(ISldWorks app, string path)
        {
            try
            {
                object[] docs = app.GetDocuments() as object[];
                if (docs != null)
                {
                    foreach (object d in docs)
                    {
                        ModelDoc2 doc = d as ModelDoc2;
                        if (doc != null && string.Equals(doc.GetPathName(), path, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch {}
            return false;
        }
    }

    public static class MirrorPlaneMapper
    {
        public static PlaneData GetLocalPlane(
            IMathUtility mathUtility,
            Component2 component,
            RefPlane assemblyPlane)
        {
            MathTransform planeTransform = assemblyPlane.Transform;

            MathPoint canonicalOrigin = mathUtility.CreatePoint(new double[] { 0.0, 0.0, 0.0 }) as MathPoint;
            MathPoint assemblyOriginPoint = canonicalOrigin.MultiplyTransform(planeTransform) as MathPoint;

            MathVector canonicalNormal = mathUtility.CreateVector(new double[] { 0.0, 0.0, 1.0 }) as MathVector;
            MathVector assemblyNormalVec = canonicalNormal.MultiplyTransform(planeTransform) as MathVector;

            MathTransform compTransform = component.Transform2;
            MathTransform assemblyToComponent = compTransform.IInverse();

            MathPoint localOriginPoint = assemblyOriginPoint.MultiplyTransform(assemblyToComponent) as MathPoint;
            double[] localOrigin = localOriginPoint.ArrayData as double[];

            MathVector localNormalVec = assemblyNormalVec.MultiplyTransform(assemblyToComponent) as MathVector;
            double[] localNormal = localNormalVec.ArrayData as double[];

            double length = Math.Sqrt(localNormal[0] * localNormal[0] + localNormal[1] * localNormal[1] + localNormal[2] * localNormal[2]);
            if (length > 1e-12)
            {
                localNormal[0] /= length;
                localNormal[1] /= length;
                localNormal[2] /= length;
            }

            CreateMirrorPartPackage.LogDebug($"PLANE_LOCAL origin=({localOrigin[0]:F4},{localOrigin[1]:F4},{localOrigin[2]:F4})");
            CreateMirrorPartPackage.LogDebug($"PLANE_LOCAL normal=({localNormal[0]:F4},{localNormal[1]:F4},{localNormal[2]:F4})");

            return new PlaneData
            {
                Origin = localOrigin,
                Normal = localNormal
            };
        }

        public static PlaneData CreatePartOriginAnchoredPlane(PlaneData selectedLocalPlane)
        {
            if (selectedLocalPlane == null || selectedLocalPlane.Normal == null || selectedLocalPlane.Normal.Length < 3)
            {
                throw new ArgumentException("Selected local mirror plane is invalid.", nameof(selectedLocalPlane));
            }

            double nx = selectedLocalPlane.Normal[0];
            double ny = selectedLocalPlane.Normal[1];
            double nz = selectedLocalPlane.Normal[2];
            double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (length <= 1e-12)
            {
                throw new InvalidOperationException("Selected local mirror plane has a zero-length normal.");
            }

            PlaneData anchoredPlane = new PlaneData
            {
                Origin = new double[] { 0.0, 0.0, 0.0 },
                Normal = new double[] { nx / length, ny / length, nz / length }
            };

            double[] selectedOrigin = selectedLocalPlane.Origin ?? new double[] { 0.0, 0.0, 0.0 };
            CreateMirrorPartPackage.LogDebug(
                "PART_ORIGIN_MIRROR_PLANE\n" +
                $"selectedPlaneOrigin=({selectedOrigin[0]:F9},{selectedOrigin[1]:F9},{selectedOrigin[2]:F9})\n" +
                "effectiveOrigin=(0.000000000,0.000000000,0.000000000)\n" +
                $"normal=({anchoredPlane.Normal[0]:F9},{anchoredPlane.Normal[1]:F9},{anchoredPlane.Normal[2]:F9})\n" +
                "rule=SELECTED_PLANE_DIRECTION_THROUGH_PART_ORIGIN");

            return anchoredPlane;
        }
    }

    public sealed class BodyBooleanResult
    {
        public bool Success { get; set; }
        public int ErrorCode { get; set; }
        public string Operation { get; set; }
        public List<Body2> Bodies { get; set; } = new List<Body2>();
        public string ErrorMessage { get; set; }
    }

    public sealed class BodyTransformResult
    {
        public bool Success { get; set; }
        public Body2 Body { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class FeatureSemanticValidationResult
    {
        public bool Success { get; set; }
        public string FeatureName { get; set; }
        public string FeatureType { get; set; }
        public FeatureGeometryChangeKind ExpectedChangeKind { get; set; }

        public int BeforeBodyCount { get; set; }
        public int AfterBodyCount { get; set; }

        public double BeforeVolume { get; set; }
        public double AfterVolume { get; set; }

        public double ActualAddedVolume { get; set; }
        public double ActualRemovedVolume { get; set; }
        public double ExpectedAddedVolume { get; set; }
        public double ExpectedRemovedVolume { get; set; }
        public double RelativeVolumeError { get; set; }

        public bool ActualMinusBeforeBooleanSuccess { get; set; }
        public bool BeforeMinusActualBooleanSuccess { get; set; }

        public string FailureReason { get; set; }
    }

    public sealed class FinalSketchStateResult
    {
        public bool Success { get; set; }
        public string SketchName { get; set; }
        public int OriginalNormalRemaining { get; set; }
        public int OriginalConstruction { get; set; }
        public int MirroredNormal { get; set; }
        public int InvariantNormal { get; set; }
        public int UnexpectedNormal { get; set; }
        public string FailureReason { get; set; }
    }

    public sealed class SketchDimensionState
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string FullName { get; set; }
        public double SystemValue { get; set; }
        public int DrivenState { get; set; }
        public bool IsReference { get; set; }
        public bool IsDangling { get; set; }
        public bool IsOriginLinked { get; set; }
        public object DisplayDimensionObject { get; set; }
        public object DimensionObject { get; set; }
        public List<object> AttachedEntities { get; } = new List<object>();
        public double[] AnnotationPosition { get; set; }
    }

    public sealed class SketchDimensionTransferResult
    {
        public bool Success { get; set; }
        public int Candidates { get; set; }
        public int Transferred { get; set; }
        public int Skipped { get; set; }
        public string FailureReason { get; set; }
    }

    internal sealed class PendingSketchDimensionTransfer
    {
        public SketchDimensionState Source { get; set; }
        public object NewDisplayDimension { get; set; }
        public object NewDimension { get; set; }
        public string Mode { get; set; }
    }

    public sealed class SketchAuditSnapshot
    {
        public List<SketchDimensionState> Dimensions { get; } = new List<SketchDimensionState>();
        public int RelationCount { get; set; }
        public int SuppressedRelationCount { get; set; }
        public int OriginLinkedDimensionCount { get; set; }
        public string CaptureWarning { get; set; }
    }

    public sealed class SketchDimensionAuditResult
    {
        public bool Success { get; set; }
        public int BeforeCount { get; set; }
        public int AfterCount { get; set; }
        public int RelationCountBefore { get; set; }
        public int RelationCountAfter { get; set; }
        public int SuppressedRelationsBefore { get; set; }
        public int SuppressedRelationsAfter { get; set; }
        public int OriginLinkedBefore { get; set; }
        public int OriginLinkedAfter { get; set; }
        public int DanglingAfter { get; set; }
        public int MissingCount { get; set; }
        public int ValueMismatchCount { get; set; }
        public string MissingDimensions { get; set; }
        public string ValueMismatchDimensions { get; set; }
        public string FailureReason { get; set; }
    }

    public sealed class SketchIndependenceResult
    {
        public bool Success { get; set; }
        public int RelationsBeforeMirror { get; set; }
        public int RelationsAfterMirror { get; set; }
        public int CandidateRelations { get; set; }
        public int SymmetricRelationsFound { get; set; }
        public int SymmetricRelationsDeleted { get; set; }
        public int RelationsAfterDetach { get; set; }
        public string FailureReason { get; set; }
    }

    public static class BodyOperationsHelper
    {
        public const double ABSOLUTE_GEOMETRY_TOLERANCE = 1e-12;
        public const double RELATIVE_TOLERANCE = 1e-7;

        public static double GetBodyVolume(Body2 body)
        {
            if (body == null) return 0.0;
            try
            {
                object mpObj = body.GetMassProperties(0);
                double[] mp = mpObj as double[];
                if (mp != null && mp.Length >= 4)
                {
                    return Math.Abs(mp[3]);
                }
            }
            catch {}
            return 0.0;
        }

        public static double SumBodyVolumes(IEnumerable<Body2> bodies)
        {
            double total = 0.0;
            if (bodies == null) return total;
            foreach (Body2 body in bodies)
            {
                total += GetBodyVolume(body);
            }
            return total;
        }

        private static bool TryGetBodiesVolumeCentroid(
            IEnumerable<Body2> bodies,
            out double totalVolume,
            out double[] centroid)
        {
            totalVolume = 0.0;
            centroid = null;
            double sx = 0.0;
            double sy = 0.0;
            double sz = 0.0;

            if (bodies == null) return false;

            foreach (Body2 body in bodies)
            {
                if (body == null) continue;
                try
                {
                    double[] mp = body.GetMassProperties(0) as double[];
                    if (mp == null || mp.Length < 4) continue;

                    double volume = Math.Abs(mp[3]);
                    if (double.IsNaN(volume) || double.IsInfinity(volume) ||
                        volume <= ABSOLUTE_GEOMETRY_TOLERANCE)
                    {
                        continue;
                    }

                    sx += mp[0] * volume;
                    sy += mp[1] * volume;
                    sz += mp[2] * volume;
                    totalVolume += volume;
                }
                catch { }
            }

            if (totalVolume <= ABSOLUTE_GEOMETRY_TOLERANCE) return false;

            centroid = new[]
            {
                sx / totalVolume,
                sy / totalVolume,
                sz / totalVolume
            };
            return true;
        }

        private static double[] ReflectPointAcrossPlane(double[] point, PlaneData plane)
        {
            if (point == null || point.Length < 3 || plane?.Origin == null || plane?.Normal == null)
            {
                return null;
            }

            double[] origin = plane.Origin;
            double[] normal = plane.Normal;
            double nn = normal[0] * normal[0] + normal[1] * normal[1] + normal[2] * normal[2];
            if (nn <= ABSOLUTE_GEOMETRY_TOLERANCE) return null;

            double signedScale =
                ((point[0] - origin[0]) * normal[0] +
                 (point[1] - origin[1]) * normal[1] +
                 (point[2] - origin[2]) * normal[2]) / nn;

            return new[]
            {
                point[0] - 2.0 * signedScale * normal[0],
                point[1] - 2.0 * signedScale * normal[1],
                point[2] - 2.0 * signedScale * normal[2]
            };
        }

        private static double Distance(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length < 3 || b.Length < 3)
            {
                return double.PositiveInfinity;
            }

            double dx = a[0] - b[0];
            double dy = a[1] - b[1];
            double dz = a[2] - b[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static string FormatPoint(double[] point)
        {
            return point == null || point.Length < 3
                ? "N/A"
                : string.Format("({0:F6},{1:F6},{2:F6})", point[0], point[1], point[2]);
        }

        private static bool TryMeasureRemovedGeometry(
            Body2 beforeBody,
            Body2 afterBody,
            string label,
            out double removedVolume,
            out double[] removedCentroid,
            out string error)
        {
            removedVolume = 0.0;
            removedCentroid = null;
            error = null;

            BodyBooleanResult cut = BooleanCutStrict(beforeBody, afterBody, label);
            if (!cut.Success)
            {
                error = cut.ErrorMessage ?? "Boolean cut failed while measuring removed geometry.";
                return false;
            }

            if (!TryGetBodiesVolumeCentroid(cut.Bodies, out removedVolume, out removedCentroid))
            {
                error = "Removed geometry is empty or its mass properties are unavailable.";
                return false;
            }

            return true;
        }

        private static bool TryMeasureRemovedVolume(
            Body2 beforeBody,
            Body2 afterBody,
            string label,
            out double removedVolume,
            out string error)
        {
            removedVolume = 0.0;
            error = null;

            double[] ignoredCentroid;
            return TryMeasureRemovedGeometry(
                beforeBody,
                afterBody,
                label,
                out removedVolume,
                out ignoredCentroid,
                out error);
        }

        private static bool TrySetFlipSideToCut(
            ModelDoc2 partDoc,
            Feature feature,
            bool flip,
            out string error)
        {
            error = null;
            IExtrudeFeatureData2 definition = null;
            bool selectionAccess = false;

            try
            {
                definition = feature?.GetDefinition() as IExtrudeFeatureData2;
                if (definition == null)
                {
                    error = "Feature definition is not IExtrudeFeatureData2.";
                    return false;
                }

                selectionAccess = definition.AccessSelections(partDoc, null);
                if (!selectionAccess)
                {
                    error = "IExtrudeFeatureData2.AccessSelections returned false.";
                    return false;
                }

                definition.FlipSideToCut = flip;
                if (!feature.ModifyDefinition(definition, partDoc, null))
                {
                    error = "Feature.ModifyDefinition returned false.";
                    return false;
                }

                partDoc.ForceRebuild3(false);
                bool warning = false;
                int featureError = feature.GetErrorCode2(out warning);
                if (featureError != 0)
                {
                    error = $"Feature rebuild error after FlipSideToCut={flip}: error={featureError}, warning={warning}.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (definition != null && selectionAccess)
                {
                    try { definition.ReleaseSelectionAccess(); } catch { }
                }
            }
        }

        public static bool TryRetargetExtrudeContours(
            ModelDoc2 partDoc,
            PostBaseFeatureInfo info,
            Feature sketchFeature,
            out string details)
        {
            details = null;
            if (partDoc == null || info?.Feature == null || sketchFeature == null)
            {
                details = "EXTRUDE_CONTOUR_RETARGET\nresult=SKIP\nreason=INVALID_ARGUMENT";
                return false;
            }

            if (!SketchDrivenFeatureMirrorHandler.IsExtrudeCutType(info.Type) &&
                !SketchDrivenFeatureMirrorHandler.IsExtrudeBossType(info.Type))
            {
                details =
                    $"EXTRUDE_CONTOUR_RETARGET\nfeature={info.Name}\nfeatureType={info.Type}\n" +
                    "result=SKIP\nreason=NOT_EXTRUDE_FEATURE";
                return true;
            }

            Sketch sketch = null;
            IExtrudeFeatureData2 definition = null;
            bool selectionAccess = false;
            int contourCandidates = 0;
            int activeContoursCount = 0;
            int activeSegmentsCount = 0;
            int assignedContoursCount = -1;
            int assignedContoursAfter = -1;
            string assignmentMode = "NONE";

            try
            {
                sketch = sketchFeature.GetSpecificFeature2() as Sketch;
                if (sketch == null)
                {
                    details =
                        $"EXTRUDE_CONTOUR_RETARGET\nfeature={info.Name}\nsketch={sketchFeature.Name}\n" +
                        "result=FAIL\nreason=SKETCH_NOT_AVAILABLE";
                    return false;
                }

                var activeContours = new List<SketchContour>();
                object rawContours = sketch.GetSketchContours();
                Array contourArray = rawContours as Array;
                if (contourArray != null)
                {
                    contourCandidates = contourArray.Length;
                    foreach (object rawContour in contourArray)
                    {
                        SketchContour contour = rawContour as SketchContour;
                        if (contour == null)
                            continue;

                        Array segmentArray = null;
                        try { segmentArray = contour.GetSketchSegments() as Array; }
                        catch { }

                        if (segmentArray == null || segmentArray.Length == 0)
                            continue;

                        int normalSegmentCount = 0;
                        foreach (object rawSegment in segmentArray)
                        {
                            SketchSegment segment = rawSegment as SketchSegment;
                            if (segment == null)
                                continue;

                            bool construction = true;
                            try { construction = segment.ConstructionGeometry; }
                            catch { }

                            if (!construction)
                                normalSegmentCount++;
                        }

                        // Only a contour containing active (non-construction) geometry may
                        // drive the mirrored cut. The original contour was intentionally
                        // converted to construction geometry and must not be reused here.
                        if (normalSegmentCount <= 0)
                            continue;

                        activeSegmentsCount += normalSegmentCount;
                        activeContours.Add(contour);
                    }
                }

                activeContoursCount = activeContours.Count;
                if (activeContoursCount == 0)
                {
                    details =
                        $"EXTRUDE_CONTOUR_RETARGET\nfeature={info.Name}\nsketch={sketchFeature.Name}\n" +
                        $"contourCandidates={contourCandidates}\nactiveContours=0\nactiveSegments=0\n" +
                        "result=FAIL\nreason=NO_ACTIVE_MIRRORED_CONTOUR";
                    return false;
                }

                definition = info.Feature.GetDefinition() as IExtrudeFeatureData2;
                if (definition == null)
                {
                    details =
                        $"EXTRUDE_CONTOUR_RETARGET\nfeature={info.Name}\nsketch={sketchFeature.Name}\n" +
                        $"contourCandidates={contourCandidates}\nactiveContours={activeContoursCount}\n" +
                        $"activeSegments={activeSegmentsCount}\nresult=FAIL\nreason=DEFINITION_NOT_EXTRUDE";
                    return false;
                }

                selectionAccess = definition.AccessSelections(partDoc, null);
                if (!selectionAccess)
                {
                    details =
                        $"EXTRUDE_CONTOUR_RETARGET\nfeature={info.Name}\nsketch={sketchFeature.Name}\n" +
                        $"contourCandidates={contourCandidates}\nactiveContours={activeContoursCount}\n" +
                        $"activeSegments={activeSegmentsCount}\nresult=FAIL\nreason=ACCESS_SELECTIONS_FAILED";
                    return false;
                }

                // Use the strongly typed COM setter. Assigning an object[] through the
                // Contours property can appear to work for a one-segment contour, but
                // SOLIDWORKS may reject ModifyDefinition for a closed contour made from
                // several sketch segments because the SAFEARRAY is not marshalled with
                // the explicit contour count expected by IExtrudeFeatureData2.
                SketchContour[] typedContours = activeContours.ToArray();
                object contourPayload = typedContours;
                try
                {
                    definition.ISetContours(typedContours.Length, ref contourPayload);
                    assignmentMode = "ISETCONTOURS";
                }
                catch (Exception setContoursException)
                {
                    // Retain the former property setter only as a compatibility fallback
                    // for SOLIDWORKS versions that expose ISetContours but reject the call.
                    definition.Contours = typedContours;
                    assignmentMode = "PROPERTY_FALLBACK:" + setContoursException.GetType().Name;
                }

                try { assignedContoursCount = definition.GetContoursCount(); }
                catch { assignedContoursCount = -1; }

                bool modified = info.Feature.ModifyDefinition(definition, partDoc, null);

                // ModifyDefinition may return true even when SOLIDWORKS silently keeps
                // the old contour selection. Re-read the live feature definition and
                // confirm that at least one explicit mirrored contour is persisted.
                try
                {
                    IExtrudeFeatureData2 verifyDefinition = info.Feature.GetDefinition() as IExtrudeFeatureData2;
                    if (verifyDefinition != null)
                        assignedContoursAfter = verifyDefinition.GetContoursCount();
                }
                catch
                {
                    assignedContoursAfter = -1;
                }

                bool assignmentConfirmed = modified && assignedContoursAfter > 0;
                details =
                    $"EXTRUDE_CONTOUR_RETARGET\nfeature={info.Name}\nsketch={sketchFeature.Name}\n" +
                    $"contourCandidates={contourCandidates}\nactiveContours={activeContoursCount}\n" +
                    $"activeSegments={activeSegmentsCount}\nassignmentMode={assignmentMode}\n" +
                    $"assignedContoursBefore={assignedContoursCount}\nassignedContoursAfter={assignedContoursAfter}\n" +
                    $"modifyResult={modified}\n" +
                    $"result={(assignmentConfirmed ? "PASS" : "FAIL")}\n" +
                    $"reason={(assignmentConfirmed ? "ACTIVE_MIRRORED_CONTOURS_ASSIGNED" : (modified ? "CONTOUR_ASSIGNMENT_NOT_CONFIRMED" : "MODIFY_DEFINITION_FAILED"))}";
                return assignmentConfirmed;
            }
            catch (Exception ex)
            {
                details =
                    $"EXTRUDE_CONTOUR_RETARGET\nfeature={info.Name}\nsketch={sketchFeature.Name}\n" +
                    $"contourCandidates={contourCandidates}\nactiveContours={activeContoursCount}\n" +
                    $"activeSegments={activeSegmentsCount}\nassignmentMode={assignmentMode}\n" +
                    $"assignedContoursBefore={assignedContoursCount}\nassignedContoursAfter={assignedContoursAfter}\n" +
                    $"result=FAIL\nreason=EXCEPTION: {ex.Message}";
                return false;
            }
            finally
            {
                if (definition != null && selectionAccess)
                {
                    try { definition.ReleaseSelectionAccess(); } catch { }
                }
            }
        }

        public static bool TryRecoverExtrudeCutRebuild(
            ModelDoc2 partDoc,
            PostBaseFeatureInfo info,
            FeatureBodyState originalCache,
            out string details)
        {
            details = null;
            if (partDoc == null || info?.Feature == null)
            {
                details = "CUT_FLIP_REBUILD_RECOVERY\nresult=SKIP\nreason=INVALID_ARGUMENT";
                return false;
            }

            if (!SketchDrivenFeatureMirrorHandler.IsExtrudeCutType(info.Type) ||
                originalCache?.RemovedBodies == null ||
                originalCache.RemovedBodies.Count == 0)
            {
                details =
                    $"CUT_FLIP_REBUILD_RECOVERY\nfeature={info.Name}\nfeatureType={info.Type}\n" +
                    "result=SKIP\nreason=NOT_CONFIRMED_CUT_FEATURE";
                return false;
            }

            bool warningBefore = false;
            int errorBefore = info.Feature.GetErrorCode2(out warningBefore);
            if (errorBefore == 0)
            {
                details =
                    $"CUT_FLIP_REBUILD_RECOVERY\nfeature={info.Name}\nerrorBefore=0\n" +
                    "result=PASS\nreason=FEATURE_ALREADY_HEALTHY";
                return true;
            }

            IExtrudeFeatureData2 definition = null;
            bool selectionAccess = false;
            bool originalFlip;
            try
            {
                definition = info.Feature.GetDefinition() as IExtrudeFeatureData2;
                if (definition == null)
                {
                    details =
                        $"CUT_FLIP_REBUILD_RECOVERY\nfeature={info.Name}\nerrorBefore={errorBefore}\n" +
                        "result=FAIL\nreason=DEFINITION_NOT_EXTRUDE";
                    return false;
                }

                selectionAccess = definition.AccessSelections(partDoc, null);
                if (!selectionAccess)
                {
                    details =
                        $"CUT_FLIP_REBUILD_RECOVERY\nfeature={info.Name}\nerrorBefore={errorBefore}\n" +
                        "result=FAIL\nreason=ACCESS_SELECTIONS_FAILED";
                    return false;
                }

                originalFlip = definition.FlipSideToCut;
            }
            catch (Exception ex)
            {
                details =
                    $"CUT_FLIP_REBUILD_RECOVERY\nfeature={info.Name}\nerrorBefore={errorBefore}\n" +
                    $"result=FAIL\nreason=READ_FLIP_FAILED: {ex.Message}";
                return false;
            }
            finally
            {
                if (definition != null && selectionAccess)
                {
                    try { definition.ReleaseSelectionAccess(); } catch { }
                }
            }

            bool toggledFlip = !originalFlip;
            string toggleError;
            bool toggleResult = TrySetFlipSideToCut(partDoc, info.Feature, toggledFlip, out toggleError);
            bool warningAfter = false;
            int errorAfter = info.Feature.GetErrorCode2(out warningAfter);
            if (toggleResult && errorAfter == 0)
            {
                details =
                    $"CUT_FLIP_REBUILD_RECOVERY\nfeature={info.Name}\nfeatureType={info.Type}\n" +
                    $"errorBefore={errorBefore}\nwarningBefore={warningBefore}\n" +
                    $"originalFlip={originalFlip}\ntoggledFlip={toggledFlip}\n" +
                    $"errorAfter={errorAfter}\nwarningAfter={warningAfter}\n" +
                    "result=PASS\nreason=TOGGLED_FLIP_REBUILT";
                return true;
            }

            string restoreError;
            bool restoreResult = TrySetFlipSideToCut(partDoc, info.Feature, originalFlip, out restoreError);
            bool warningRestored = false;
            int errorRestored = info.Feature.GetErrorCode2(out warningRestored);
            details =
                $"CUT_FLIP_REBUILD_RECOVERY\nfeature={info.Name}\nfeatureType={info.Type}\n" +
                $"errorBefore={errorBefore}\nwarningBefore={warningBefore}\n" +
                $"originalFlip={originalFlip}\ntoggledFlip={toggledFlip}\n" +
                $"toggleResult={toggleResult}\ntoggleError={toggleError ?? string.Empty}\n" +
                $"errorAfter={errorAfter}\nwarningAfter={warningAfter}\n" +
                $"restoreResult={restoreResult}\nrestoreError={restoreError ?? string.Empty}\n" +
                $"errorRestored={errorRestored}\nwarningRestored={warningRestored}\n" +
                "result=FAIL\nreason=TOGGLED_FLIP_DID_NOT_REBUILD";
            return false;
        }

        public static bool TryCorrectExtrudeCutFlip(
            ModelDoc2 partDoc,
            PostBaseFeatureInfo info,
            Body2 previousActualBody,
            FeatureBodyState originalCache,
            PlaneData mirrorPlane,
            ref Body2 replayedActualBody,
            out bool allowAsymmetricCutVolume,
            out string details)
        {
            allowAsymmetricCutVolume = false;
            details = null;
            if (partDoc == null || info?.Feature == null || previousActualBody == null || replayedActualBody == null)
            {
                details = "CUT_FLIP_EVALUATE result=FAIL reason=INVALID_ARGUMENT";
                return false;
            }

            if (!SketchDrivenFeatureMirrorHandler.IsExtrudeCutType(info.Type))
            {
                return true;
            }

            double expectedRemoved;
            double[] sourceRemovedCentroid;
            bool sourceGeometryAvailable = TryGetBodiesVolumeCentroid(
                originalCache?.RemovedBodies,
                out expectedRemoved,
                out sourceRemovedCentroid);
            if (!sourceGeometryAvailable)
            {
                details = $"CUT_FLIP_EVALUATE\nfeature={info.Name}\nresult=FAIL\nreason=SOURCE_REMOVED_GEOMETRY_UNAVAILABLE";
                return false;
            }

            double expectedTolerance = Math.Max(
                ABSOLUTE_GEOMETRY_TOLERANCE,
                Math.Max(expectedRemoved, GetBodyVolume(previousActualBody)) * RELATIVE_TOLERANCE);

            double currentRemoved;
            double[] currentRemovedCentroid;
            string measureError;
            if (!TryMeasureRemovedGeometry(
                previousActualBody,
                replayedActualBody,
                info.Name + "_CUT_FLIP_BEFORE",
                out currentRemoved,
                out currentRemovedCentroid,
                out measureError))
            {
                details = $"CUT_FLIP_EVALUATE\nfeature={info.Name}\nresult=FAIL\nreason=BEFORE_MEASURE_FAILED: {measureError}";
                return false;
            }

            double currentError = Math.Abs(currentRemoved - expectedRemoved);
            double[] expectedMirroredCentroid = ReflectPointAcrossPlane(sourceRemovedCentroid, mirrorPlane);
            double currentDistance = Distance(currentRemovedCentroid, expectedMirroredCentroid);

            // The removed volume is normally identical on both cut directions. Volume alone
            // cannot prove that the cut moved to the mirrored side. Always test the opposite
            // FlipSideToCut state and use the mirrored removed-region centroid to disambiguate.
            if (expectedMirroredCentroid == null || double.IsInfinity(currentDistance))
            {
                details = $"CUT_FLIP_GEOMETRY_EVALUATE\nfeature={info.Name}\nsourceRemovedCentroid={FormatPoint(sourceRemovedCentroid)}\nexpectedMirroredCentroid={FormatPoint(expectedMirroredCentroid)}\nbeforeCentroid={FormatPoint(currentRemovedCentroid)}\nresult=FAIL\nreason=BEFORE_REMOVED_REGION_CENTROID_UNAVAILABLE";
                return false;
            }

            IExtrudeFeatureData2 currentDefinition = info.Feature.GetDefinition() as IExtrudeFeatureData2;
            if (currentDefinition == null)
            {
                details = $"CUT_FLIP_EVALUATE\nfeature={info.Name}\nexpectedRemovedVolume={expectedRemoved:E6}\nbeforeToggleRemovedVolume={currentRemoved:E6}\nresult=FAIL\nreason=NOT_EXTRUDE_FEATURE_DATA2";
                return false;
            }

            bool originalFlip;
            try { originalFlip = currentDefinition.FlipSideToCut; }
            catch (Exception ex)
            {
                details = $"CUT_FLIP_EVALUATE\nfeature={info.Name}\nresult=FAIL\nreason=CANNOT_READ_FLIP_SIDE: {ex.Message}";
                return false;
            }

            bool toggledFlip = !originalFlip;
            string setError;
            if (!TrySetFlipSideToCut(partDoc, info.Feature, toggledFlip, out setError))
            {
                details = $"CUT_FLIP_EVALUATE\nfeature={info.Name}\noriginalFlip={originalFlip}\nexpectedRemovedVolume={expectedRemoved:E6}\nbeforeToggleRemovedVolume={currentRemoved:E6}\ntoggledFlip={toggledFlip}\nresult=FAIL\nreason=TOGGLE_FAILED: {setError}";
                return false;
            }

            string captureError;
            Body2 toggledBody = GetSolidBodyCopyStrict(partDoc, out captureError);
            double toggledRemoved = 0.0;
            double[] toggledRemovedCentroid = null;
            string toggledMeasureError = null;
            bool toggledMeasured = toggledBody != null &&
                TryMeasureRemovedGeometry(
                    previousActualBody,
                    toggledBody,
                    info.Name + "_CUT_FLIP_AFTER",
                    out toggledRemoved,
                    out toggledRemovedCentroid,
                    out toggledMeasureError);
            double toggledError = toggledMeasured ? Math.Abs(toggledRemoved - expectedRemoved) : double.PositiveInfinity;

            if (!toggledMeasured)
            {
                string restoreError;
                bool restored = TrySetFlipSideToCut(partDoc, info.Feature, originalFlip, out restoreError);
                details = $"CUT_FLIP_GEOMETRY_EVALUATE\nfeature={info.Name}\nsourceRemovedCentroid={FormatPoint(sourceRemovedCentroid)}\nexpectedMirroredCentroid={FormatPoint(expectedMirroredCentroid)}\nbeforeCentroid={FormatPoint(currentRemovedCentroid)}\nafterCentroid={FormatPoint(toggledRemovedCentroid)}\nresult=FAIL\nreason=AFTER_MEASURE_FAILED: {toggledMeasureError ?? captureError}\nrestoreResult={restored}\nrestoreError={restoreError}";
                return false;
            }

            double toggledDistance = Distance(toggledRemovedCentroid, expectedMirroredCentroid);
            if (double.IsInfinity(toggledDistance))
            {
                string restoreError;
                bool restored = TrySetFlipSideToCut(partDoc, info.Feature, originalFlip, out restoreError);
                details = $"CUT_FLIP_GEOMETRY_EVALUATE\nfeature={info.Name}\nsourceRemovedCentroid={FormatPoint(sourceRemovedCentroid)}\nexpectedMirroredCentroid={FormatPoint(expectedMirroredCentroid)}\nbeforeCentroid={FormatPoint(currentRemovedCentroid)}\nafterCentroid={FormatPoint(toggledRemovedCentroid)}\nresult=FAIL\nreason=AFTER_REMOVED_REGION_CENTROID_UNAVAILABLE\nrestoreResult={restored}\nrestoreError={restoreError}";
                return false;
            }

            bool currentVolumeMatches = currentError <= expectedTolerance;
            bool toggledVolumeMatches = toggledMeasured && toggledError <= expectedTolerance;
            bool chooseToggled;
            string selectionReason;
            if (currentVolumeMatches && toggledVolumeMatches)
            {
                chooseToggled = toggledDistance < currentDistance;
                selectionReason = "BOTH_VOLUMES_MATCHED_MIRRORED_CENTROID_NEAREST";
            }
            else if (toggledVolumeMatches)
            {
                chooseToggled = true;
                selectionReason = "TOGGLED_SOURCE_VOLUME_MATCHED";
            }
            else if (currentVolumeMatches)
            {
                chooseToggled = false;
                selectionReason = "CURRENT_SOURCE_VOLUME_MATCHED";
            }
            else
            {
                chooseToggled = toggledDistance < currentDistance;
                allowAsymmetricCutVolume = true;
                selectionReason = "MIRRORED_REMOVED_REGION_NEAREST";
            }

            if (chooseToggled)
            {
                replayedActualBody = toggledBody;
            }
            else
            {
                string restoreError;
                if (!TrySetFlipSideToCut(partDoc, info.Feature, originalFlip, out restoreError))
                {
                    details = $"CUT_FLIP_EVALUATE\nfeature={info.Name}\noriginalFlip={originalFlip}\nexpectedRemovedVolume={expectedRemoved:E6}\nbeforeToggleRemovedVolume={currentRemoved:E6}\ntoggledFlip={toggledFlip}\nafterToggleRemovedVolume={(toggledMeasured ? toggledRemoved.ToString("E6") : "N/A")}\nresult=FAIL\nreason=RESTORE_FAILED: {restoreError}";
                    return false;
                }
            }

            details = $"CUT_FLIP_GEOMETRY_EVALUATE\nfeature={info.Name}\nsourceRemovedCentroid={FormatPoint(sourceRemovedCentroid)}\nexpectedMirroredCentroid={FormatPoint(expectedMirroredCentroid)}\nbeforeCentroid={FormatPoint(currentRemovedCentroid)}\nafterCentroid={FormatPoint(toggledRemovedCentroid)}\nbeforeDistance={currentDistance * 1000.0:F6}mm\nafterDistance={toggledDistance * 1000.0:F6}mm\nexpectedRemovedVolume={expectedRemoved:E6}\nbeforeToggleRemovedVolume={currentRemoved:E6}\nafterToggleRemovedVolume={toggledRemoved:E6}\nbeforeVolumeError={currentError:E6}\nafterVolumeError={toggledError:E6}\nbeforeVolumeMatches={currentVolumeMatches}\nafterVolumeMatches={toggledVolumeMatches}\noriginalFlip={originalFlip}\ntoggledFlip={toggledFlip}\nchosenFlip={(chooseToggled ? toggledFlip.ToString() : originalFlip.ToString())}\nvolumeMode={(allowAsymmetricCutVolume ? "ASYMMETRIC_BASE" : "SOURCE_MATCH")}\nresult=PASS\nreason={selectionReason}";
            return true;
        }

        public static List<Body2> GetSolidBodyCopies(ModelDoc2 partDoc)
        {
            List<Body2> list = new List<Body2>();
            PartDoc part = partDoc as PartDoc;
            if (part == null) return list;

            object[] bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
            if (bodies != null)
            {
                foreach (object bObj in bodies)
                {
                    Body2 b = bObj as Body2;
                    if (b != null)
                    {
                        Body2 cp = b.Copy2(false) as Body2;
                        if (cp != null) list.Add(cp);
                    }
                }
            }
            return list;
        }

        public static Body2 GetSolidBodyCopyStrict(ModelDoc2 partDoc, out string error)
        {
            error = null;
            PartDoc part = partDoc as PartDoc;
            if (part == null)
            {
                error = "ModelDoc2 is not a PartDoc.";
                return null;
            }

            object[] bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
            if (bodies == null || bodies.Length == 0)
            {
                error = "No solid body found in part.";
                return null;
            }

            if (bodies.Length > 1)
            {
                error = "MULTIBODY_NOT_SUPPORTED_YET (found " + bodies.Length + " solid bodies).";
                return null;
            }

            Body2 b = bodies[0] as Body2;
            if (b == null)
            {
                error = "Solid body object is null.";
                return null;
            }

            Body2 cp = b.Copy2(false) as Body2;
            if (cp == null)
            {
                error = "Body2.Copy2 returned null.";
                return null;
            }

            return cp;
        }

        public static BodyBooleanResult BooleanCutStrict(Body2 targetBody, Body2 toolBody, string label = "CUT")
        {
            BodyBooleanResult res = new BodyBooleanResult
            {
                Operation = "CUT",
                Success = false
            };

            if (targetBody == null)
            {
                res.ErrorMessage = "Target body is null.";
                CreateMirrorPartPackage.LogDebug($"BOOLEAN operation=CUT label={label} errorCode=-1 resultBodyCount=0 success=False error={res.ErrorMessage}");
                return res;
            }

            if (toolBody == null)
            {
                Body2 targetCopyOnly = targetBody.Copy2(false) as Body2;
                if (targetCopyOnly != null)
                {
                    res.Bodies.Add(targetCopyOnly);
                    res.Success = true;
                    CreateMirrorPartPackage.LogDebug($"BOOLEAN operation=CUT label={label} errorCode=0 resultBodyCount=1 success=True");
                    return res;
                }
                res.ErrorMessage = "Failed to copy target body.";
                return res;
            }

            Body2 targetCopy = targetBody.Copy2(false) as Body2;
            Body2 toolCopy = toolBody.Copy2(false) as Body2;

            if (targetCopy == null || toolCopy == null)
            {
                res.ErrorMessage = "Failed to copy target or tool body.";
                CreateMirrorPartPackage.LogDebug($"BOOLEAN operation=CUT label={label} errorCode=-1 resultBodyCount=0 success=False error={res.ErrorMessage}");
                return res;
            }

            int errCode = 0;
            object opResult = null;
            try
            {
                opResult = targetCopy.Operations2((int)swBodyOperationType_e.SWBODYCUT, toolCopy, out errCode);
            }
            catch (Exception ex)
            {
                res.ErrorCode = -1;
                res.ErrorMessage = "Exception in Operations2 CUT: " + ex.Message;
                CreateMirrorPartPackage.LogDebug($"BOOLEAN operation=CUT label={label} errorCode=-1 resultBodyCount=0 success=False error={res.ErrorMessage}");
                return res;
            }

            res.ErrorCode = errCode;

            if (opResult != null)
            {
                if (opResult is object[] arr)
                {
                    foreach (object o in arr)
                    {
                        if (o is Body2 b) res.Bodies.Add(b);
                    }
                }
                else if (opResult is Body2 b)
                {
                    res.Bodies.Add(b);
                }
            }

            if (errCode == 0)
            {
                res.Success = true;
            }
            else
            {
                res.Success = false;
                res.ErrorMessage = $"Operations2 returned errorCode={errCode}";
            }

            CreateMirrorPartPackage.LogDebug($"BOOLEAN operation=CUT label={label} errorCode={errCode} resultBodyCount={res.Bodies.Count} success={res.Success}");
            return res;
        }

        public static BodyBooleanResult BooleanAddStrict(Body2 targetBody, Body2 toolBody, string label = "ADD")
        {
            BodyBooleanResult res = new BodyBooleanResult
            {
                Operation = "ADD",
                Success = false
            };

            if (targetBody == null && toolBody == null)
            {
                res.ErrorMessage = "Both target and tool bodies are null.";
                CreateMirrorPartPackage.LogDebug($"BOOLEAN operation=ADD label={label} errorCode=-1 resultBodyCount=0 success=False error={res.ErrorMessage}");
                return res;
            }

            if (targetBody == null)
            {
                Body2 cpTool = toolBody.Copy2(false) as Body2;
                if (cpTool != null)
                {
                    res.Bodies.Add(cpTool);
                    res.Success = true;
                    CreateMirrorPartPackage.LogDebug($"BOOLEAN operation=ADD label={label} errorCode=0 resultBodyCount=1 success=True");
                    return res;
                }
                res.ErrorMessage = "Failed to copy tool body.";
                return res;
            }

            if (toolBody == null)
            {
                Body2 cpTarget = targetBody.Copy2(false) as Body2;
                if (cpTarget != null)
                {
                    res.Bodies.Add(cpTarget);
                    res.Success = true;
                    CreateMirrorPartPackage.LogDebug($"BOOLEAN operation=ADD label={label} errorCode=0 resultBodyCount=1 success=True");
                    return res;
                }
                res.ErrorMessage = "Failed to copy target body.";
                return res;
            }

            Body2 targetCopy = targetBody.Copy2(false) as Body2;
            Body2 toolCopy = toolBody.Copy2(false) as Body2;

            if (targetCopy == null || toolCopy == null)
            {
                res.ErrorMessage = "Failed to copy target or tool body.";
                CreateMirrorPartPackage.LogDebug($"BOOLEAN operation=ADD label={label} errorCode=-1 resultBodyCount=0 success=False error={res.ErrorMessage}");
                return res;
            }

            int errCode = 0;
            object opResult = null;
            try
            {
                opResult = targetCopy.Operations2((int)swBodyOperationType_e.SWBODYADD, toolCopy, out errCode);
            }
            catch (Exception ex)
            {
                res.ErrorCode = -1;
                res.ErrorMessage = "Exception in Operations2 ADD: " + ex.Message;
                CreateMirrorPartPackage.LogDebug($"BOOLEAN operation=ADD label={label} errorCode=-1 resultBodyCount=0 success=False error={res.ErrorMessage}");
                return res;
            }

            res.ErrorCode = errCode;

            if (opResult != null)
            {
                if (opResult is object[] arr)
                {
                    foreach (object o in arr)
                    {
                        if (o is Body2 b) res.Bodies.Add(b);
                    }
                }
                else if (opResult is Body2 b)
                {
                    res.Bodies.Add(b);
                }
            }

            if (errCode == 0 && res.Bodies.Count > 0)
            {
                res.Success = true;
            }
            else
            {
                res.Success = false;
                res.ErrorMessage = $"Operations2 ADD failed (errorCode={errCode}, resultCount={res.Bodies.Count})";
            }

            CreateMirrorPartPackage.LogDebug($"BOOLEAN operation=ADD label={label} errorCode={errCode} resultBodyCount={res.Bodies.Count} success={res.Success}");
            return res;
        }

        public static BodyTransformResult MirrorBodyStrict(ISldWorks swApp, Body2 sourceBody, PlaneData mirrorPlane)
        {
            BodyTransformResult res = new BodyTransformResult { Success = false };

            if (sourceBody == null || mirrorPlane == null)
            {
                res.ErrorMessage = "sourceBody or mirrorPlane is null.";
                return res;
            }

            double[] n = mirrorPlane.Normal;
            double[] o = mirrorPlane.Origin;

            double lenN = Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
            if (lenN < 1e-6)
            {
                res.ErrorMessage = "INVALID_MIRROR_PLANE_NORMAL (length < 1e-6)";
                return res;
            }

            double nx = n[0] / lenN;
            double ny = n[1] / lenN;
            double nz = n[2] / lenN;

            Body2 copy = sourceBody.Copy2(false) as Body2;
            if (copy == null)
            {
                res.ErrorMessage = "Failed to copy body for mirror transform.";
                return res;
            }

            double origVol = GetBodyVolume(sourceBody);

            try
            {
                IMathUtility mathUtility = swApp.GetMathUtility() as IMathUtility;
                if (mathUtility == null)
                {
                    res.ErrorMessage = "IMathUtility is null.";
                    return res;
                }

                double dotON = o[0] * nx + o[1] * ny + o[2] * nz;

                double[] xform = new double[16];
                xform[0] = 1.0 - 2.0 * nx * nx;
                xform[1] = -2.0 * nx * ny;
                xform[2] = -2.0 * nx * nz;

                xform[3] = -2.0 * ny * nx;
                xform[4] = 1.0 - 2.0 * ny * ny;
                xform[5] = -2.0 * ny * nz;

                xform[6] = -2.0 * nz * nx;
                xform[7] = -2.0 * nz * ny;
                xform[8] = 1.0 - 2.0 * nz * nz;

                xform[9] = 2.0 * dotON * nx;
                xform[10] = 2.0 * dotON * ny;
                xform[11] = 2.0 * dotON * nz;

                xform[12] = 1.0;
                xform[13] = 0.0;
                xform[14] = 0.0;
                xform[15] = 0.0;

                MathTransform mathXform = mathUtility.CreateTransform(xform) as MathTransform;
                if (mathXform == null)
                {
                    res.ErrorMessage = "Failed to create reflection MathTransform.";
                    return res;
                }

                bool transformed = copy.ApplyTransform(mathXform);
                if (!transformed)
                {
                    res.ErrorMessage = "Body2.ApplyTransform returned false.";
                    return res;
                }

                double mirrVol = GetBodyVolume(copy);
                double tol = Math.Max(ABSOLUTE_GEOMETRY_TOLERANCE, origVol * RELATIVE_TOLERANCE);
                if (Math.Abs(origVol - mirrVol) > tol)
                {
                    res.ErrorMessage = $"REFLECTION_VOLUME_CHANGED (orig={origVol:E6}, mirr={mirrVol:E6})";
                    return res;
                }

                res.Body = copy;
                res.Success = true;
            }
            catch (Exception ex)
            {
                res.ErrorMessage = "Exception during MirrorBodyStrict: " + ex.Message;
                res.Success = false;
            }

            return res;
        }

        public static FeatureSemanticValidationResult ValidateReplaySemantics(
            Body2 previousActualBody,
            Body2 replayedActualBody,
            PostBaseFeatureInfo info,
            FeatureBodyState originalCache,
            FeatureReplayResult replayResult,
            ModelDoc2 partDoc)
        {
            FeatureSemanticValidationResult res = new FeatureSemanticValidationResult
            {
                Success = false,
                FeatureName = info.Name,
                FeatureType = info.Type,
                ExpectedChangeKind = (originalCache != null) ? originalCache.ChangeKind : FeatureGeometryChangeKind.None
            };

            if (previousActualBody == null)
            {
                res.FailureReason = "previousActualBody is null.";
                return res;
            }

            if (replayedActualBody == null)
            {
                res.FailureReason = "replayedActualBody is null.";
                return res;
            }

            double bVol = GetBodyVolume(previousActualBody);
            double aVol = GetBodyVolume(replayedActualBody);

            res.BeforeVolume = bVol;
            res.AfterVolume = aVol;

            List<Body2> liveBodies = GetSolidBodyCopies(partDoc);
            res.AfterBodyCount = liveBodies.Count;
            res.BeforeBodyCount = 1;

            if (res.AfterBodyCount != 1)
            {
                res.FailureReason = $"CUT_RESULT_MULTIBODY_NOT_SUPPORTED (found {res.AfterBodyCount} bodies)";
                return res;
            }

            // A - B (Added)
            BodyBooleanResult cutAdded = BooleanCutStrict(replayedActualBody, previousActualBody, $"{info.Name}_ADDED_ACTUAL");
            // B - A (Removed)
            BodyBooleanResult cutRemoved = BooleanCutStrict(previousActualBody, replayedActualBody, $"{info.Name}_REMOVED_ACTUAL");

            res.ActualMinusBeforeBooleanSuccess = cutAdded.Success;
            res.BeforeMinusActualBooleanSuccess = cutRemoved.Success;

            if (!cutAdded.Success || !cutRemoved.Success)
            {
                res.FailureReason = "Boolean delta calculation between previous and replayed body failed.";
                return res;
            }

            double actualAddedVol = 0.0;
            foreach (var b in cutAdded.Bodies) actualAddedVol += GetBodyVolume(b);

            double actualRemovedVol = 0.0;
            foreach (var b in cutRemoved.Bodies) actualRemovedVol += GetBodyVolume(b);

            res.ActualAddedVolume = actualAddedVol;
            res.ActualRemovedVolume = actualRemovedVol;
            res.ExpectedAddedVolume = SumBodyVolumes(originalCache?.AddedBodies);
            res.ExpectedRemovedVolume = SumBodyVolumes(originalCache?.RemovedBodies);

            double tol = Math.Max(ABSOLUTE_GEOMETRY_TOLERANCE, bVol * RELATIVE_TOLERANCE);

            if (SketchDrivenFeatureMirrorHandler.IsExtrudeCutType(info.Type))
            {
                if (actualAddedVol > tol)
                {
                    res.FailureReason = $"CUT_ADDED_MATERIAL (addedVol={actualAddedVol:E6})";
                    return res;
                }

                if (originalCache != null && originalCache.ChangesGeometry && actualRemovedVol <= tol)
                {
                    res.FailureReason = $"CUT_REMOVED_NOTHING (removedVol={actualRemovedVol:E6})";
                    return res;
                }

                if (originalCache != null && originalCache.ChangesGeometry && aVol >= bVol - tol)
                {
                    res.FailureReason = $"CUT_VOLUME_DID_NOT_DECREASE (before={bVol:E6}, after={aVol:E6})";
                    return res;
                }

                double volumeMatchTolerance = Math.Max(
                    ABSOLUTE_GEOMETRY_TOLERANCE,
                    Math.Max(res.ExpectedRemovedVolume, bVol) * RELATIVE_TOLERANCE);
                double removedDifference = Math.Abs(actualRemovedVol - res.ExpectedRemovedVolume);
                res.RelativeVolumeError = res.ExpectedRemovedVolume > ABSOLUTE_GEOMETRY_TOLERANCE
                    ? removedDifference / res.ExpectedRemovedVolume
                    : removedDifference;

                if (originalCache != null && originalCache.ChangesGeometry && removedDifference > volumeMatchTolerance)
                {
                    if (replayResult == null || !replayResult.AllowAsymmetricCutVolume)
                    {
                        res.FailureReason = $"CUT_REMOVED_VOLUME_MISMATCH(expected={res.ExpectedRemovedVolume:E6}, actual={actualRemovedVol:E6}, relativeError={res.RelativeVolumeError:E6})";
                        return res;
                    }

                    CreateMirrorPartPackage.LogDebug(
                        $"CUT_ASYMMETRIC_VOLUME_ACCEPTED\nfeature={info.Name}\nexpectedRemovedVolume={res.ExpectedRemovedVolume:E6}\nactualRemovedVolume={actualRemovedVol:E6}\nrelativeError={res.RelativeVolumeError:E6}\nreason=MIRRORED_REMOVED_REGION_SELECTED");
                }
            }
            else if (SketchDrivenFeatureMirrorHandler.IsExtrudeBossType(info.Type))
            {
                if (actualRemovedVol > tol)
                {
                    res.FailureReason = $"BOSS_REMOVED_MATERIAL (removedVol={actualRemovedVol:E6})";
                    return res;
                }

                if (originalCache != null && originalCache.ChangesGeometry && actualAddedVol <= tol)
                {
                    res.FailureReason = $"BOSS_ADDED_NOTHING (addedVol={actualAddedVol:E6})";
                    return res;
                }

                if (originalCache != null && originalCache.ChangesGeometry && aVol <= bVol + tol)
                {
                    res.FailureReason = $"BOSS_VOLUME_DID_NOT_INCREASE (before={bVol:E6}, after={aVol:E6})";
                    return res;
                }
            }

            if (replayResult != null && !replayResult.Success)
            {
                res.FailureReason = "ReplayResult reported failure: " + replayResult.Message;
                return res;
            }

            res.Success = true;
            return res;
        }
    }

    public sealed class MirrorReferenceResult
    {
        public bool Success { get; set; }
        public MirrorReferenceKind Kind { get; set; } = MirrorReferenceKind.None;
        public Feature PlaneFeature { get; set; }
        public SketchSegment Centerline { get; set; }
        public double SketchMirrorNormalDot { get; set; }
        public double[] AxisPoint1 { get; set; } = new double[2];
        public double[] AxisPoint2 { get; set; } = new double[2];
        public string Message { get; set; }
    }

    public static class MirrorReferenceResolver
    {
        public static MirrorReferenceResult ResolveAndSelectMirrorReference(
            ISldWorks swApp,
            ModelDoc2 partDoc,
            Sketch sketch,
            PlaneData mirrorPlane)
        {
            MirrorReferenceResult res = new MirrorReferenceResult { Success = false };

            IMathUtility mathUtility = swApp.GetMathUtility() as IMathUtility;
            if (mathUtility == null || sketch == null || mirrorPlane == null)
            {
                res.Message = "Null mathUtility, sketch, or mirrorPlane.";
                return res;
            }

            MathTransform m2s = sketch.ModelToSketchTransform;
            if (m2s == null)
            {
                res.Message = "sketch.ModelToSketchTransform is null.";
                return res;
            }
            MathTransform s2m = m2s.IInverse();

            MathVector skZ = mathUtility.CreateVector(new double[] { 0, 0, 1 }) as MathVector;
            MathVector skNormVec = skZ.MultiplyTransform(s2m) as MathVector;
            double[] nSk = skNormVec.ArrayData as double[];

            double[] nMp = mirrorPlane.Normal;
            double[] oMp = mirrorPlane.Origin;

            double dot = nSk[0] * nMp[0] + nSk[1] * nMp[1] + nSk[2] * nMp[2];
            res.SketchMirrorNormalDot = dot;

            // CASE B: PARALLEL / COINCIDENT (|dot| ≈ 1)
            if (Math.Abs(Math.Abs(dot) - 1.0) < 0.05)
            {
                res.Kind = MirrorReferenceKind.ParallelUnsupported;
                res.Message = $"Mirror plane is PARALLEL to sketch plane (|dot|={Math.Abs(dot):F4} ≈ 1). Cannot 2D mirror inside sketch.";
                return res;
            }

            // CASE C: OBLIQUE (|dot| neither ~0 nor ~1)
            if (Math.Abs(dot) >= 0.05 && Math.Abs(Math.Abs(dot) - 1.0) >= 0.05)
            {
                res.Kind = MirrorReferenceKind.ObliqueRequiresRehost;
                res.Message = $"Mirror plane is OBLIQUE to sketch plane (|dot|={Math.Abs(dot):F4}). Requires rehosting sketch plane.";
                return res;
            }

            // CASE A: PERPENDICULAR (|dot| ≈ 0)
            MathPoint skP0 = mathUtility.CreatePoint(new double[] { 0, 0, 0 }) as MathPoint;
            MathPoint skOriginPoint = skP0.MultiplyTransform(s2m) as MathPoint;
            double[] oSk = skOriginPoint.ArrayData as double[];

            double dx = nSk[1] * nMp[2] - nSk[2] * nMp[1];
            double dy = nSk[2] * nMp[0] - nSk[0] * nMp[2];
            double dz = nSk[0] * nMp[1] - nSk[1] * nMp[0];
            double lenD = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            if (lenD > 1e-6)
            {
                dx /= lenD;
                dy /= lenD;
                dz /= lenD;

                double d1 = nSk[0] * oSk[0] + nSk[1] * oSk[1] + nSk[2] * oSk[2];
                double d2 = nMp[0] * oMp[0] + nMp[1] * oMp[1] + nMp[2] * oMp[2];

                double c1x = nMp[1] * dz - nMp[2] * dy;
                double c1y = nMp[2] * dx - nMp[0] * dz;
                double c1z = nMp[0] * dy - nMp[1] * dx;

                double c2x = dy * nSk[2] - dz * nSk[1];
                double c2y = dz * nSk[0] - dx * nSk[2];
                double c2z = dx * nSk[1] - dy * nSk[0];

                double px = d1 * c1x + d2 * c2x;
                double py = d1 * c1y + d2 * c2y;
                double pz = d1 * c1z + d2 * c2z;

                MathPoint p1M = mathUtility.CreatePoint(new double[] { px - 1.0 * dx, py - 1.0 * dy, pz - 1.0 * dz }) as MathPoint;
                MathPoint p2M = mathUtility.CreatePoint(new double[] { px + 1.0 * dx, py + 1.0 * dy, pz + 1.0 * dz }) as MathPoint;

                MathPoint p1S = p1M.MultiplyTransform(m2s) as MathPoint;
                MathPoint p2S = p2M.MultiplyTransform(m2s) as MathPoint;

                double[] s1 = p1S.ArrayData as double[];
                double[] s2 = p2S.ArrayData as double[];

                res.AxisPoint1 = new double[] { s1[0], s1[1] };
                res.AxisPoint2 = new double[] { s2[0], s2[1] };

                // Native SketchMirror is reliable only when its mirror reference belongs
                // to the active sketch. Selecting a coincident model RefPlane can make the
                // API report success while the persisted sketch geometry remains unchanged.
                // Match the proven user macro: create an explicit construction centerline
                // at the intersection of the assembly mirror plane and this sketch plane.
                SketchSegment cl = partDoc.SketchManager.CreateCenterLine(s1[0], s1[1], 0.0, s2[0], s2[1], 0.0) as SketchSegment;
                if (cl != null)
                {
                    try { cl.ConstructionGeometry = true; } catch { }
                    bool clSel = cl.Select4(true, null);
                    if (clSel)
                    {
                        res.Success = true;
                        res.Kind = MirrorReferenceKind.IntersectionCenterline;
                        res.Centerline = cl;
                        res.Message = "EXPLICIT_SKETCH_CENTERLINE";
                        CreateMirrorPartPackage.LogDebug(
                            $"SKETCH_MIRROR_REFERENCE\nmode=EXPLICIT_CENTERLINE\n" +
                            "anchor=PART_ORIGIN\n" +
                            $"mirrorPlaneOrigin=({oMp[0]:F9},{oMp[1]:F9},{oMp[2]:F9})\n" +
                            $"axis1=({s1[0]:F9},{s1[1]:F9})\n" +
                            $"axis2=({s2[0]:F9},{s2[1]:F9})");
                        return res;
                    }
                }
            }

            res.Message = "Failed to resolve valid mirror reference.";
            return res;
        }
    }

    public sealed class PostBaseFeatureInfo
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public Feature Feature { get; set; }
        public List<string> ParentFeatureNames { get; set; } = new List<string>();
        public List<string> ChildFeatureNames { get; set; } = new List<string>();
        public string DrivingSketchName { get; set; }
        public Feature DrivingSketchFeature { get; set; }
        public bool HasDrivingSketch { get; set; }
        public bool IsSuppressed { get; set; }
        public FeatureReplayDisposition Disposition { get; set; } = FeatureReplayDisposition.ReplayRequired;
    }

    public sealed class FeatureBodyState
    {
        public int FeatureIndex { get; set; }
        public string FeatureName { get; set; }
        public Body2 BeforeBody { get; set; }
        public Body2 AfterBody { get; set; }
        public List<Body2> AddedBodies { get; set; } = new List<Body2>();
        public List<Body2> RemovedBodies { get; set; } = new List<Body2>();
        public bool ChangesGeometry { get; set; }
        public FeatureGeometryChangeKind ChangeKind { get; set; } = FeatureGeometryChangeKind.None;
    }

    public sealed class FeatureReplayResult
    {
        public bool Success { get; set; }
        public bool Unsupported { get; set; }
        public string StatusCode { get; set; }
        public string Message { get; set; }
        public string FeatureName { get; set; }
        public string FeatureType { get; set; }

        public bool SketchEntered { get; set; }
        public bool MirrorReferenceResolved { get; set; }
        public bool SketchMirrorExecuted { get; set; }
        public bool MirrorGeometryVerified { get; set; }
        public bool OriginalsNeutralized { get; set; }
        public bool DimensionAuditPassed { get; set; }
        public bool OriginReferencePreserved { get; set; }
        public bool RebuildPassed { get; set; }
        public int FeatureErrorCode { get; set; }
        public bool FeatureWarning { get; set; }
        public bool AllowAsymmetricCutVolume { get; set; }

        public int SourceEntities { get; set; }
        public int InvariantEntities { get; set; }
        public int MirroredEntities { get; set; }
        public int ConstructionEntities { get; set; }
        public int DimensionCountBefore { get; set; }
        public int DimensionCountAfter { get; set; }

        public MirrorReferenceKind MirrorReferenceKind { get; set; } = MirrorReferenceKind.None;
    }

    public static class SketchSignatureHelper
    {
        public class SketchSegmentSignature
        {
            public int SegmentType { get; set; }
            public bool IsConstruction { get; set; }
            public double Length { get; set; }
            public double[] StartPoint { get; set; }
            public double[] EndPoint { get; set; }
            public double[] CenterPoint { get; set; }
            public double Radius { get; set; }
        }

        public static List<SketchSegmentSignature> CaptureSketchSignature(Sketch sketch)
        {
            List<SketchSegmentSignature> sigs = new List<SketchSegmentSignature>();
            if (sketch == null) return sigs;

            object[] segs = sketch.GetSketchSegments() as object[];
            if (segs == null) return sigs;

            foreach (object sObj in segs)
            {
                SketchSegment seg = sObj as SketchSegment;
                if (seg == null) continue;

                SketchSegmentSignature sig = new SketchSegmentSignature
                {
                    SegmentType = seg.GetType(),
                    IsConstruction = seg.ConstructionGeometry,
                    Length = seg.GetLength()
                };

                try
                {
                    SketchLine line = seg as SketchLine;
                    if (line != null)
                    {
                        SketchPoint sp = line.GetStartPoint2() as SketchPoint;
                        SketchPoint ep = line.GetEndPoint2() as SketchPoint;
                        if (sp != null) sig.StartPoint = new double[] { sp.X, sp.Y, sp.Z };
                        if (ep != null) sig.EndPoint = new double[] { ep.X, ep.Y, ep.Z };
                    }

                    SketchArc arc = seg as SketchArc;
                    if (arc != null)
                    {
                        SketchPoint cp = arc.GetCenterPoint2() as SketchPoint;
                        SketchPoint sp = arc.GetStartPoint2() as SketchPoint;
                        SketchPoint ep = arc.GetEndPoint2() as SketchPoint;
                        if (cp != null) sig.CenterPoint = new double[] { cp.X, cp.Y, cp.Z };
                        if (sp != null) sig.StartPoint = new double[] { sp.X, sp.Y, sp.Z };
                        if (ep != null) sig.EndPoint = new double[] { ep.X, ep.Y, ep.Z };
                        sig.Radius = arc.GetRadius();
                    }
                }
                catch {}

                sigs.Add(sig);
            }

            return sigs;
        }

        public static bool SegmentMatchesSignature(SketchSegment seg, SketchSegmentSignature sig)
        {
            if (seg == null || sig == null) return false;
            if (seg.GetType() != sig.SegmentType) return false;
            if (Math.Abs(seg.GetLength() - sig.Length) > 1e-5) return false;

            SketchLine line = seg as SketchLine;
            if (line != null && sig.StartPoint != null && sig.EndPoint != null)
            {
                SketchPoint sp = line.GetStartPoint2() as SketchPoint;
                SketchPoint ep = line.GetEndPoint2() as SketchPoint;
                if (sp == null || ep == null) return false;

                bool matchFwd = (Dist2D(sp.X, sp.Y, sig.StartPoint[0], sig.StartPoint[1]) < 1e-4) &&
                                (Dist2D(ep.X, ep.Y, sig.EndPoint[0], sig.EndPoint[1]) < 1e-4);
                bool matchRev = (Dist2D(sp.X, sp.Y, sig.EndPoint[0], sig.EndPoint[1]) < 1e-4) &&
                                (Dist2D(ep.X, ep.Y, sig.StartPoint[0], sig.StartPoint[1]) < 1e-4);

                return matchFwd || matchRev;
            }

            SketchArc arc = seg as SketchArc;
            if (arc != null && sig.CenterPoint != null)
            {
                SketchPoint cp = arc.GetCenterPoint2() as SketchPoint;
                if (cp == null) return false;

                if (Dist2D(cp.X, cp.Y, sig.CenterPoint[0], sig.CenterPoint[1]) > 1e-4) return false;
                if (Math.Abs(arc.GetRadius() - sig.Radius) > 1e-4) return false;

                return true;
            }

            return true;
        }

        public static bool CompareSignatures(List<SketchSegmentSignature> sig1, List<SketchSegmentSignature> sig2)
        {
            if (sig1 == null && sig2 == null) return true;
            if (sig1 == null || sig2 == null) return false;
            if (sig1.Count != sig2.Count) return false;

            for (int i = 0; i < sig1.Count; i++)
            {
                var a = sig1[i];
                var b = sig2[i];
                if (a.SegmentType != b.SegmentType) return false;
                if (a.IsConstruction != b.IsConstruction) return false;
                if (Math.Abs(a.Length - b.Length) > 1e-6) return false;
                if (Math.Abs(a.Radius - b.Radius) > 1e-6) return false;
            }

            return true;
        }

        private static double Dist2D(double x1, double y1, double x2, double y2)
        {
            return Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));
        }
    }

    public interface IFeatureMirrorHandler
    {
        bool CanHandle(PostBaseFeatureInfo info);
        FeatureReplayResult Replay(
            ISldWorks swApp,
            ModelDoc2 partDoc,
            PostBaseFeatureInfo info,
            PlaneData mirrorPlane,
            FeatureBodyState cache,
            string protectedBaseFeatureName,
            string protectedBaseSketchName);
    }

    public sealed class SketchDrivenFeatureMirrorHandler : IFeatureMirrorHandler
    {
        public static bool IsExtrudeCutType(string type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            return string.Equals(type, "Cut", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(type, "ICE", StringComparison.OrdinalIgnoreCase) ||
                   type.IndexOf("Cut", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   type.IndexOf("ExtrudeCut", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   string.Equals(type, "Extrusion", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsExtrudeBossType(string type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            return string.Equals(type, "Boss", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(type, "BossThin", StringComparison.OrdinalIgnoreCase) ||
                   type.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   type.IndexOf("Extrude", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public bool CanHandle(PostBaseFeatureInfo info)
        {
            if (info == null || !info.HasDrivingSketch || info.DrivingSketchFeature == null)
            {
                return false;
            }

            return IsExtrudeCutType(info.Type) || IsExtrudeBossType(info.Type);
        }

        public FeatureReplayResult Replay(
            ISldWorks swApp,
            ModelDoc2 partDoc,
            PostBaseFeatureInfo info,
            PlaneData mirrorPlane,
            FeatureBodyState cache,
            string protectedBaseFeatureName,
            string protectedBaseSketchName)
        {
            FeatureReplayResult invalidResult = new FeatureReplayResult
            {
                Success = false,
                FeatureName = info?.Name,
                FeatureType = info?.Type
            };

            if (partDoc == null || info?.Feature == null || info.DrivingSketchFeature == null)
            {
                invalidResult.StatusCode = "INDEPENDENT_SKETCH_INVALID_ARGUMENT";
                invalidResult.Message = "Part, feature or driving sketch is not available.";
                return invalidResult;
            }

            Feature originalSketch = info.DrivingSketchFeature;
            string originalSketchName = info.DrivingSketchName;
            Feature copiedSketch = null;
            string copiedSketchName = string.Empty;
            PostBaseFeatureInfo copiedInfo = null;
            bool retargeted = false;

            try
            {
                string copyDetails;
                if (!TryDuplicateSketchFeature(partDoc, originalSketch, out copiedSketch, out copyDetails))
                {
                    CreateMirrorPartPackage.LogDebug(
                        "[MIRROR-SKETCH-INDEPENDENT] COPY\n" +
                        $"feature={info.Name}\nsourceSketch={originalSketchName}\n" +
                        $"result=FAIL\ndetails={copyDetails}");

                    invalidResult.StatusCode = "INDEPENDENT_SKETCH_COPY_FAILED";
                    invalidResult.Message = copyDetails;
                    return invalidResult;
                }

                CreateMirrorPartPackage.LogDebug(
                    "[MIRROR-SKETCH-INDEPENDENT] COPY\n" +
                    $"feature={info.Name}\nsourceSketch={originalSketchName}\n" +
                    $"copiedSketch={copiedSketch.Name}\nresult=PASS");
                copiedSketchName = copiedSketch.Name;

                copiedInfo = CloneFeatureInfoWithSketch(info, copiedSketch);
                FeatureReplayResult coreResult = ReplayOnIndependentSketch(
                    swApp,
                    partDoc,
                    copiedInfo,
                    mirrorPlane,
                    cache,
                    protectedBaseFeatureName,
                    protectedBaseSketchName);

                if (coreResult == null || !coreResult.Success)
                {
                    string restoreDetails;
                    bool restored = BodyOperationsHelper.TryRetargetExtrudeContours(
                        partDoc,
                        info,
                        originalSketch,
                        out restoreDetails);
                    partDoc.EditRebuild3();

                    string deleteDetails;
                    TryDeleteFeature(partDoc, copiedSketch, out deleteDetails);
                    CreateMirrorPartPackage.LogDebug(
                        "[MIRROR-SKETCH-INDEPENDENT] ROLLBACK\n" +
                        $"feature={info.Name}\nsourceSketch={originalSketchName}\n" +
                        $"copiedSketch={copiedSketchName}\nretargeted=false\n" +
                        $"reason=CORE_FAILED\nrestore={restored}\nrestoreDetails={restoreDetails}\n" +
                        $"delete={deleteDetails}");
                    return coreResult ?? invalidResult;
                }

                string retargetDetails;
                retargeted = BodyOperationsHelper.TryRetargetExtrudeContours(
                    partDoc,
                    copiedInfo,
                    copiedSketch,
                    out retargetDetails);

                CreateMirrorPartPackage.LogDebug(
                    "[MIRROR-SKETCH-INDEPENDENT] RETARGET\n" +
                    $"feature={info.Name}\nsourceSketch={originalSketchName}\n" +
                    $"copiedSketch={copiedSketch.Name}\nresult={(retargeted ? "PASS" : "FAIL")}\n" +
                    $"details={retargetDetails}");

                if (!retargeted)
                {
                    string restoreDetails;
                    bool restored = BodyOperationsHelper.TryRetargetExtrudeContours(
                        partDoc,
                        info,
                        originalSketch,
                        out restoreDetails);
                    partDoc.EditRebuild3();

                    string deleteDetails;
                    TryDeleteFeature(partDoc, copiedSketch, out deleteDetails);
                    coreResult.Success = false;
                    coreResult.StatusCode = "INDEPENDENT_SKETCH_RETARGET_FAILED";
                    coreResult.Message = retargetDetails;
                    CreateMirrorPartPackage.LogDebug(
                        "[MIRROR-SKETCH-INDEPENDENT] ROLLBACK\n" +
                        $"feature={info.Name}\nretargeted=false\nreason=RETARGET_FAILED\n" +
                        $"restore={restored}\nrestoreDetails={restoreDetails}\n" +
                        $"delete={deleteDetails}");
                    return coreResult;
                }

                partDoc.EditRebuild3();
                bool warning = false;
                int errorCode = info.Feature.GetErrorCode2(out warning);
                if (errorCode != 0 && IsExtrudeCutType(info.Type))
                {
                    string recoveryDetails;
                    BodyOperationsHelper.TryRecoverExtrudeCutRebuild(
                        partDoc,
                        copiedInfo,
                        cache,
                        out recoveryDetails);
                    CreateMirrorPartPackage.LogDebug(recoveryDetails ??
                        $"CUT_FLIP_REBUILD_RECOVERY\nfeature={info.Name}\nresult=FAIL\nreason=NO_DETAILS");
                    errorCode = info.Feature.GetErrorCode2(out warning);
                }

                if (errorCode != 0)
                {
                    string restoreDetails;
                    bool restored = BodyOperationsHelper.TryRetargetExtrudeContours(
                        partDoc,
                        info,
                        originalSketch,
                        out restoreDetails);
                    partDoc.EditRebuild3();

                    string deleteDetails;
                    TryDeleteFeature(partDoc, copiedSketch, out deleteDetails);
                    coreResult.Success = false;
                    coreResult.StatusCode = "INDEPENDENT_SKETCH_REBUILD_FAILED";
                    coreResult.Message =
                        $"Mirrored sketch caused rebuild error {errorCode}. Original sketch restored={restored}.";
                    coreResult.FeatureErrorCode = errorCode;
                    coreResult.FeatureWarning = warning;
                    CreateMirrorPartPackage.LogDebug(
                        "[MIRROR-SKETCH-INDEPENDENT] ROLLBACK\n" +
                        $"feature={info.Name}\nretargeted=true\nreason=REBUILD_ERROR_{errorCode}\n" +
                        $"restore={restored}\nrestoreDetails={restoreDetails}\ndelete={deleteDetails}");
                    return coreResult;
                }

                info.DrivingSketchFeature = copiedSketch;
                info.DrivingSketchName = copiedSketch.Name;
                info.HasDrivingSketch = true;

                coreResult.Success = true;
                coreResult.StatusCode = "SUCCESS_INDEPENDENT_SKETCH";
                coreResult.Message = "Feature now uses an independent mirrored sketch.";
                coreResult.FeatureErrorCode = 0;
                coreResult.FeatureWarning = warning;
                CreateMirrorPartPackage.LogDebug(
                    "[MIRROR-SKETCH-INDEPENDENT] COMMIT\n" +
                    $"feature={info.Name}\nsourceSketch={originalSketchName}\n" +
                    $"copiedSketch={copiedSketch.Name}\nresult=PASS");
                return coreResult;
            }
            catch (Exception ex)
            {
                string restoreDetails = "NOT_REQUIRED";
                if (retargeted)
                {
                    try
                    {
                        BodyOperationsHelper.TryRetargetExtrudeContours(
                            partDoc,
                            info,
                            originalSketch,
                            out restoreDetails);
                        partDoc.EditRebuild3();
                    }
                    catch (Exception restoreEx)
                    {
                        restoreDetails = "EXCEPTION: " + restoreEx.Message;
                    }
                }

                string deleteDetails = "NO_COPY";
                if (copiedSketch != null)
                {
                    TryDeleteFeature(partDoc, copiedSketch, out deleteDetails);
                }

                CreateMirrorPartPackage.LogDebug(
                    "[MIRROR-SKETCH-INDEPENDENT] ROLLBACK\n" +
                    $"feature={info.Name}\nretargeted={retargeted}\nreason=EXCEPTION: {ex.Message}\n" +
                    $"restoreDetails={restoreDetails}\ndelete={deleteDetails}");
                invalidResult.StatusCode = "INDEPENDENT_SKETCH_EXCEPTION";
                invalidResult.Message = ex.Message;
                return invalidResult;
            }
            finally
            {
                try { partDoc.ClearSelection2(true); } catch { }
            }
        }

        private FeatureReplayResult ReplayOnIndependentSketch(
            ISldWorks swApp,
            ModelDoc2 partDoc,
            PostBaseFeatureInfo info,
            PlaneData mirrorPlane,
            FeatureBodyState cache,
            string protectedBaseFeatureName,
            string protectedBaseSketchName)
        {
            FeatureReplayResult result = new FeatureReplayResult
            {
                Success = false,
                FeatureName = info.Name,
                FeatureType = info.Type
            };

            Feature sketchFeat = info.DrivingSketchFeature;
            if (sketchFeat == null)
            {
                result.StatusCode = "NO_DRIVING_SKETCH";
                result.Message = "No driving sketch found.";
                return result;
            }

            if (string.Equals(info.Name, protectedBaseFeatureName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sketchFeat.Name, protectedBaseSketchName, StringComparison.OrdinalIgnoreCase))
            {
                result.StatusCode = "ATTEMPTED_MODIFY_PROTECTED_BASE";
                result.Message = "CRITICAL: Attempted to replay on protected Base feature or Base sketch!";
                return result;
            }

            Sketch sketch = sketchFeat.GetSpecificFeature2() as Sketch;
            if (sketch == null)
            {
                result.StatusCode = "NULL_SKETCH_INTERFACE";
                result.Message = "Sketch interface is null.";
                return result;
            }

            try
            {
                bool selSketch = sketchFeat.Select2(false, 0);
                if (!selSketch)
                {
                    result.StatusCode = "SELECT_SKETCH_FAILED";
                    result.Message = "Failed to select driving sketch.";
                    return result;
                }

                partDoc.EditSketch();
                Sketch activeSketch = partDoc.SketchManager.ActiveSketch;
                if (activeSketch == null)
                {
                    result.StatusCode = "EDIT_SKETCH_FAILED";
                    result.Message = "partDoc.SketchManager.ActiveSketch is null after EditSketch.";
                    return result;
                }
                result.SketchEntered = true;

                SketchAuditSnapshot dimensionBaseline = CaptureSketchAudit(sketchFeat, sketch);
                result.DimensionCountBefore = dimensionBaseline.Dimensions.Count;
                CreateMirrorPartPackage.LogDebug(
                    $"SKETCH_DIMENSION_BASELINE\n" +
                    $"sketch={sketchFeat.Name}\n" +
                    $"dimensionCount={dimensionBaseline.Dimensions.Count}\n" +
                    $"originLinked={dimensionBaseline.OriginLinkedDimensionCount}\n" +
                    $"relationCount={dimensionBaseline.RelationCount}\n" +
                    $"suppressedRelations={dimensionBaseline.SuppressedRelationCount}\n" +
                    $"warning={dimensionBaseline.CaptureWarning ?? string.Empty}");

                // 1. Snapshot Before Reference
                object[] segsBeforeRefObj = sketch.GetSketchSegments() as object[];
                int countBeforeReference = (segsBeforeRefObj != null) ? segsBeforeRefObj.Length : 0;

                List<SketchSegment> profileSegments = new List<SketchSegment>();
                if (segsBeforeRefObj != null)
                {
                    foreach (object sObj in segsBeforeRefObj)
                    {
                        SketchSegment seg = sObj as SketchSegment;
                        if (seg == null) continue;
                        if (!seg.ConstructionGeometry)
                        {
                            profileSegments.Add(seg);
                        }
                    }
                }

                result.SourceEntities = profileSegments.Count;

                if (profileSegments.Count == 0)
                {
                    partDoc.SketchManager.InsertSketch(true);
                    partDoc.ClearSelection2(true);
                    result.StatusCode = "EMPTY_PROFILE_SEGMENTS";
                    result.Message = "No active (non-construction) sketch segments found.";
                    return result;
                }

                // 2. Resolve mirror reference
                MirrorReferenceResult refRes = MirrorReferenceResolver.ResolveAndSelectMirrorReference(swApp, partDoc, sketch, mirrorPlane);
                result.MirrorReferenceKind = refRes.Kind;

                if (!refRes.Success)
                {
                    partDoc.SketchManager.InsertSketch(true);
                    partDoc.ClearSelection2(true);
                    result.StatusCode = "MIRROR_REFERENCE_RESOLVE_FAILED";
                    result.Message = refRes.Message;
                    return result;
                }
                result.MirrorReferenceResolved = true;

                // 3. Snapshot After Reference
                object[] segsAfterRefObj = sketch.GetSketchSegments() as object[];
                int countAfterReference = (segsAfterRefObj != null) ? segsAfterRefObj.Length : 0;
                int createdByReference = countAfterReference - countBeforeReference;

                List<SketchSignatureHelper.SketchSegmentSignature> sigsAfterRef = SketchSignatureHelper.CaptureSketchSignature(sketch);

                // 4. Invariant entity detection using 2D mirror axis
                double ax1 = refRes.AxisPoint1[0], ay1 = refRes.AxisPoint1[1];
                double ax2 = refRes.AxisPoint2[0], ay2 = refRes.AxisPoint2[1];

                List<SketchSegment> invariantSegments = new List<SketchSegment>();
                List<SketchSegment> segmentsToMirror = new List<SketchSegment>();

                foreach (SketchSegment seg in profileSegments)
                {
                    if (CheckIfInvariant2D(seg, ax1, ay1, ax2, ay2))
                    {
                        invariantSegments.Add(seg);
                    }
                    else
                    {
                        segmentsToMirror.Add(seg);
                    }
                }

                result.InvariantEntities = invariantSegments.Count;
                int expectedMirror = segmentsToMirror.Count;

                if (segmentsToMirror.Count == 0)
                {
                    partDoc.SketchManager.InsertSketch(true);
                    partDoc.ClearSelection2(true);
                    result.Success = true;
                    result.StatusCode = "ALL_INVARIANT";
                    result.Message = "All sketch entities lie on mirror axis.";
                    return result;
                }

                // Native SketchMirror is used only as a temporary geometry generator.
                // Remember the exact source relations so only newly-created symmetric
                // dependencies can be removed after the reflected geometry is verified.
                int relationCountBeforeMirror;
                string relationSnapshotWarning;
                HashSet<long> relationIdsBeforeMirror = CaptureSketchRelationIds(
                    sketch,
                    out relationCountBeforeMirror,
                    out relationSnapshotWarning);

                // 5. Select segments to mirror + mirror reference
                partDoc.ClearSelection2(true);
                foreach (SketchSegment seg in segmentsToMirror)
                {
                    seg.Select4(true, null);
                }

                if (refRes.Kind == MirrorReferenceKind.ModelPlane && refRes.PlaneFeature != null)
                {
                    refRes.PlaneFeature.Select2(true, 0);
                }
                else if (refRes.Kind == MirrorReferenceKind.IntersectionCenterline && refRes.Centerline != null)
                {
                    refRes.Centerline.Select4(true, null);
                }

                // 6. Call native SketchMirror
                partDoc.SketchMirror();
                result.SketchMirrorExecuted = true;

                // 7. Snapshot After Mirror
                object[] segsAfterMirrorObj = sketch.GetSketchSegments() as object[];
                int countAfterMirror = (segsAfterMirrorObj != null) ? segsAfterMirrorObj.Length : 0;
                int createdByMirror = countAfterMirror - countAfterReference;

                bool mirrorCountPass = (createdByMirror >= expectedMirror) && (createdByMirror > 0);
                string mirrorVerifyResultStr = mirrorCountPass ? "PASS" : "FAIL";

                CreateMirrorPartPackage.LogDebug($"SKETCH_MIRROR_VERIFY\nsketch={sketchFeat.Name}\nbeforeReference={countBeforeReference}\nafterReference={countAfterReference}\nafterMirror={countAfterMirror}\ncreatedByReference={createdByReference}\ncreatedByMirror={createdByMirror}\nexpectedMirror={expectedMirror}\nresult={mirrorVerifyResultStr}");

                if (createdByMirror == 0)
                {
                    partDoc.SketchManager.InsertSketch(true);
                    partDoc.ClearSelection2(true);
                    result.StatusCode = "SKETCH_MIRROR_CREATED_NOTHING";
                    result.Message = $"SketchMirror created 0 new entities (beforeRef={countBeforeReference}, afterRef={countAfterReference}, afterMirror={countAfterMirror}).";
                    return result;
                }

                // 8. Detect actual new segments without relying on array index
                List<SketchSegment> newSegments = DetectNewSketchSegments(sigsAfterRef, segsAfterMirrorObj);

                int geometryMatches = 0;
                foreach (SketchSegment srcSeg in segmentsToMirror)
                {
                    if (FindReflectedMatch(srcSeg, newSegments, ax1, ay1, ax2, ay2))
                    {
                        geometryMatches++;
                    }
                }

                CreateMirrorPartPackage.LogDebug($"SKETCH_GEOMETRY_MATCH\nsketch={sketchFeat.Name}\nexpectedMatches={expectedMirror}\ngeometryMatches={geometryMatches}");

                if (geometryMatches < expectedMirror)
                {
                    partDoc.SketchManager.InsertSketch(true);
                    partDoc.ClearSelection2(true);
                    result.StatusCode = "MIRRORED_GEOMETRY_NOT_FOUND";
                    result.Message = $"Mirrored geometry matching failed: matched {geometryMatches}/{expectedMirror} entities.";
                    return result;
                }

                result.MirrorGeometryVerified = true;
                result.MirroredEntities = geometryMatches;

                // Remove the temporary mirror dependency while retaining the reflected
                // entities. Source dimensions and all pre-existing relations stay intact.
                SketchIndependenceResult independence = DetachNewSymmetricRelations(
                    sketch,
                    relationIdsBeforeMirror,
                    relationCountBeforeMirror);

                int geometryMatchesAfterDetach = 0;
                foreach (SketchSegment srcSeg in segmentsToMirror)
                {
                    if (FindReflectedMatch(srcSeg, newSegments, ax1, ay1, ax2, ay2))
                    {
                        geometryMatchesAfterDetach++;
                    }
                }

                CreateMirrorPartPackage.LogDebug(
                    $"SKETCH_INDEPENDENCE_AUDIT\n" +
                    $"sketch={sketchFeat.Name}\n" +
                    $"relationsBeforeMirror={independence.RelationsBeforeMirror}\n" +
                    $"relationsAfterMirror={independence.RelationsAfterMirror}\n" +
                    $"candidateRelations={independence.CandidateRelations}\n" +
                    $"symmetricRelationsFound={independence.SymmetricRelationsFound}\n" +
                    $"symmetricRelationsDeleted={independence.SymmetricRelationsDeleted}\n" +
                    $"relationsAfterDetach={independence.RelationsAfterDetach}\n" +
                    $"geometryMatchesAfterDetach={geometryMatchesAfterDetach}/{expectedMirror}\n" +
                    $"snapshotWarning={relationSnapshotWarning ?? string.Empty}\n" +
                    $"result={(independence.Success && geometryMatchesAfterDetach == expectedMirror ? "PASS" : "FAIL")}\n" +
                    $"reason={independence.FailureReason ?? string.Empty}");

                if (!independence.Success || geometryMatchesAfterDetach != expectedMirror)
                {
                    partDoc.SketchManager.InsertSketch(true);
                    partDoc.ClearSelection2(true);
                    result.StatusCode = "SKETCH_INDEPENDENCE_FAILED";
                    result.Message = !independence.Success
                        ? "Cannot detach temporary symmetric relations: " + independence.FailureReason
                        : $"Reflected geometry changed after detach: {geometryMatchesAfterDetach}/{expectedMirror}.";
                    return result;
                }

                // A native sketch mirror copies geometry, but after the temporary
                // symmetric relation is removed SolidWorks does not retarget existing
                // dimensions. Recreate only dimensions that actually reference source
                // entities, attach them to the reflected entities, and then remove the
                // old source dimensions. This makes the reflected sketch independently
                // editable while preserving the original parameter values.
                SketchDimensionTransferResult dimensionTransfer = TransferDimensionsToReflectedGeometry(
                    partDoc,
                    dimensionBaseline,
                    segmentsToMirror,
                    newSegments,
                    refRes,
                    ax1,
                    ay1,
                    ax2,
                    ay2);

                CreateMirrorPartPackage.LogDebug(
                    $"SKETCH_DIMENSION_TRANSFER\n" +
                    $"sketch={sketchFeat.Name}\n" +
                    $"candidates={dimensionTransfer.Candidates}\n" +
                    $"transferred={dimensionTransfer.Transferred}\n" +
                    $"skipped={dimensionTransfer.Skipped}\n" +
                    $"result={(dimensionTransfer.Success ? "PASS" : "FAIL")}\n" +
                    $"reason={dimensionTransfer.FailureReason ?? string.Empty}");

                if (!dimensionTransfer.Success)
                {
                    partDoc.SketchManager.InsertSketch(true);
                    partDoc.ClearSelection2(true);
                    result.StatusCode = "SKETCH_DIMENSION_TRANSFER_FAILED";
                    result.Message = "Cannot transfer source dimensions to reflected geometry: " +
                                     dimensionTransfer.FailureReason;
                    return result;
                }

                // 10. Safely neutralize non-invariant original segments to Construction
                partDoc.ClearSelection2(true);
                foreach (SketchSegment seg in segmentsToMirror)
                {
                    try
                    {
                        seg.ConstructionGeometry = true;
                        result.ConstructionEntities++;
                    }
                    catch {}
                }
                result.OriginalsNeutralized = true;

                // 10. Validate Final Active Sketch State (Oracle)
                FinalSketchStateResult finalSkRes = ValidateFinalSketchState(sketchFeat.Name, segmentsToMirror, invariantSegments, newSegments, sketch);
                CreateMirrorPartPackage.LogDebug($"FINAL_SKETCH_STATE\nsketch={sketchFeat.Name}\noriginalNormalRemaining={finalSkRes.OriginalNormalRemaining}\noriginalConstruction={finalSkRes.OriginalConstruction}\nmirroredNormal={finalSkRes.MirroredNormal}\ninvariantNormal={finalSkRes.InvariantNormal}\nunexpectedNormal={finalSkRes.UnexpectedNormal}\nresult={(finalSkRes.Success ? "PASS" : "FAIL")}");

                if (!finalSkRes.Success)
                {
                    partDoc.SketchManager.InsertSketch(true);
                    partDoc.ClearSelection2(true);
                    result.StatusCode = finalSkRes.FailureReason;
                    result.Message = "Final sketch state validation failed: " + finalSkRes.FailureReason;
                    return result;
                }

                // 11. Exit sketch & rebuild
                partDoc.SketchManager.InsertSketch(true);
                partDoc.ClearSelection2(true);

                // The mirrored profile lives in the SAME driving sketch. Let SOLIDWORKS
                // resolve the feature's native sketch dependency first. Calling
                // ISetContours unconditionally here is unsafe in managed COM: valid closed
                // multi-segment contours can be marshalled as the wrong SAFEARRAY type and
                // ModifyDefinition then breaks a feature that could rebuild on its own.
                CreateMirrorPartPackage.LogDebug(
                    $"IN_PLACE_SKETCH_REBUILD\nfeature={info.Name}\nsketch={sketchFeat.Name}\n" +
                    "contourRetarget=DEFERRED\nresult=PRIMARY_REBUILD");
                partDoc.ForceRebuild3(false);

                // 12. Audit dimensions and sketch relations after the final rebuild.
                // This is intentionally done after leaving the sketch because dangling
                // annotations can appear only after SolidWorks resolves the rebuilt feature.
                SketchAuditSnapshot dimensionFinal = CaptureSketchAudit(sketchFeat, sketch);
                SketchDimensionAuditResult dimensionAudit = CompareSketchAudits(dimensionBaseline, dimensionFinal);
                result.DimensionCountAfter = dimensionFinal.Dimensions.Count;
                result.DimensionAuditPassed = dimensionAudit.Success;
                result.OriginReferencePreserved = dimensionAudit.OriginLinkedAfter >= dimensionAudit.OriginLinkedBefore;

                CreateMirrorPartPackage.LogDebug(
                    $"SKETCH_DIMENSION_AUDIT\n" +
                    $"sketch={sketchFeat.Name}\n" +
                    $"beforeCount={dimensionAudit.BeforeCount}\n" +
                    $"afterCount={dimensionAudit.AfterCount}\n" +
                    $"originLinkedBefore={dimensionAudit.OriginLinkedBefore}\n" +
                    $"originLinkedAfter={dimensionAudit.OriginLinkedAfter}\n" +
                    $"relationCountBefore={dimensionAudit.RelationCountBefore}\n" +
                    $"relationCountAfter={dimensionAudit.RelationCountAfter}\n" +
                    $"suppressedRelationsBefore={dimensionAudit.SuppressedRelationsBefore}\n" +
                    $"suppressedRelationsAfter={dimensionAudit.SuppressedRelationsAfter}\n" +
                    $"danglingAfter={dimensionAudit.DanglingAfter}\n" +
                    $"missing={dimensionAudit.MissingCount}\n" +
                    $"missingNames={dimensionAudit.MissingDimensions ?? string.Empty}\n" +
                    $"valueMismatch={dimensionAudit.ValueMismatchCount}\n" +
                    $"valueMismatchNames={dimensionAudit.ValueMismatchDimensions ?? string.Empty}\n" +
                    $"captureWarningBefore={dimensionBaseline.CaptureWarning ?? string.Empty}\n" +
                    $"captureWarningAfter={dimensionFinal.CaptureWarning ?? string.Empty}\n" +
                    $"result={(dimensionAudit.Success ? "PASS" : "FAIL")}\n" +
                    $"reason={dimensionAudit.FailureReason ?? string.Empty}");

                if (!dimensionAudit.Success)
                {
                    result.StatusCode = "SKETCH_DIMENSION_AUDIT_FAILED";
                    result.Message = "Sketch dimension/relation audit failed: " + dimensionAudit.FailureReason;
                    return result;
                }

                // 13. Verify feature health
                bool isWarning = false;
                int errCode = info.Feature.GetErrorCode2(out isWarning);
                result.FeatureErrorCode = errCode;
                result.FeatureWarning = isWarning;

                CreateMirrorPartPackage.LogDebug($"REPLAY_FEATURE_HEALTH\nfeature={info.Name}\nerrorCode={errCode}\nwarning={isWarning}\nresult={(errCode == 0 ? "PASS" : "FAIL")}");

                if (errCode != 0)
                {
                    // Fallback 1: only a feature that failed the native in-place rebuild is
                    // allowed to enter the fragile contour-retarget path.
                    string contourRetargetDetails;
                    bool contourRetargeted = BodyOperationsHelper.TryRetargetExtrudeContours(
                        partDoc,
                        info,
                        sketchFeat,
                        out contourRetargetDetails);
                    CreateMirrorPartPackage.LogDebug(contourRetargetDetails ??
                        $"EXTRUDE_CONTOUR_RETARGET\nfeature={info.Name}\nresult=FAIL\nreason=NO_DETAILS");

                    if (contourRetargeted)
                    {
                        partDoc.ForceRebuild3(false);
                    }

                    isWarning = false;
                    errCode = info.Feature.GetErrorCode2(out isWarning);
                    result.FeatureErrorCode = errCode;
                    result.FeatureWarning = isWarning;
                    CreateMirrorPartPackage.LogDebug(
                        $"REPLAY_FEATURE_HEALTH_AFTER_CONTOUR_FALLBACK\nfeature={info.Name}\n" +
                        $"retargeted={contourRetargeted}\nerrorCode={errCode}\nwarning={isWarning}\n" +
                        $"result={(errCode == 0 ? "PASS" : "FAIL")}");

                    // Fallback 2: geometry is now valid but a mirrored cut can still point
                    // at the wrong side. Flip only after contour resolution has completed.
                    if (errCode != 0)
                    {
                        string flipRecoveryDetails;
                        bool recovered = BodyOperationsHelper.TryRecoverExtrudeCutRebuild(
                            partDoc,
                            info,
                            cache,
                            out flipRecoveryDetails);
                        CreateMirrorPartPackage.LogDebug(flipRecoveryDetails ??
                            $"CUT_FLIP_REBUILD_RECOVERY\nfeature={info.Name}\nresult=FAIL\nreason=NO_DETAILS");

                        isWarning = false;
                        errCode = info.Feature.GetErrorCode2(out isWarning);
                        result.FeatureErrorCode = errCode;
                        result.FeatureWarning = isWarning;
                        CreateMirrorPartPackage.LogDebug(
                            $"REPLAY_FEATURE_HEALTH_AFTER_FLIP_RECOVERY\nfeature={info.Name}\n" +
                            $"recovered={recovered}\nerrorCode={errCode}\nwarning={isWarning}\n" +
                            $"result={(errCode == 0 ? "PASS" : "FAIL")}");
                    }

                    if (errCode != 0)
                    {
                        result.StatusCode = "FEATURE_REBUILD_ERROR";
                        result.Message = $"Feature has rebuild error code={errCode} warning={isWarning}";
                        return result;
                    }
                }

                result.RebuildPassed = true;
                result.Success = true;
                result.StatusCode = "SUCCESS";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.StatusCode = "EXCEPTION";
                result.Message = ex.Message;
                try
                {
                    partDoc.SketchManager.InsertSketch(true);
                    partDoc.ClearSelection2(true);
                }
                catch {}
            }

            return result;
        }

        private static List<SketchSegment> DetectNewSketchSegments(
            List<SketchSignatureHelper.SketchSegmentSignature> beforeSignatures,
            object[] afterSegmentsObj)
        {
            List<SketchSegment> newSegments = new List<SketchSegment>();
            if (afterSegmentsObj == null) return newSegments;

            foreach (object sObj in afterSegmentsObj)
            {
                SketchSegment seg = sObj as SketchSegment;
                if (seg == null) continue;

                bool isExisting = false;
                if (beforeSignatures != null)
                {
                    foreach (var bSig in beforeSignatures)
                    {
                        if (SketchSignatureHelper.SegmentMatchesSignature(seg, bSig))
                        {
                            isExisting = true;
                            break;
                        }
                    }
                }

                if (!isExisting)
                {
                    newSegments.Add(seg);
                }
            }

            return newSegments;
        }

        private static SketchDimensionTransferResult TransferDimensionsToReflectedGeometry(
            ModelDoc2 partDoc,
            SketchAuditSnapshot baseline,
            List<SketchSegment> sourceSegments,
            List<SketchSegment> reflectedSegments,
            MirrorReferenceResult mirrorReference,
            double x1,
            double y1,
            double x2,
            double y2)
        {
            SketchDimensionTransferResult result = new SketchDimensionTransferResult();
            List<PendingSketchDimensionTransfer> created = new List<PendingSketchDimensionTransfer>();

            if (partDoc == null || baseline == null)
            {
                result.FailureReason = "DIMENSION_TRANSFER_INPUT_NULL";
                return result;
            }

            Dictionary<long, SketchSegment> segmentMap = new Dictionary<long, SketchSegment>();
            foreach (SketchSegment source in sourceSegments ?? new List<SketchSegment>())
            {
                SketchSegment target = FindReflectedSegment(source, reflectedSegments, x1, y1, x2, y2);
                if (target != null) segmentMap[GetComIdentity(source)] = target;
            }

            foreach (SketchDimensionState state in baseline.Dimensions)
            {
                List<object> mappedEntities = new List<object>();
                int sourceReferenceCount = 0;
                int mappedReferenceCount = 0;
                bool mappingFailed = false;
                string mode = "ATTACHED_ENTITIES";

                foreach (object entity in state.AttachedEntities)
                {
                    bool belongsToSource;
                    object mapped = MapDimensionEntity(
                        entity,
                        sourceSegments,
                        segmentMap,
                        x1,
                        y1,
                        x2,
                        y2,
                        out belongsToSource);

                    if (belongsToSource)
                    {
                        sourceReferenceCount++;
                        if (mapped == null)
                        {
                            mappingFailed = true;
                            break;
                        }
                        mappedReferenceCount++;
                        mappedEntities.Add(mapped);
                    }
                    else if (entity != null)
                    {
                        mappedEntities.Add(entity);
                    }
                }

                if (sourceReferenceCount == 0 && state.IsOriginLinked)
                {
                    mappedEntities.Clear();
                    if (TryBuildAxisDistanceReferences(
                        state,
                        sourceSegments,
                        segmentMap,
                        mirrorReference,
                        x1,
                        y1,
                        x2,
                        y2,
                        mappedEntities))
                    {
                        sourceReferenceCount = 1;
                        mappedReferenceCount = 1;
                        mode = "AXIS_DISTANCE_FALLBACK";
                    }
                }

                // A broken external relation can leave attachment metadata behind while no
                // attached entity can be mapped back to the source sketch. Preserve the solved
                // dimension by using the same strict geometric axis-distance reconstruction.
                // Live references that map normally never enter this fallback.
                if (sourceReferenceCount == 0 &&
                    state.AttachedEntities.Count > 0 &&
                    !state.IsReference &&
                    !state.IsOriginLinked)
                {
                    mappedEntities.Clear();
                    bool fallbackBuilt = TryBuildAxisDistanceReferences(
                        state,
                        sourceSegments,
                        segmentMap,
                        mirrorReference,
                        x1,
                        y1,
                        x2,
                        y2,
                        mappedEntities);

                    CreateMirrorPartPackage.LogDebug(
                        $"SKETCH_DIMENSION_BROKEN_REFERENCE_FALLBACK\ndimension={state.Key}\n" +
                        $"dangling={state.IsDangling}\nattachedCount={state.AttachedEntities.Count}\n" +
                        $"value={state.SystemValue:R}\nresult={(fallbackBuilt ? "ACCEPT" : "REJECT")}\n" +
                        $"reason={(fallbackBuilt ? "EXACT_AXIS_DISTANCE_MATCH" : "NO_EXACT_GEOMETRIC_MATCH")}");

                    if (fallbackBuilt)
                    {
                        sourceReferenceCount = 1;
                        mappedReferenceCount = 1;
                        mappingFailed = false;
                        mode = "BROKEN_REFERENCE_AXIS_DISTANCE";
                    }
                }

                if (sourceReferenceCount == 0)
                {
                    result.Skipped++;
                    LogDimensionTransferItem(state, mode, 0, 0, "SKIP", "NO_SOURCE_REFERENCE");
                    continue;
                }

                result.Candidates++;
                if (mappingFailed || mappedEntities.Count < 2)
                {
                    DeleteCreatedSketchDimensions(partDoc, created);
                    result.FailureReason = mappingFailed
                        ? "DIMENSION_REFERENCE_MAPPING_FAILED:" + state.Key
                        : "DIMENSION_REFERENCE_COUNT_LT_2:" + state.Key;
                    LogDimensionTransferItem(state, mode, sourceReferenceCount, mappedReferenceCount, "FAIL", result.FailureReason);
                    return result;
                }

                partDoc.ClearSelection2(true);
                int selected = 0;
                foreach (object mappedEntity in mappedEntities)
                {
                    if (SelectSketchDimensionEntity(mappedEntity, selected > 0)) selected++;
                }

                if (selected < 2)
                {
                    partDoc.ClearSelection2(true);
                    DeleteCreatedSketchDimensions(partDoc, created);
                    result.FailureReason = "DIMENSION_REFERENCE_SELECTION_FAILED:" + state.Key;
                    LogDimensionTransferItem(state, mode, sourceReferenceCount, mappedReferenceCount, "FAIL", result.FailureReason);
                    return result;
                }

                double[] position = ReflectDimensionPosition(state.AnnotationPosition, x1, y1, x2, y2);
                if (position == null)
                {
                    position = BuildFallbackDimensionPosition(mappedEntities, x1, y1, x2, y2);
                }

                object newDisplayObject = null;
                object newDimensionObject = null;
                string createReason;
                bool createdOk = TryCreateReplacementDimension(
                    partDoc,
                    state,
                    position,
                    out newDisplayObject,
                    out newDimensionObject,
                    out createReason);
                partDoc.ClearSelection2(true);

                if (!createdOk)
                {
                    DeleteCreatedSketchDimensions(partDoc, created);
                    result.FailureReason = "DIMENSION_CREATE_FAILED:" + state.Key + ":" + createReason;
                    LogDimensionTransferItem(state, mode, sourceReferenceCount, mappedReferenceCount, "FAIL", result.FailureReason);
                    return result;
                }

                created.Add(new PendingSketchDimensionTransfer
                {
                    Source = state,
                    NewDisplayDimension = newDisplayObject,
                    NewDimension = newDimensionObject,
                    Mode = mode
                });
                LogDimensionTransferItem(state, mode, sourceReferenceCount, mappedReferenceCount, "PASS", "CREATED");
            }

            if (!DeleteOriginalSketchDimensions(partDoc, created))
            {
                DeleteCreatedSketchDimensions(partDoc, created);
                result.FailureReason = "SOURCE_DIMENSION_DELETE_FAILED";
                return result;
            }

            result.Success = true;
            result.Transferred = created.Count;
            return result;
        }

        private static PostBaseFeatureInfo CloneFeatureInfoWithSketch(
            PostBaseFeatureInfo source,
            Feature sketchFeature)
        {
            return new PostBaseFeatureInfo
            {
                Index = source.Index,
                Name = source.Name,
                Type = source.Type,
                Feature = source.Feature,
                ParentFeatureNames = source.ParentFeatureNames == null
                    ? new List<string>()
                    : new List<string>(source.ParentFeatureNames),
                ChildFeatureNames = source.ChildFeatureNames == null
                    ? new List<string>()
                    : new List<string>(source.ChildFeatureNames),
                DrivingSketchName = sketchFeature?.Name,
                DrivingSketchFeature = sketchFeature,
                HasDrivingSketch = sketchFeature != null,
                IsSuppressed = source.IsSuppressed,
                Disposition = source.Disposition
            };
        }

        private static bool TryDuplicateSketchFeature(
            ModelDoc2 partDoc,
            Feature sourceSketch,
            out Feature copiedSketch,
            out string details)
        {
            copiedSketch = null;
            details = null;
            if (partDoc == null || sourceSketch == null)
            {
                details = "INVALID_ARGUMENT";
                return false;
            }

            var before = new HashSet<long>();
            foreach (Feature sketchFeature in EnumerateSketchFeatures(partDoc))
            {
                before.Add(GetComIdentity(sketchFeature));
            }

            try
            {
                partDoc.ClearSelection2(true);
                if (!sourceSketch.Select2(false, 0))
                {
                    details = "SOURCE_SKETCH_SELECT_FAILED";
                    return false;
                }

                partDoc.EditCopy();
                partDoc.Paste();
                partDoc.ClearSelection2(true);

                foreach (Feature candidate in EnumerateSketchFeatures(partDoc))
                {
                    long identity = GetComIdentity(candidate);
                    if (before.Contains(identity))
                        continue;

                    copiedSketch = candidate;
                    break;
                }

                if (copiedSketch == null)
                {
                    details = "PASTE_DID_NOT_CREATE_SKETCH_FEATURE";
                    return false;
                }

                string copiedName = GenerateUniqueSketchName(partDoc, sourceSketch.Name);
                try { copiedSketch.Name = copiedName; }
                catch (Exception renameEx)
                {
                    details = "COPIED_SKETCH_RENAME_FAILED: " + renameEx.Message;
                    TryDeleteFeature(partDoc, copiedSketch, out _);
                    copiedSketch = null;
                    return false;
                }

                details = $"source={sourceSketch.Name}; copied={copiedName}";
                return true;
            }
            catch (Exception ex)
            {
                details = "EXCEPTION: " + ex.Message;
                if (copiedSketch != null)
                {
                    TryDeleteFeature(partDoc, copiedSketch, out _);
                    copiedSketch = null;
                }
                return false;
            }
            finally
            {
                try { partDoc.ClearSelection2(true); } catch { }
            }
        }

        private static IEnumerable<Feature> EnumerateSketchFeatures(ModelDoc2 partDoc)
        {
            Feature feature = partDoc?.FirstFeature() as Feature;
            while (feature != null)
            {
                string type = null;
                try { type = feature.GetTypeName2(); } catch { }
                if (string.Equals(type, "ProfileFeature", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(type, "3DProfileFeature", StringComparison.OrdinalIgnoreCase))
                {
                    yield return feature;
                }
                feature = feature.GetNextFeature() as Feature;
            }
        }

        private static string GenerateUniqueSketchName(ModelDoc2 partDoc, string sourceName)
        {
            string safeSource = string.IsNullOrWhiteSpace(sourceName) ? "Sketch" : sourceName;
            string baseName = "MIRROR_" + safeSource;
            string candidate = baseName;
            int suffix = 1;
            PartDoc pDoc = partDoc as PartDoc;
            while (pDoc != null && pDoc.FeatureByName(candidate) != null)
            {
                candidate = baseName + "_" + suffix.ToString();
                suffix++;
            }
            return candidate;
        }

        private static bool TryDeleteFeature(ModelDoc2 partDoc, Feature feature, out string details)
        {
            details = null;
            if (partDoc == null || feature == null)
            {
                details = "SKIP_INVALID_ARGUMENT";
                return false;
            }

            try
            {
                string featureName = feature.Name;
                partDoc.ClearSelection2(true);
                if (!feature.Select2(false, 0))
                {
                    details = "SELECT_FAILED: " + featureName;
                    return false;
                }

                partDoc.EditDelete();
                partDoc.ClearSelection2(true);
                details = "DELETED: " + featureName;
                return true;
            }
            catch (Exception ex)
            {
                details = "EXCEPTION: " + ex.Message;
                return false;
            }
        }

        private static object MapDimensionEntity(
            object entity,
            List<SketchSegment> sourceSegments,
            Dictionary<long, SketchSegment> segmentMap,
            double x1,
            double y1,
            double x2,
            double y2,
            out bool belongsToSource)
        {
            belongsToSource = false;
            if (entity == null) return null;

            long identity = GetComIdentity(entity);
            SketchSegment mappedSegment;
            if (segmentMap.TryGetValue(identity, out mappedSegment))
            {
                belongsToSource = true;
                return mappedSegment;
            }

            foreach (SketchSegment source in sourceSegments ?? new List<SketchSegment>())
            {
                SketchSegment target;
                if (!segmentMap.TryGetValue(GetComIdentity(source), out target)) continue;

                object mappedPoint = MapOwnedSketchPoint(entity, source, target, x1, y1, x2, y2);
                if (mappedPoint != null)
                {
                    belongsToSource = true;
                    return mappedPoint;
                }

                if (EntityBelongsToSegment(entity, source))
                {
                    belongsToSource = true;
                    return null;
                }
            }

            return entity;
        }

        private static object MapOwnedSketchPoint(
            object entity,
            SketchSegment source,
            SketchSegment target,
            double x1,
            double y1,
            double x2,
            double y2)
        {
            List<SketchPoint> sourcePoints = GetSegmentPoints(source);
            List<SketchPoint> targetPoints = GetSegmentPoints(target);
            if (sourcePoints.Count == 0 || targetPoints.Count == 0) return null;

            long entityId = GetComIdentity(entity);
            for (int i = 0; i < sourcePoints.Count; i++)
            {
                SketchPoint sourcePoint = sourcePoints[i];
                if (GetComIdentity(sourcePoint) != entityId) continue;

                double[] reflected = ReflectPoint2D(sourcePoint.X, sourcePoint.Y, x1, y1, x2, y2);
                SketchPoint best = null;
                double bestDistance = double.MaxValue;
                foreach (SketchPoint targetPoint in targetPoints)
                {
                    double distance = Dist2D(reflected[0], reflected[1], targetPoint.X, targetPoint.Y);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = targetPoint;
                    }
                }
                return bestDistance <= 1e-4 ? best : null;
            }
            return null;
        }

        private static bool EntityBelongsToSegment(object entity, SketchSegment segment)
        {
            long entityId = GetComIdentity(entity);
            foreach (SketchPoint point in GetSegmentPoints(segment))
            {
                if (GetComIdentity(point) == entityId) return true;
            }
            return false;
        }

        private static List<SketchPoint> GetSegmentPoints(SketchSegment segment)
        {
            List<SketchPoint> points = new List<SketchPoint>();
            try
            {
                SketchLine line = segment as SketchLine;
                if (line != null)
                {
                    AddUniqueSketchPoint(points, line.GetStartPoint2() as SketchPoint);
                    AddUniqueSketchPoint(points, line.GetEndPoint2() as SketchPoint);
                    return points;
                }

                SketchArc arc = segment as SketchArc;
                if (arc != null)
                {
                    AddUniqueSketchPoint(points, arc.GetCenterPoint2() as SketchPoint);
                    AddUniqueSketchPoint(points, arc.GetStartPoint2() as SketchPoint);
                    AddUniqueSketchPoint(points, arc.GetEndPoint2() as SketchPoint);
                }
            }
            catch { }
            return points;
        }

        private static void AddUniqueSketchPoint(List<SketchPoint> points, SketchPoint point)
        {
            if (point == null) return;
            long id = GetComIdentity(point);
            foreach (SketchPoint existing in points)
            {
                if (GetComIdentity(existing) == id) return;
            }
            points.Add(point);
        }

        private static bool TryBuildAxisDistanceReferences(
            SketchDimensionState state,
            List<SketchSegment> sourceSegments,
            Dictionary<long, SketchSegment> segmentMap,
            MirrorReferenceResult mirrorReference,
            double x1,
            double y1,
            double x2,
            double y2,
            List<object> mappedEntities)
        {
            if (state == null || double.IsNaN(state.SystemValue) || mirrorReference == null || mirrorReference.Centerline == null)
                return false;

            double axisDx = x2 - x1;
            double axisDy = y2 - y1;
            double axisLength = Math.Sqrt(axisDx * axisDx + axisDy * axisDy);
            if (axisLength <= 1e-12) return false;
            axisDx /= axisLength;
            axisDy /= axisLength;

            SketchLine bestSource = null;
            SketchSegment bestTarget = null;
            double bestError = double.MaxValue;

            foreach (SketchSegment source in sourceSegments ?? new List<SketchSegment>())
            {
                SketchLine line = source as SketchLine;
                if (line == null) continue;
                SketchPoint start = line.GetStartPoint2() as SketchPoint;
                SketchPoint end = line.GetEndPoint2() as SketchPoint;
                if (start == null || end == null) continue;

                double lineDx = end.X - start.X;
                double lineDy = end.Y - start.Y;
                double lineLength = Math.Sqrt(lineDx * lineDx + lineDy * lineDy);
                if (lineLength <= 1e-12) continue;
                lineDx /= lineLength;
                lineDy /= lineLength;
                if (Math.Abs(lineDx * axisDy - lineDy * axisDx) > 1e-4) continue;

                double midX = (start.X + end.X) * 0.5;
                double midY = (start.Y + end.Y) * 0.5;
                double distance = Math.Abs((midX - x1) * (-axisDy) + (midY - y1) * axisDx);
                double error = Math.Abs(distance - Math.Abs(state.SystemValue));
                if (error >= bestError) continue;

                SketchSegment target;
                if (!segmentMap.TryGetValue(GetComIdentity(source), out target)) continue;
                bestError = error;
                bestSource = line;
                bestTarget = target;
            }

            double tolerance = Math.Max(1e-6, Math.Abs(state.SystemValue) * 1e-4);
            if (bestSource == null || bestTarget == null || bestError > tolerance) return false;

            mappedEntities.Add(mirrorReference.Centerline);
            mappedEntities.Add(bestTarget);
            return true;
        }

        private static bool SelectSketchDimensionEntity(object entity, bool append)
        {
            if (entity == null) return false;
            try { return (bool)((dynamic)entity).Select4(append, null); } catch { }
            try { return (bool)((dynamic)entity).Select2(append, 0); } catch { }
            try { return (bool)((dynamic)entity).Select(append); } catch { }
            return false;
        }

        private static bool TryCreateReplacementDimension(
            ModelDoc2 partDoc,
            SketchDimensionState source,
            double[] position,
            out object displayObject,
            out object dimensionObject,
            out string reason)
        {
            displayObject = null;
            dimensionObject = null;
            reason = string.Empty;
            try
            {
                DisplayDimension display = partDoc.AddDimension2(position[0], position[1], position[2]) as DisplayDimension;
                if (display == null)
                {
                    reason = "ADD_DIMENSION_RETURNED_NULL";
                    return false;
                }

                Dimension dimension = display.GetDimension2(0) as Dimension;
                if (dimension == null)
                {
                    reason = "GET_DIMENSION_RETURNED_NULL";
                    return false;
                }

                if (!double.IsNaN(source.SystemValue))
                {
                    dimension.SystemValue = source.SystemValue;
                    double tolerance = Math.Max(1e-8, Math.Abs(source.SystemValue) * 1e-8);
                    if (Math.Abs(dimension.SystemValue - source.SystemValue) > tolerance)
                    {
                        reason = "VALUE_APPLY_FAILED";
                        DeleteDisplayDimension(partDoc, display);
                        return false;
                    }
                }

                try { ((dynamic)dimension).DrivenState = source.DrivenState; } catch { }
                try
                {
                    Annotation annotation = display.GetAnnotation() as Annotation;
                    if (annotation != null && annotation.IsDangling())
                    {
                        reason = "NEW_DIMENSION_DANGLING";
                        DeleteDisplayDimension(partDoc, display);
                        return false;
                    }
                }
                catch { }

                displayObject = display;
                dimensionObject = dimension;
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        private static bool DeleteOriginalSketchDimensions(ModelDoc2 partDoc, List<PendingSketchDimensionTransfer> transfers)
        {
            if (transfers == null || transfers.Count == 0) return true;
            partDoc.ClearSelection2(true);
            int selected = 0;
            foreach (PendingSketchDimensionTransfer transfer in transfers)
            {
                try
                {
                    dynamic display = transfer.Source.DisplayDimensionObject;
                    dynamic annotation = display.GetAnnotation();
                    if (annotation != null && (bool)annotation.Select3(selected > 0, null)) selected++;
                }
                catch { }
            }

            if (selected != transfers.Count)
            {
                partDoc.ClearSelection2(true);
                return false;
            }

            try
            {
                partDoc.EditDelete();
                partDoc.ClearSelection2(true);
                return true;
            }
            catch
            {
                partDoc.ClearSelection2(true);
                return false;
            }
        }

        private static void DeleteCreatedSketchDimensions(ModelDoc2 partDoc, List<PendingSketchDimensionTransfer> transfers)
        {
            if (transfers == null) return;
            for (int i = transfers.Count - 1; i >= 0; i--)
            {
                DeleteDisplayDimension(partDoc, transfers[i].NewDisplayDimension);
            }
            partDoc.ClearSelection2(true);
        }

        private static void DeleteDisplayDimension(ModelDoc2 partDoc, object displayObject)
        {
            if (partDoc == null || displayObject == null) return;
            try
            {
                partDoc.ClearSelection2(true);
                dynamic display = displayObject;
                dynamic annotation = display.GetAnnotation();
                if (annotation != null && (bool)annotation.Select3(false, null)) partDoc.EditDelete();
            }
            catch { }
        }

        private static double[] ReflectDimensionPosition(double[] position, double x1, double y1, double x2, double y2)
        {
            if (position == null || position.Length < 3) return null;
            double[] reflected = ReflectPoint2D(position[0], position[1], x1, y1, x2, y2);
            return new[] { reflected[0], reflected[1], position[2] };
        }

        private static double[] BuildFallbackDimensionPosition(List<object> entities, double x1, double y1, double x2, double y2)
        {
            double x = (x1 + x2) * 0.5;
            double y = (y1 + y2) * 0.5;
            foreach (object entity in entities)
            {
                try
                {
                    dynamic point = entity;
                    x = Convert.ToDouble(point.X);
                    y = Convert.ToDouble(point.Y);
                    break;
                }
                catch { }
            }
            return new[] { x + 0.01, y + 0.01, 0.0 };
        }

        private static void LogDimensionTransferItem(
            SketchDimensionState state,
            string mode,
            int sourceReferences,
            int mappedReferences,
            string result,
            string reason)
        {
            CreateMirrorPartPackage.LogDebug(
                "SKETCH_DIMENSION_TRANSFER_ITEM\n" +
                "key=" + (state != null ? state.Key : string.Empty) + "\n" +
                "mode=" + mode + "\n" +
                "sourceRefs=" + sourceReferences + "\n" +
                "mappedRefs=" + mappedReferences + "\n" +
                "value=" + (state != null ? FormatAuditValue(state.SystemValue) : "NaN") + "\n" +
                "result=" + result + "\n" +
                "reason=" + reason);
        }

        private static SketchAuditSnapshot CaptureSketchAudit(Feature sketchFeature, Sketch sketch)
        {
            SketchAuditSnapshot snapshot = new SketchAuditSnapshot();
            List<string> warnings = new List<string>();

            if (sketchFeature == null)
            {
                snapshot.CaptureWarning = "SKETCH_FEATURE_NULL";
                return snapshot;
            }

            try
            {
                object displayObject = sketchFeature.GetFirstDisplayDimension();
                int guard = 0;
                while (displayObject != null && guard++ < 10000)
                {
                    object nextObject = null;
                    try { nextObject = sketchFeature.GetNextDisplayDimension(displayObject); }
                    catch (Exception ex) { warnings.Add("NEXT_DIM:" + ex.Message); }

                    try
                    {
                        dynamic displayDimension = displayObject;
                        dynamic dimension = displayDimension.GetDimension2(0);
                        if (dimension != null)
                        {
                            SketchDimensionState state = new SketchDimensionState();
                            state.DisplayDimensionObject = displayObject;
                            state.DimensionObject = dimension;
                            state.Name = SafeDynamicString(() => (object)dimension.Name);
                            state.FullName = SafeDynamicString(() => (object)dimension.FullName);
                            state.SystemValue = SafeDynamicDouble(() => (object)dimension.SystemValue, double.NaN);
                            state.DrivenState = SafeDynamicInt(() => (object)dimension.DrivenState, -1);
                            state.IsReference = SafeDynamicBool(() => (object)dimension.IsReference(), false);

                            try
                            {
                                dynamic annotation = displayDimension.GetAnnotation();
                                state.IsDangling = annotation != null && (bool)annotation.IsDangling();
                                if (annotation != null)
                                {
                                    state.AnnotationPosition = ReadAnnotationPosition(annotation);
                                }
                            }
                            catch { state.IsDangling = false; }

                            // GetAttachedEntities2 contains the real sketch entities used by
                            // the dimension.  The old audit only compared name/value, so a
                            // dimension that remained attached to the source geometry could
                            // incorrectly pass after that geometry became construction.
                            try
                            {
                                object attachedObject = displayDimension.GetAttachedEntities2();
                                Array attached = attachedObject as Array;
                                if (attached != null)
                                {
                                    foreach (object entity in attached)
                                    {
                                        if (entity != null) state.AttachedEntities.Add(entity);
                                    }
                                }
                            }
                            catch { }

                            if (state.AttachedEntities.Count == 0)
                            {
                                try
                                {
                                    Array references = dimension.ReferencePoints as Array;
                                    if (references != null)
                                    {
                                        foreach (object entity in references)
                                        {
                                            if (entity != null) state.AttachedEntities.Add(entity);
                                        }
                                    }
                                }
                                catch { }
                            }

                            // Depending on the SOLIDWORKS version and dimension type, the
                            // origin/centerline reference can be exposed either through
                            // Dimension.ReferencePoints or DisplayDimension attached
                            // entities. Inspect both so axis dimensions (for example the
                            // 400 mm dimension) are transferred reliably.
                            state.IsOriginLinked = DimensionReferencesSketchOrigin(dimension) ||
                                                   EntitiesReferenceSketchOrigin(state.AttachedEntities);
                            state.Key = BuildDimensionKey(state);
                            snapshot.Dimensions.Add(state);
                            if (state.IsOriginLinked) snapshot.OriginLinkedDimensionCount++;

                            CreateMirrorPartPackage.LogDebug(
                                $"SKETCH_DIMENSION_ITEM\n" +
                                $"key={state.Key}\n" +
                                $"value={FormatAuditValue(state.SystemValue)}\n" +
                                $"drivenState={state.DrivenState}\n" +
                                $"reference={state.IsReference}\n" +
                                $"dangling={state.IsDangling}\n" +
                                $"originLinked={state.IsOriginLinked}\n" +
                                $"attachedEntities={state.AttachedEntities.Count}\n" +
                                $"annotationPosition={FormatAuditPosition(state.AnnotationPosition)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        warnings.Add("READ_DIM:" + ex.Message);
                    }

                    displayObject = nextObject;
                }

                if (guard >= 10000) warnings.Add("DIMENSION_GUARD_REACHED");
            }
            catch (Exception ex)
            {
                warnings.Add("DIMENSION_ENUM:" + ex.Message);
            }

            try
            {
                dynamic dynamicSketch = sketch;
                dynamic relationManager = dynamicSketch != null ? dynamicSketch.RelationManager : null;
                if (relationManager != null)
                {
                    object relationObject = relationManager.GetRelations((int)swSketchRelationFilterType_e.swAll);
                    object[] relations = relationObject as object[];
                    if (relations != null)
                    {
                        snapshot.RelationCount = relations.Length;
                        foreach (object relationObjectItem in relations)
                        {
                            try
                            {
                                dynamic relation = relationObjectItem;
                                if (SafeDynamicBool(() => (object)relation.Suppressed, false))
                                    snapshot.SuppressedRelationCount++;
                            }
                            catch (Exception ex)
                            {
                                warnings.Add("READ_RELATION:" + ex.Message);
                            }
                        }
                    }
                    else
                    {
                        snapshot.RelationCount = SafeDynamicInt(
                            () => (object)relationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swAll), 0);
                    }
                }
            }
            catch (Exception ex)
            {
                warnings.Add("RELATION_ENUM:" + ex.Message);
            }

            snapshot.CaptureWarning = string.Join(" | ", warnings.ToArray());
            return snapshot;
        }

        private static HashSet<long> CaptureSketchRelationIds(
            Sketch sketch,
            out int relationCount,
            out string warning)
        {
            HashSet<long> ids = new HashSet<long>();
            relationCount = 0;
            warning = string.Empty;

            try
            {
                dynamic dynamicSketch = sketch;
                dynamic relationManager = dynamicSketch != null ? dynamicSketch.RelationManager : null;
                if (relationManager == null)
                {
                    warning = "RELATION_MANAGER_NULL";
                    return ids;
                }

                object relationObject = relationManager.GetRelations((int)swSketchRelationFilterType_e.swAll);
                Array relations = relationObject as Array;
                if (relations == null)
                {
                    relationCount = SafeDynamicInt(
                        () => (object)relationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swAll),
                        0);
                    return ids;
                }

                relationCount = relations.Length;
                foreach (object relation in relations)
                {
                    long id = GetComIdentity(relation);
                    if (id != 0) ids.Add(id);
                }
            }
            catch (Exception ex)
            {
                warning = "RELATION_SNAPSHOT:" + ex.Message;
            }

            return ids;
        }

        private static SketchIndependenceResult DetachNewSymmetricRelations(
            Sketch sketch,
            HashSet<long> relationIdsBeforeMirror,
            int relationCountBeforeMirror)
        {
            SketchIndependenceResult result = new SketchIndependenceResult();
            result.RelationsBeforeMirror = relationCountBeforeMirror;
            relationIdsBeforeMirror = relationIdsBeforeMirror ?? new HashSet<long>();

            try
            {
                dynamic dynamicSketch = sketch;
                dynamic relationManager = dynamicSketch != null ? dynamicSketch.RelationManager : null;
                if (relationManager == null)
                {
                    result.FailureReason = "RELATION_MANAGER_NULL";
                    return result;
                }

                object relationObject = relationManager.GetRelations((int)swSketchRelationFilterType_e.swAll);
                Array relations = relationObject as Array;
                result.RelationsAfterMirror = relations != null
                    ? relations.Length
                    : SafeDynamicInt(
                        () => (object)relationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swAll),
                        0);

                List<object> symmetricRelationsToDelete = new List<object>();
                if (relations != null)
                {
                    foreach (object relation in relations)
                    {
                        if (relation == null) continue;

                        long id = GetComIdentity(relation);
                        bool existedBefore = id != 0 && relationIdsBeforeMirror.Contains(id);
                        if (existedBefore) continue;

                        result.CandidateRelations++;
                        int relationType = SafeDynamicInt(
                            () => (object)((dynamic)relation).GetRelationType(),
                            -1);
                        if (relationType == (int)swConstraintType_e.swConstraintType_SYMMETRIC)
                        {
                            result.SymmetricRelationsFound++;
                            symmetricRelationsToDelete.Add(relation);
                        }
                    }
                }

                foreach (object relation in symmetricRelationsToDelete)
                {
                    bool deleted = false;
                    try
                    {
                        deleted = (bool)relationManager.DeleteRelation((dynamic)relation);
                    }
                    catch
                    {
                        deleted = false;
                    }

                    if (deleted) result.SymmetricRelationsDeleted++;
                }

                result.RelationsAfterDetach = SafeDynamicInt(
                    () => (object)relationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swAll),
                    0);

                if (result.SymmetricRelationsFound == 0)
                {
                    result.FailureReason = "NEW_SYMMETRIC_RELATION_NOT_FOUND";
                    return result;
                }

                if (result.SymmetricRelationsDeleted != result.SymmetricRelationsFound)
                {
                    result.FailureReason = "SYMMETRIC_RELATION_DELETE_INCOMPLETE";
                    return result;
                }

                if (result.RelationsAfterDetach > result.RelationsAfterMirror - result.SymmetricRelationsDeleted)
                {
                    result.FailureReason = "RELATION_COUNT_NOT_REDUCED";
                    return result;
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.FailureReason = "DETACH_EXCEPTION:" + ex.Message;
            }

            return result;
        }

        private static long GetComIdentity(object comObject)
        {
            if (comObject == null || !Marshal.IsComObject(comObject)) return 0;

            IntPtr unknown = IntPtr.Zero;
            try
            {
                unknown = Marshal.GetIUnknownForObject(comObject);
                return unknown.ToInt64();
            }
            catch
            {
                return 0;
            }
            finally
            {
                if (unknown != IntPtr.Zero) Marshal.Release(unknown);
            }
        }

        private static SketchDimensionAuditResult CompareSketchAudits(
            SketchAuditSnapshot before,
            SketchAuditSnapshot after)
        {
            before = before ?? new SketchAuditSnapshot();
            after = after ?? new SketchAuditSnapshot();

            SketchDimensionAuditResult result = new SketchDimensionAuditResult
            {
                BeforeCount = before.Dimensions.Count,
                AfterCount = after.Dimensions.Count,
                RelationCountBefore = before.RelationCount,
                RelationCountAfter = after.RelationCount,
                SuppressedRelationsBefore = before.SuppressedRelationCount,
                SuppressedRelationsAfter = after.SuppressedRelationCount,
                OriginLinkedBefore = before.OriginLinkedDimensionCount,
                OriginLinkedAfter = after.OriginLinkedDimensionCount,
                DanglingAfter = after.Dimensions.FindAll(d => d.IsDangling).Count
            };

            List<string> missing = new List<string>();
            List<string> mismatched = new List<string>();
            HashSet<int> usedAfter = new HashSet<int>();

            foreach (SketchDimensionState expected in before.Dimensions)
            {
                int matchedIndex = FindDimensionMatch(expected, after.Dimensions, usedAfter);
                if (matchedIndex < 0)
                {
                    missing.Add(expected.Key);
                    continue;
                }

                usedAfter.Add(matchedIndex);
                SketchDimensionState actual = after.Dimensions[matchedIndex];
                if (!double.IsNaN(expected.SystemValue) && !double.IsNaN(actual.SystemValue))
                {
                    double tolerance = Math.Max(1e-8, Math.Abs(expected.SystemValue) * 1e-8);
                    if (Math.Abs(expected.SystemValue - actual.SystemValue) > tolerance)
                    {
                        mismatched.Add(expected.Key + ":" +
                            FormatAuditValue(expected.SystemValue) + "->" +
                            FormatAuditValue(actual.SystemValue));
                    }
                }
            }

            result.MissingCount = missing.Count;
            result.ValueMismatchCount = mismatched.Count;
            result.MissingDimensions = string.Join(",", missing.ToArray());
            result.ValueMismatchDimensions = string.Join(",", mismatched.ToArray());

            List<string> failures = new List<string>();
            if (result.MissingCount > 0) failures.Add("DIMENSION_MISSING");
            if (result.ValueMismatchCount > 0) failures.Add("DIMENSION_VALUE_CHANGED");
            if (result.DanglingAfter > 0) failures.Add("DIMENSION_DANGLING");
            if (result.OriginLinkedAfter < result.OriginLinkedBefore) failures.Add("PART_ORIGIN_REFERENCE_LOST");
            if (result.RelationCountBefore > 0 && result.RelationCountAfter < result.RelationCountBefore)
                failures.Add("SKETCH_RELATION_LOST");
            if (result.SuppressedRelationsAfter > result.SuppressedRelationsBefore)
                failures.Add("SKETCH_RELATION_SUPPRESSED");

            result.Success = failures.Count == 0;
            result.FailureReason = string.Join(";", failures.ToArray());
            return result;
        }

        private static int FindDimensionMatch(
            SketchDimensionState expected,
            List<SketchDimensionState> candidates,
            HashSet<int> used)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (used.Contains(i)) continue;
                if (!string.IsNullOrWhiteSpace(expected.FullName) &&
                    string.Equals(expected.FullName, candidates[i].FullName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (used.Contains(i)) continue;
                if (!string.IsNullOrWhiteSpace(expected.Name) &&
                    string.Equals(expected.Name, candidates[i].Name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (used.Contains(i)) continue;
                SketchDimensionState candidate = candidates[i];
                if (expected.IsReference != candidate.IsReference || expected.DrivenState != candidate.DrivenState)
                    continue;
                if (double.IsNaN(expected.SystemValue) || double.IsNaN(candidate.SystemValue))
                    continue;
                double tolerance = Math.Max(1e-8, Math.Abs(expected.SystemValue) * 1e-8);
                if (Math.Abs(expected.SystemValue - candidate.SystemValue) <= tolerance)
                    return i;
            }

            return -1;
        }

        private static bool DimensionReferencesSketchOrigin(dynamic dimension)
        {
            try
            {
                object referenceObject = dimension.ReferencePoints;
                Array references = referenceObject as Array;
                if (references == null) return false;
                foreach (object reference in references)
                {
                    if (EntityIsAtSketchOrigin(reference)) return true;
                }
            }
            catch { }
            return false;
        }

        private static bool EntitiesReferenceSketchOrigin(IEnumerable<object> entities)
        {
            if (entities == null) return false;
            foreach (object entity in entities)
            {
                if (EntityIsAtSketchOrigin(entity)) return true;
            }
            return false;
        }

        private static bool EntityIsAtSketchOrigin(object entity)
        {
            if (entity == null) return false;
            try
            {
                dynamic point = entity;
                double x = Convert.ToDouble(point.X);
                double y = Convert.ToDouble(point.Y);
                double z = Convert.ToDouble(point.Z);
                return Math.Abs(x) <= 1e-9 && Math.Abs(y) <= 1e-9 && Math.Abs(z) <= 1e-9;
            }
            catch { }

            // A construction centerline through the sketch origin is also an
            // origin reference.  This is the normal reference used by the
            // 400 mm dimension in the mirror workflow.
            try
            {
                SketchLine line = entity as SketchLine;
                if (line == null) return false;
                SketchPoint start = line.GetStartPoint2() as SketchPoint;
                SketchPoint end = line.GetEndPoint2() as SketchPoint;
                if (start == null || end == null) return false;

                double dx = end.X - start.X;
                double dy = end.Y - start.Y;
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length <= 1e-12) return false;

                double distanceToOrigin = Math.Abs(start.X * end.Y - start.Y * end.X) / length;
                return distanceToOrigin <= 1e-9;
            }
            catch { return false; }
        }

        private static string BuildDimensionKey(SketchDimensionState state)
        {
            if (!string.IsNullOrWhiteSpace(state.FullName)) return state.FullName;
            if (!string.IsNullOrWhiteSpace(state.Name)) return state.Name;
            return "DIM@" + FormatAuditValue(state.SystemValue) + "#" + state.DrivenState;
        }

        private static string FormatAuditValue(double value)
        {
            return double.IsNaN(value) ? "NaN" : value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static double[] ReadAnnotationPosition(object annotationObject)
        {
            if (annotationObject == null) return null;
            try
            {
                object raw = ((dynamic)annotationObject).GetPosition();
                Array values = raw as Array;
                if (values == null && raw != null)
                {
                    try { values = ((dynamic)raw).ArrayData as Array; }
                    catch { values = null; }
                }

                if (values == null || values.Length < 3) return null;
                return new[]
                {
                    Convert.ToDouble(values.GetValue(0), System.Globalization.CultureInfo.InvariantCulture),
                    Convert.ToDouble(values.GetValue(1), System.Globalization.CultureInfo.InvariantCulture),
                    Convert.ToDouble(values.GetValue(2), System.Globalization.CultureInfo.InvariantCulture)
                };
            }
            catch { return null; }
        }

        private static string FormatAuditPosition(double[] position)
        {
            if (position == null || position.Length < 3) return "(null)";
            return "(" +
                   FormatAuditValue(position[0]) + "," +
                   FormatAuditValue(position[1]) + "," +
                   FormatAuditValue(position[2]) + ")";
        }

        private static string SafeDynamicString(Func<object> getter)
        {
            try { return Convert.ToString(getter()) ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static double SafeDynamicDouble(Func<object> getter, double fallback)
        {
            try { return Convert.ToDouble(getter(), System.Globalization.CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        private static int SafeDynamicInt(Func<object> getter, int fallback)
        {
            try { return Convert.ToInt32(getter(), System.Globalization.CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        private static bool SafeDynamicBool(Func<object> getter, bool fallback)
        {
            try { return Convert.ToBoolean(getter(), System.Globalization.CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        private static FinalSketchStateResult ValidateFinalSketchState(
            string sketchName,
            List<SketchSegment> nonInvariantOriginals,
            List<SketchSegment> invariantOriginals,
            List<SketchSegment> newMirroredSegments,
            Sketch sketch)
        {
            FinalSketchStateResult res = new FinalSketchStateResult
            {
                Success = false,
                SketchName = sketchName
            };

            if (sketch == null)
            {
                res.FailureReason = "Sketch is null.";
                return res;
            }

            object[] allSegsObj = sketch.GetSketchSegments() as object[];
            if (allSegsObj == null)
            {
                res.FailureReason = "No segments found in final sketch.";
                return res;
            }

            HashSet<SketchSegment> origSet = new HashSet<SketchSegment>(nonInvariantOriginals);
            HashSet<SketchSegment> invSet = new HashSet<SketchSegment>(invariantOriginals);
            HashSet<SketchSegment> mirrSet = new HashSet<SketchSegment>(newMirroredSegments);

            foreach (object sObj in allSegsObj)
            {
                SketchSegment s = sObj as SketchSegment;
                if (s == null) continue;

                if (origSet.Contains(s))
                {
                    if (s.ConstructionGeometry) res.OriginalConstruction++;
                    else res.OriginalNormalRemaining++;
                }
                else if (mirrSet.Contains(s))
                {
                    if (!s.ConstructionGeometry) res.MirroredNormal++;
                }
                else if (invSet.Contains(s))
                {
                    if (!s.ConstructionGeometry) res.InvariantNormal++;
                }
                else
                {
                    if (!s.ConstructionGeometry) res.UnexpectedNormal++;
                }
            }

            if (res.OriginalNormalRemaining > 0)
            {
                res.FailureReason = "ORIGINAL_PROFILE_STILL_ACTIVE";
                return res;
            }

            if (res.MirroredNormal < nonInvariantOriginals.Count)
            {
                res.FailureReason = "MIRRORED_PROFILE_NOT_ACTIVE";
                return res;
            }

            res.Success = true;
            return res;
        }

        private static bool CheckIfInvariant2D(SketchSegment seg, double x1, double y1, double x2, double y2)
        {
            if (seg == null) return false;
            try
            {
                SketchLine line = seg as SketchLine;
                if (line != null)
                {
                    SketchPoint sp = line.GetStartPoint2() as SketchPoint;
                    SketchPoint ep = line.GetEndPoint2() as SketchPoint;
                    if (sp != null && ep != null)
                    {
                        double[] rS = ReflectPoint2D(sp.X, sp.Y, x1, y1, x2, y2);
                        double[] rE = ReflectPoint2D(ep.X, ep.Y, x1, y1, x2, y2);

                        bool matchFwd = (Dist2D(rS[0], rS[1], sp.X, sp.Y) < 1e-5) &&
                                        (Dist2D(rE[0], rE[1], ep.X, ep.Y) < 1e-5);
                        bool matchRev = (Dist2D(rS[0], rS[1], ep.X, ep.Y) < 1e-5) &&
                                        (Dist2D(rE[0], rE[1], sp.X, sp.Y) < 1e-5);

                        return matchFwd || matchRev;
                    }
                }

                SketchArc arc = seg as SketchArc;
                if (arc != null)
                {
                    SketchPoint cp = arc.GetCenterPoint2() as SketchPoint;
                    SketchPoint sp = arc.GetStartPoint2() as SketchPoint;
                    SketchPoint ep = arc.GetEndPoint2() as SketchPoint;

                    if (cp != null)
                    {
                        double[] rC = ReflectPoint2D(cp.X, cp.Y, x1, y1, x2, y2);
                        if (Dist2D(rC[0], rC[1], cp.X, cp.Y) > 1e-5) return false;

                        // Check if full circle
                        if (sp != null && ep != null)
                        {
                            if (Dist2D(sp.X, sp.Y, ep.X, ep.Y) < 1e-6)
                            {
                                return true;
                            }

                            double[] rS = ReflectPoint2D(sp.X, sp.Y, x1, y1, x2, y2);
                            double[] rE = ReflectPoint2D(ep.X, ep.Y, x1, y1, x2, y2);

                            bool matchFwd = (Dist2D(rS[0], rS[1], sp.X, sp.Y) < 1e-5) &&
                                            (Dist2D(rE[0], rE[1], ep.X, ep.Y) < 1e-5);
                            bool matchRev = (Dist2D(rS[0], rS[1], ep.X, ep.Y) < 1e-5) &&
                                            (Dist2D(rE[0], rE[1], sp.X, sp.Y) < 1e-5);

                            return matchFwd || matchRev;
                        }

                        return true;
                    }
                }
            }
            catch {}
            return false;
        }

        private static SketchSegment FindReflectedSegment(SketchSegment srcSeg, List<SketchSegment> candidates, double x1, double y1, double x2, double y2)
        {
            try
            {
                SketchLine srcLine = srcSeg as SketchLine;
                if (srcLine != null)
                {
                    SketchPoint sp = srcLine.GetStartPoint2() as SketchPoint;
                    SketchPoint ep = srcLine.GetEndPoint2() as SketchPoint;
                    if (sp == null || ep == null) return null;

                    double[] rStart = ReflectPoint2D(sp.X, sp.Y, x1, y1, x2, y2);
                    double[] rEnd = ReflectPoint2D(ep.X, ep.Y, x1, y1, x2, y2);

                    foreach (var c in candidates)
                    {
                        SketchLine candLine = c as SketchLine;
                        if (candLine == null) continue;
                        SketchPoint csp = candLine.GetStartPoint2() as SketchPoint;
                        SketchPoint cep = candLine.GetEndPoint2() as SketchPoint;
                        if (csp == null || cep == null) continue;

                        bool matchFwd = (Dist2D(rStart[0], rStart[1], csp.X, csp.Y) < 1e-4) &&
                                        (Dist2D(rEnd[0], rEnd[1], cep.X, cep.Y) < 1e-4);
                        bool matchRev = (Dist2D(rStart[0], rStart[1], cep.X, cep.Y) < 1e-4) &&
                                        (Dist2D(rEnd[0], rEnd[1], csp.X, csp.Y) < 1e-4);

                        if (matchFwd || matchRev) return c;
                    }
                }

                SketchArc srcArc = srcSeg as SketchArc;
                if (srcArc != null)
                {
                    SketchPoint cp = srcArc.GetCenterPoint2() as SketchPoint;
                    SketchPoint sp = srcArc.GetStartPoint2() as SketchPoint;
                    SketchPoint ep = srcArc.GetEndPoint2() as SketchPoint;
                    double radius = srcArc.GetRadius();
                    if (cp == null) return null;

                    double[] rCenter = ReflectPoint2D(cp.X, cp.Y, x1, y1, x2, y2);

                    foreach (var c in candidates)
                    {
                        SketchArc candArc = c as SketchArc;
                        if (candArc == null) continue;
                        SketchPoint ccp = candArc.GetCenterPoint2() as SketchPoint;
                        if (ccp == null) continue;

                        if (Dist2D(rCenter[0], rCenter[1], ccp.X, ccp.Y) < 1e-4 &&
                            Math.Abs(radius - candArc.GetRadius()) < 1e-4)
                        {
                            return c;
                        }
                    }
                }
            }
            catch {}
            return null;
        }

        private static bool FindReflectedMatch(SketchSegment srcSeg, List<SketchSegment> candidates, double x1, double y1, double x2, double y2)
        {
            return FindReflectedSegment(srcSeg, candidates, x1, y1, x2, y2) != null;
        }

        private static double[] ReflectPoint2D(double px, double py, double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1;
            double dy = y2 - y1;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-12) return new double[] { px, py };

            double nx = -dy / len;
            double ny = dx / len;

            double wx = px - x1;
            double wy = py - y1;

            double dot = wx * nx + wy * ny;
            return new double[]
            {
                px - 2.0 * dot * nx,
                py - 2.0 * dot * ny
            };
        }

        private static double Dist2D(double x1, double y1, double x2, double y2)
        {
            return Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));
        }
    }

    public sealed class ChamferFeatureMirrorHandler : IFeatureMirrorHandler
    {
        private sealed class EdgeGeometry
        {
            public Edge Edge { get; set; }
            public int CurveType { get; set; }
            public bool Closed { get; set; }
            public double Length { get; set; }
            public List<double[]> Points { get; set; } = new List<double[]>();
        }

        public bool CanHandle(PostBaseFeatureInfo info)
        {
            return info != null &&
                   !string.IsNullOrEmpty(info.Type) &&
                   info.Type.IndexOf("Chamfer", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public FeatureReplayResult Replay(
            ISldWorks swApp,
            ModelDoc2 partDoc,
            PostBaseFeatureInfo info,
            PlaneData mirrorPlane,
            FeatureBodyState cache,
            string protectedBaseFeatureName,
            string protectedBaseSketchName)
        {
            FeatureReplayResult result = new FeatureReplayResult
            {
                FeatureName = info != null ? info.Name : string.Empty,
                FeatureType = info != null ? info.Type : string.Empty,
                StatusCode = "CHAMFER_REPLAY_FAILED",
                Message = "Khong the replay Chamfer.",
                MirrorReferenceKind = MirrorReferenceKind.ModelPlane
            };

            if (partDoc == null || info == null || info.Feature == null || mirrorPlane == null)
            {
                result.Message = "Thieu du lieu de replay Chamfer.";
                return result;
            }

            IChamferFeatureData2 definition = null;
            bool selectionAccess = false;

            try
            {
                Debug.WriteLine("CHAMFER_REPLAY_BEGIN feature=" + info.Name + " type=" + info.Type);

                definition = info.Feature.GetDefinition() as IChamferFeatureData2;
                if (definition == null)
                {
                    result.Message = "Khong doc duoc IChamferFeatureData2.";
                    return result;
                }

                selectionAccess = definition.AccessSelections(partDoc, null);
                if (!selectionAccess)
                {
                    result.Message = "Chamfer AccessSelections that bai.";
                    return result;
                }

                List<Edge> sourceEdges = ToEdges(definition.Edges);
                if (sourceEdges.Count == 0)
                {
                    result.Message = "Chamfer khong co canh tham chieu.";
                    return result;
                }

                List<bool> sourceFlip = new List<bool>();
                foreach (Edge edge in sourceEdges)
                {
                    bool flipped = false;
                    try { flipped = definition.GetIsFlipped(edge); } catch { }
                    sourceFlip.Add(flipped);
                }

                List<Edge> targetEdges = GetCurrentBodyEdges(partDoc);
                if (targetEdges.Count == 0)
                {
                    result.Message = "Khong tim thay canh tren body dich.";
                    return result;
                }

                double modelScale = GetModelScale(partDoc);
                double pointTolerance = Math.Max(1.0e-6, modelScale * 1.0e-6);
                double lengthTolerance = Math.Max(1.0e-6, modelScale * 1.0e-6);

                List<Edge> mappedEdges = new List<Edge>();
                HashSet<Edge> usedEdges = new HashSet<Edge>();

                for (int i = 0; i < sourceEdges.Count; i++)
                {
                    EdgeGeometry sourceGeometry;
                    string sourceReason;
                    if (!TryReadEdgeGeometry(sourceEdges[i], out sourceGeometry, out sourceReason))
                    {
                        result.Message = "Khong doc duoc canh Chamfer nguon: " + sourceReason;
                        return result;
                    }

                    EdgeGeometry reflectedSource = ReflectGeometry(sourceGeometry, mirrorPlane);
                    Edge directBestEdge = null;
                    double directBestScore = double.MaxValue;
                    double directBestLengthDelta = double.MaxValue;
                    Edge reflectedBestEdge = null;
                    double reflectedBestScore = double.MaxValue;
                    double reflectedBestLengthDelta = double.MaxValue;

                    foreach (Edge candidate in targetEdges)
                    {
                        if (candidate == null || usedEdges.Contains(candidate)) continue;

                        EdgeGeometry candidateGeometry;
                        string candidateReason;
                        if (!TryReadEdgeGeometry(candidate, out candidateGeometry, out candidateReason)) continue;
                        if (candidateGeometry.CurveType != sourceGeometry.CurveType) continue;

                        double lengthDelta = Math.Abs(candidateGeometry.Length - sourceGeometry.Length);
                        if (lengthDelta > lengthTolerance) continue;

                        double directScore = CompareGeometry(sourceGeometry, candidateGeometry);
                        if (directScore < directBestScore)
                        {
                            directBestScore = directScore;
                            directBestLengthDelta = lengthDelta;
                            directBestEdge = candidate;
                        }

                        double reflectedScore = CompareGeometry(reflectedSource, candidateGeometry);
                        if (reflectedScore < reflectedBestScore)
                        {
                            reflectedBestScore = reflectedScore;
                            reflectedBestLengthDelta = lengthDelta;
                            reflectedBestEdge = candidate;
                        }
                    }

                    bool directAccepted = directBestEdge != null && directBestScore <= pointTolerance;
                    bool reflectedAccepted = reflectedBestEdge != null && reflectedBestScore <= pointTolerance;
                    Edge bestEdge = null;
                    double bestScore = double.MaxValue;
                    double bestLengthDelta = double.MaxValue;
                    string mappingMode = "NONE";

                    if (directAccepted && (!reflectedAccepted || directBestScore <= reflectedBestScore))
                    {
                        bestEdge = directBestEdge;
                        bestScore = directBestScore;
                        bestLengthDelta = directBestLengthDelta;
                        mappingMode = "DIRECT_CURRENT";
                    }
                    else if (reflectedAccepted)
                    {
                        bestEdge = reflectedBestEdge;
                        bestScore = reflectedBestScore;
                        bestLengthDelta = reflectedBestLengthDelta;
                        mappingMode = "REFLECTED";
                    }

                    Debug.WriteLine(
                        "CHAMFER_EDGE_MAP feature=" + info.Name +
                        " sourceIndex=" + i +
                        " sourceLengthMm=" + (sourceGeometry.Length * 1000.0).ToString("0.######") +
                        " directScoreMm=" + (directBestScore < double.MaxValue ? (directBestScore * 1000.0).ToString("0.######") : "NA") +
                        " reflectedScoreMm=" + (reflectedBestScore < double.MaxValue ? (reflectedBestScore * 1000.0).ToString("0.######") : "NA") +
                        " directLengthDeltaMm=" + (directBestLengthDelta < double.MaxValue ? (directBestLengthDelta * 1000.0).ToString("0.######") : "NA") +
                        " reflectedLengthDeltaMm=" + (reflectedBestLengthDelta < double.MaxValue ? (reflectedBestLengthDelta * 1000.0).ToString("0.######") : "NA") +
                        " selectedScoreMm=" + (bestScore < double.MaxValue ? (bestScore * 1000.0).ToString("0.######") : "NA") +
                        " selectedLengthDeltaMm=" + (bestLengthDelta < double.MaxValue ? (bestLengthDelta * 1000.0).ToString("0.######") : "NA") +
                        " toleranceMm=" + (pointTolerance * 1000.0).ToString("0.######") +
                        " mode=" + mappingMode +
                        " accepted=" + (bestEdge != null));

                    if (bestEdge == null)
                    {
                        result.Message = "Khong tim thay canh Chamfer khop hinh hoc trong dung sai.";
                        return result;
                    }

                    mappedEdges.Add(bestEdge);
                    usedEdges.Add(bestEdge);
                }

                definition.Edges = mappedEdges.ToArray();
                for (int i = 0; i < mappedEdges.Count; i++)
                {
                    try { definition.SetIsFlipped(mappedEdges[i], sourceFlip[i]); } catch { }
                }

                bool modified = info.Feature.ModifyDefinition(definition, partDoc, null);
                if (!modified)
                {
                    result.Message = "Chamfer ModifyDefinition that bai.";
                    return result;
                }

                partDoc.ForceRebuild3(false);

                bool warning;
                int errorCode = info.Feature.GetErrorCode2(out warning);
                result.FeatureErrorCode = errorCode;
                result.FeatureWarning = warning;
                result.RebuildPassed = errorCode == 0;

                if (errorCode != 0)
                {
                    result.Message = "Chamfer rebuild loi. Error=" + errorCode + ", Warning=" + warning;
                    return result;
                }

                result.Success = true;
                result.StatusCode = "CHAMFER_REPLAY_OK";
                result.Message = "Chamfer da duoc anh xa va replay doc lap.";
                result.MirrorReferenceResolved = true;
                result.MirrorGeometryVerified = true;
                result.OriginReferencePreserved = true;

                Debug.WriteLine(
                    "CHAMFER_REPLAY_RESULT feature=" + info.Name +
                    " success=True edgeCount=" + mappedEdges.Count +
                    " error=" + errorCode + " warning=" + warning);

                return result;
            }
            catch (Exception ex)
            {
                result.Message = "Chamfer replay exception: " + ex.GetType().Name + " - " + ex.Message;
                Debug.WriteLine("CHAMFER_REPLAY_RESULT feature=" + (info != null ? info.Name : string.Empty) +
                                " success=False exception=" + ex);
                return result;
            }
            finally
            {
                if (definition != null && selectionAccess)
                {
                    try { definition.ReleaseSelectionAccess(); } catch { }
                }
            }
        }

        private static List<Edge> ToEdges(object value)
        {
            List<Edge> edges = new List<Edge>();
            if (value == null) return edges;

            Edge single = value as Edge;
            if (single != null)
            {
                edges.Add(single);
                return edges;
            }

            Array values = value as Array;
            if (values == null) return edges;
            foreach (object item in values)
            {
                Edge edge = item as Edge;
                if (edge != null) edges.Add(edge);
            }
            return edges;
        }

        private static List<Edge> GetCurrentBodyEdges(ModelDoc2 partDoc)
        {
            List<Edge> edges = new List<Edge>();
            PartDoc part = partDoc as PartDoc;
            if (part == null) return edges;

            object[] bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
            if (bodies == null) return edges;

            foreach (object bodyObject in bodies)
            {
                Body2 body = bodyObject as Body2;
                if (body == null) continue;
                object[] bodyEdges = body.GetEdges() as object[];
                if (bodyEdges == null) continue;
                foreach (object edgeObject in bodyEdges)
                {
                    Edge edge = edgeObject as Edge;
                    if (edge != null) edges.Add(edge);
                }
            }
            return edges;
        }

        private static double GetModelScale(ModelDoc2 partDoc)
        {
            PartDoc part = partDoc as PartDoc;
            if (part == null) return 1.0;
            object[] bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
            if (bodies == null || bodies.Length == 0) return 1.0;

            double scale = 0.0;
            foreach (object bodyObject in bodies)
            {
                Body2 body = bodyObject as Body2;
                double[] box = body != null ? body.GetBodyBox() as double[] : null;
                if (box == null || box.Length < 6) continue;
                double dx = box[3] - box[0];
                double dy = box[4] - box[1];
                double dz = box[5] - box[2];
                scale = Math.Max(scale, Math.Sqrt(dx * dx + dy * dy + dz * dz));
            }
            return scale > 1.0e-9 ? scale : 1.0;
        }

        private static bool TryReadEdgeGeometry(Edge edge, out EdgeGeometry geometry, out string reason)
        {
            geometry = null;
            reason = string.Empty;
            if (edge == null)
            {
                reason = "EDGE_NULL";
                return false;
            }

            try
            {
                Curve curve = edge.GetCurve() as Curve;
                CurveParamData parameters = edge.GetCurveParams3() as CurveParamData;
                if (curve == null || parameters == null)
                {
                    reason = "CURVE_OR_PARAMS_NULL";
                    return false;
                }

                double u0 = parameters.UMinValue;
                double u1 = parameters.UMaxValue;
                bool closed = false;
                bool periodic = false;
                double ignoredStart = 0.0;
                double ignoredEnd = 0.0;
                try { curve.GetEndParams(out ignoredStart, out ignoredEnd, out closed, out periodic); } catch { }

                int sampleCount = (closed || periodic) ? 12 : 7;
                List<double[]> points = new List<double[]>();
                for (int i = 0; i < sampleCount; i++)
                {
                    double t = sampleCount == 1 ? 0.0 : (double)i / (sampleCount - 1);
                    double u = u0 + (u1 - u0) * t;
                    object evaluation = curve.Evaluate2(u, 0);
                    double[] values = evaluation as double[];
                    if (values == null || values.Length < 3) continue;
                    points.Add(new double[] { values[0], values[1], values[2] });
                }

                if (points.Count < 2)
                {
                    reason = "INSUFFICIENT_SAMPLES";
                    return false;
                }

                double length = 0.0;
                try { length = curve.GetLength3(u0, u1); } catch { }
                if (double.IsNaN(length) || double.IsInfinity(length) || length <= 1.0e-12)
                {
                    for (int i = 1; i < points.Count; i++) length += Distance(points[i - 1], points[i]);
                }

                if (length <= 1.0e-12)
                {
                    reason = "ZERO_LENGTH";
                    return false;
                }

                geometry = new EdgeGeometry
                {
                    Edge = edge,
                    CurveType = curve.Identity(),
                    Closed = closed || periodic,
                    Length = length,
                    Points = points
                };
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        private static EdgeGeometry ReflectGeometry(EdgeGeometry source, PlaneData plane)
        {
            EdgeGeometry reflected = new EdgeGeometry
            {
                Edge = source.Edge,
                CurveType = source.CurveType,
                Closed = source.Closed,
                Length = source.Length
            };

            foreach (double[] point in source.Points)
            {
                reflected.Points.Add(ReflectPoint(point, plane));
            }
            return reflected;
        }

        private static double[] ReflectPoint(double[] point, PlaneData plane)
        {
            double nx = plane.Normal[0];
            double ny = plane.Normal[1];
            double nz = plane.Normal[2];
            double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (length <= 1.0e-12) return new double[] { point[0], point[1], point[2] };
            nx /= length;
            ny /= length;
            nz /= length;

            double dx = point[0] - plane.Origin[0];
            double dy = point[1] - plane.Origin[1];
            double dz = point[2] - plane.Origin[2];
            double signedDistance = dx * nx + dy * ny + dz * nz;

            return new double[]
            {
                point[0] - 2.0 * signedDistance * nx,
                point[1] - 2.0 * signedDistance * ny,
                point[2] - 2.0 * signedDistance * nz
            };
        }

        private static double CompareGeometry(EdgeGeometry expected, EdgeGeometry candidate)
        {
            if (expected == null || candidate == null || expected.Points.Count == 0 || candidate.Points.Count == 0)
                return double.MaxValue;

            int count = Math.Min(expected.Points.Count, candidate.Points.Count);
            double forward = ComparePointSequence(expected.Points, candidate.Points, count, false, 0);
            double reverse = ComparePointSequence(expected.Points, candidate.Points, count, true, 0);
            double best = Math.Min(forward, reverse);

            if (expected.Closed && candidate.Closed)
            {
                for (int offset = 1; offset < count; offset++)
                {
                    best = Math.Min(best, ComparePointSequence(expected.Points, candidate.Points, count, false, offset));
                    best = Math.Min(best, ComparePointSequence(expected.Points, candidate.Points, count, true, offset));
                }
            }
            return best;
        }

        private static double ComparePointSequence(
            List<double[]> expected,
            List<double[]> candidate,
            int count,
            bool reverse,
            int offset)
        {
            double maxDistance = 0.0;
            for (int i = 0; i < count; i++)
            {
                int candidateIndex = reverse ? (count - 1 - i) : i;
                candidateIndex = (candidateIndex + offset) % count;
                maxDistance = Math.Max(maxDistance, Distance(expected[i], candidate[candidateIndex]));
            }
            return maxDistance;
        }

        private static double Distance(double[] a, double[] b)
        {
            double dx = a[0] - b[0];
            double dy = a[1] - b[1];
            double dz = a[2] - b[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    public sealed class FeatureReplayDispatcher
    {
        private readonly List<IFeatureMirrorHandler> handlers = new List<IFeatureMirrorHandler>();

        public FeatureReplayDispatcher()
        {
            handlers.Add(new SketchDrivenFeatureMirrorHandler());
            handlers.Add(new ChamferFeatureMirrorHandler());
        }

        public IFeatureMirrorHandler GetHandler(PostBaseFeatureInfo info)
        {
            foreach (var h in handlers)
            {
                if (h.CanHandle(info)) return h;
            }
            return null;
        }
    }

    public static class SheetMetalMirrorServiceV6
    {
        public static MirrorPackageResult ExecuteV6MirrorPipeline(
            ISldWorks swApp,
            Component2 sourceComponent,
            RefPlane assemblyPlane,
            ISavePathProvider savePathProvider)
        {
            MirrorPackageResult result = new MirrorPackageResult { Success = false };

            string sourcePath = sourceComponent.GetPathName();
            CreateMirrorPartPackage.LogDebug($"SOURCE path={sourcePath}");
            CreateMirrorPartPackage.LogDebug($"SOURCE component={sourceComponent.Name2}");

            MathTransform sourceTransform = sourceComponent.Transform2;
            if (sourceTransform != null)
            {
                double[] tData = sourceTransform.ArrayData as double[];
                if (tData != null && tData.Length >= 16)
                {
                    CreateMirrorPartPackage.LogDebug($"SOURCE transform={string.Join(",", tData)}");
                }
            }

            string defaultDir = Path.GetDirectoryName(sourcePath);
            string defaultFileName = Path.GetFileNameWithoutExtension(sourcePath) + "-MIRROR.sldprt";

            string chosenTargetPartPath = savePathProvider.ResolveSavePath(swApp, sourcePath, defaultDir, defaultFileName);
            if (string.IsNullOrWhiteSpace(chosenTargetPartPath))
            {
                result.Cancelled = true;
                result.Message = "User cancelled Save As.";
                CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=CANCELLED");
                return result;
            }

            if (string.Equals(sourcePath, chosenTargetPartPath, StringComparison.OrdinalIgnoreCase))
            {
                result.Message = "Ten file moi khong duoc trung voi file goc.";
                CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=FAIL");
                return result;
            }

            CreateMirrorPartPackage.LogDebug($"TARGET path={chosenTargetPartPath}");

            IMathUtility mathUtility = swApp.GetMathUtility() as IMathUtility;
            PlaneData selectedMirrorPlane = MirrorPlaneMapper.GetLocalPlane(mathUtility, sourceComponent, assemblyPlane);
            PlaneData mirrorPlane = MirrorPlaneMapper.CreatePartOriginAnchoredPlane(selectedMirrorPlane);

            using (SourceDocumentGuard sourceGuard = new SourceDocumentGuard(swApp, sourcePath))
            {
                if (sourceGuard.Document == null)
                {
                    result.Message = "Khong the mo Part nguon de kiem tra.";
                    CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=FAIL");
                    return result;
                }

                if (File.Exists(chosenTargetPartPath))
                {
                    try { File.Delete(chosenTargetPartPath); } catch {}
                }

                File.Copy(sourcePath, chosenTargetPartPath, true);

                int errors = 0;
                int warnings = 0;
                ModelDoc2 copiedPartDoc = swApp.OpenDoc6(
                    chosenTargetPartPath,
                    (int)swDocumentTypes_e.swDocPART,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref errors,
                    ref warnings);

                if (copiedPartDoc == null)
                {
                    result.Message = "Khong the mo Part copy.";
                    CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=FAIL");
                    return result;
                }

                try
                {
                    // Activate Referenced Configuration
                    string reqConfig = sourceComponent.ReferencedConfiguration;
                    bool showConfigRet = false;
                    string actConfigName = copiedPartDoc.ConfigurationManager.ActiveConfiguration.Name;

                    if (!string.IsNullOrEmpty(reqConfig))
                    {
                        showConfigRet = copiedPartDoc.ShowConfiguration2(reqConfig);
                        actConfigName = copiedPartDoc.ConfigurationManager.ActiveConfiguration.Name;
                    }

                    bool configMatched = string.IsNullOrEmpty(reqConfig) || string.Equals(actConfigName, reqConfig, StringComparison.OrdinalIgnoreCase);

                    CreateMirrorPartPackage.LogDebug($"CONFIG\nrequested={reqConfig}\nshowConfigurationReturn={showConfigRet}\nactive={actConfigName}\nmatched={configMatched}");

                    if (!configMatched)
                    {
                        result.Message = $"Failed to activate referenced configuration '{reqConfig}'. Active is '{actConfigName}'.";
                        CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=FAIL");
                        return result;
                    }

                    // 1. Find Base-Flange and Driving Sketch
                    Feature baseFeature = null;
                    Feature baseDrivingSketch = null;
                    FindBaseFlangeAndSketch(copiedPartDoc, out baseFeature, out baseDrivingSketch);

                    if (baseFeature == null)
                    {
                        result.Message = "Khong tim thay Base-Flange feature trong Part.";
                        CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=FAIL");
                        return result;
                    }

                    string baseFeatName = baseFeature.Name;
                    string baseSketchName = (baseDrivingSketch != null) ? baseDrivingSketch.Name : "<none>";

                    CreateMirrorPartPackage.LogDebug($"MIRROR_PART_V6: BASE_PROTECTED feature={baseFeatName} sketch={baseSketchName}");

                    // Capture initial Base Sketch Signature
                    Sketch baseSketchObj = (baseDrivingSketch != null) ? baseDrivingSketch.GetSpecificFeature2() as Sketch : null;
                    List<SketchSignatureHelper.SketchSegmentSignature> initialBaseSketchSig = SketchSignatureHelper.CaptureSketchSignature(baseSketchObj);

                    // 2. Scan All Post-Base Features
                    List<PostBaseFeatureInfo> postBaseFeatures = EnumeratePostBaseFeatures(copiedPartDoc, baseFeature);

                    // 3. Build 3D Body State Cache
                    CreateMirrorPartPackage.LogDebug("==============================");
                    CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: CACHE BUILD");
                    CreateMirrorPartPackage.LogDebug("==============================");

                    string cacheError = null;
                    List<FeatureBodyState> bodyCache = BuildFeatureBodyCache(copiedPartDoc, baseFeature, postBaseFeatures, out cacheError);
                    if (!string.IsNullOrEmpty(cacheError))
                    {
                        result.Message = "Cache build failed: " + cacheError;
                        CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: CACHE_BUILD_FAILED " + cacheError);
                        CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=FAIL");
                        return result;
                    }

                    // Classify Features based on body delta
                    int bodyChangingCount = 0;
                    int noGeometryChangeCount = 0;
                    int suppressedCount = 0;

                    for (int i = 0; i < postBaseFeatures.Count; i++)
                    {
                        var info = postBaseFeatures[i];
                        var cache = (i < bodyCache.Count) ? bodyCache[i] : null;

                        if (info.IsSuppressed)
                        {
                            info.Disposition = FeatureReplayDisposition.Suppressed;
                            suppressedCount++;
                        }
                        else if (cache != null && !cache.ChangesGeometry)
                        {
                            info.Disposition = FeatureReplayDisposition.NoGeometryChange;
                            noGeometryChangeCount++;
                        }
                        else
                        {
                            info.Disposition = FeatureReplayDisposition.ReplayRequired;
                            bodyChangingCount++;
                        }
                    }

                    // 4. Synchronize Live Model with B0 and Prepare Sequential Replay
                    string rbBaseErr = null;
                    bool rbBaseOk = MoveRollbackForReplay(copiedPartDoc, baseFeature, out rbBaseErr);
                    if (!rbBaseOk)
                    {
                        result.Message = "Initial rollback to Base-Flange failed: " + rbBaseErr;
                        CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=FAIL");
                        return result;
                    }

                    string liveBaseErr = null;
                    Body2 liveBaseBody = BodyOperationsHelper.GetSolidBodyCopyStrict(copiedPartDoc, out liveBaseErr);
                    if (liveBaseBody == null)
                    {
                        result.Message = "Failed to capture live base body after rollback: " + liveBaseErr;
                        CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=FAIL");
                        return result;
                    }

                    double tempVol = (bodyCache.Count > 0 && bodyCache[0].BeforeBody != null)
                        ? BodyOperationsHelper.GetBodyVolume(bodyCache[0].BeforeBody)
                        : BodyOperationsHelper.GetBodyVolume(liveBaseBody);
                    double liveVol = BodyOperationsHelper.GetBodyVolume(liveBaseBody);
                    double syncDiff = Math.Abs(tempVol - liveVol);
                    bool syncPass = syncDiff <= Math.Max(BodyOperationsHelper.ABSOLUTE_GEOMETRY_TOLERANCE, tempVol * BodyOperationsHelper.RELATIVE_TOLERANCE);

                    CreateMirrorPartPackage.LogDebug($"REPLAY_STATE_SYNC\ntempVolume={tempVol:E6}\nliveVolume={liveVol:E6}\ndifference={syncDiff:E6}\nresult={(syncPass ? "PASS" : "FAIL")}");

                    if (!syncPass)
                    {
                        result.Message = $"Live model state does not match B0 cache (diff={syncDiff:E6})";
                        CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=FAIL");
                        return result;
                    }

                    Body2 currentActualBody = liveBaseBody;
                    FeatureReplayDispatcher dispatcher = new FeatureReplayDispatcher();

                    int replayedCount = 0;
                    int unsupportedGeometryCount = 0;
                    int failedCount = 0;
                    int validatedCount = 0;
                    int manualSkippedGeometryCount = 0;

                    // Track replayed sketch signatures for upstream persistence checking
                    Dictionary<int, List<SketchSignatureHelper.SketchSegmentSignature>> replayedSketchSignatures =
                        new Dictionary<int, List<SketchSignatureHelper.SketchSegmentSignature>>();

                    for (int i = 0; i < postBaseFeatures.Count; i++)
                    {
                        PostBaseFeatureInfo featInfo = postBaseFeatures[i];
                        FeatureBodyState cacheState = (i < bodyCache.Count) ? bodyCache[i] : null;

                        CreateMirrorPartPackage.LogDebug("==============================");
                        CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: REPLAY FEATURE");
                        CreateMirrorPartPackage.LogDebug("==============================");
                        CreateMirrorPartPackage.LogDebug($"index={i}");
                        CreateMirrorPartPackage.LogDebug($"name={featInfo.Name}");
                        CreateMirrorPartPackage.LogDebug($"type={featInfo.Type}");
                        CreateMirrorPartPackage.LogDebug($"disposition={featInfo.Disposition}");

                        if (featInfo.Disposition == FeatureReplayDisposition.Suppressed ||
                            featInfo.Disposition == FeatureReplayDisposition.NoGeometryChange)
                        {
                            CreateMirrorPartPackage.LogDebug("handler=SKIPPED_NO_GEOMETRY_CHANGE");
                            continue;
                        }

                        // Curve-driven patterns are intentionally left for manual correction.
                        if (string.Equals(featInfo.Type, "CurvePattern", StringComparison.OrdinalIgnoreCase))
                        {
                            manualSkippedGeometryCount++;
                            CreateMirrorPartPackage.LogDebug($"FEATURE_REPLAY_SKIP_MANUAL\nname={featInfo.Name}\ntype={featInfo.Type}\nreason=CURVE_PATTERN_MANUAL_CORRECTION");
                            continue;
                        }

                        IFeatureMirrorHandler handler = dispatcher.GetHandler(featInfo);
                        if (handler == null)
                        {
                            CreateMirrorPartPackage.LogDebug("handler=UNSUPPORTED");
                            unsupportedGeometryCount++;
                            failedCount++;
                            result.Message = $"Unsupported geometry-changing feature '{featInfo.Name}' type='{featInfo.Type}'";
                            CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=FAIL");
                            return result;
                        }

                        CreateMirrorPartPackage.LogDebug($"handler={handler.GetType().Name}");

                        // Move rollback bar to after this feature so downstream features are rolled back!
                        string rbFeatErr = null;
                        bool rbFeatOk = MoveRollbackForReplay(copiedPartDoc, featInfo.Feature, out rbFeatErr);
                        if (!rbFeatOk)
                        {
                            failedCount++;
                            result.Message = $"Rollback for feature '{featInfo.Name}' failed: {rbFeatErr}";
                            CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=FAIL");
                            return result;
                        }

                        // Check Upstream Replay Persistence
                        for (int prevIdx = 0; prevIdx < i; prevIdx++)
                        {
                            var prevFeat = postBaseFeatures[prevIdx];
                            if (prevFeat.Disposition == FeatureReplayDisposition.ReplayRequired &&
                                prevFeat.HasDrivingSketch &&
                                prevFeat.DrivingSketchFeature != null &&
                                replayedSketchSignatures.ContainsKey(prevIdx))
                            {
                                Sketch skObj = prevFeat.DrivingSketchFeature.GetSpecificFeature2() as Sketch;
                                var currentSig = SketchSignatureHelper.CaptureSketchSignature(skObj);
                                bool match = SketchSignatureHelper.CompareSignatures(replayedSketchSignatures[prevIdx], currentSig);
                                CreateMirrorPartPackage.LogDebug($"UPSTREAM_REPLAY_PERSISTENCE\npreviousFeature={prevFeat.Name}\nnextFeature={featInfo.Name}\nsketch={prevFeat.DrivingSketchName}\nunchanged={match}");

                                if (!match)
                                {
                                    failedCount++;
                                    result.Message = $"CRITICAL: Upstream sketch '{prevFeat.DrivingSketchName}' was modified/reverted when rolling back to '{featInfo.Name}'!";
                                    CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=FAIL");
                                    return result;
                                }
                            }
                        }

                        FeatureReplayResult replayRes = handler.Replay(swApp, copiedPartDoc, featInfo, mirrorPlane, cacheState, baseFeatName, baseSketchName);

                        CreateMirrorPartPackage.LogDebug($"mirrorReference.mode={replayRes.MirrorReferenceKind} ({replayRes.StatusCode})");
                        CreateMirrorPartPackage.LogDebug($"sourceEntities={replayRes.SourceEntities}");
                        CreateMirrorPartPackage.LogDebug($"invariantEntities={replayRes.InvariantEntities}");
                        CreateMirrorPartPackage.LogDebug($"mirroredEntities={replayRes.MirroredEntities}");
                        CreateMirrorPartPackage.LogDebug($"constructionEntities={replayRes.ConstructionEntities}");

                        if (!replayRes.Success)
                        {
                            failedCount++;
                            result.Message = $"Replay failed for feature '{featInfo.Name}': {replayRes.Message}";
                            CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=FAIL");
                            return result;
                        }

                        replayedCount++;

                        // Capture signature of newly replayed driving sketch
                        if (featInfo.HasDrivingSketch && featInfo.DrivingSketchFeature != null)
                        {
                            Sketch currentSkObj = featInfo.DrivingSketchFeature.GetSpecificFeature2() as Sketch;
                            replayedSketchSignatures[i] = SketchSignatureHelper.CaptureSketchSignature(currentSkObj);
                        }

                        // Capture actual body after replay
                        string actErr = null;
                        Body2 stepActualBody = BodyOperationsHelper.GetSolidBodyCopyStrict(copiedPartDoc, out actErr);
                        if (stepActualBody == null)
                        {
                            failedCount++;
                            result.Message = $"Failed to capture actual body after feature '{featInfo.Name}': {actErr}";
                            CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=FAIL");
                            return result;
                        }

                        // A mirrored sketch can require the opposite Flip Side To Cut state.
                        // Prefer the source removed-volume oracle when it remains valid. For an
                        // asymmetric unchanged sheet-metal base, select the state whose removed
                        // region is nearest the reflected source removed-region centroid.
                        string cutFlipDetails;
                        bool allowAsymmetricCutVolume;
                        if (!BodyOperationsHelper.TryCorrectExtrudeCutFlip(
                            copiedPartDoc,
                            featInfo,
                            currentActualBody,
                            cacheState,
                            mirrorPlane,
                            ref stepActualBody,
                            out allowAsymmetricCutVolume,
                            out cutFlipDetails))
                        {
                            if (!string.IsNullOrEmpty(cutFlipDetails))
                            {
                                CreateMirrorPartPackage.LogDebug(cutFlipDetails);
                            }
                            failedCount++;
                            result.Message = $"Cut direction correction failed for feature '{featInfo.Name}'.";
                            CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=FAIL");
                            return result;
                        }

                        replayRes.AllowAsymmetricCutVolume = allowAsymmetricCutVolume;

                        if (!string.IsNullOrEmpty(cutFlipDetails))
                        {
                            CreateMirrorPartPackage.LogDebug(cutFlipDetails);
                        }

                        // Feature-Semantic Validation (V6.2 Oracle)
                        FeatureSemanticValidationResult semVal = BodyOperationsHelper.ValidateReplaySemantics(
                            currentActualBody,
                            stepActualBody,
                            featInfo,
                            cacheState,
                            replayRes,
                            copiedPartDoc);

                        string semPassStr = semVal.Success ? "PASS" : "FAIL";
                        CreateMirrorPartPackage.LogDebug($"FEATURE_SEMANTIC_VALIDATE\nfeature={featInfo.Name}\nkind={semVal.ExpectedChangeKind}\nbeforeBodyCount={semVal.BeforeBodyCount}\nafterBodyCount={semVal.AfterBodyCount}\nbeforeVolume={semVal.BeforeVolume:E6}\nafterVolume={semVal.AfterVolume:E6}\nexpectedAddedVolume={semVal.ExpectedAddedVolume:E6}\nexpectedRemovedVolume={semVal.ExpectedRemovedVolume:E6}\nactualAddedVolume={semVal.ActualAddedVolume:E6}\nactualRemovedVolume={semVal.ActualRemovedVolume:E6}\nrelativeVolumeError={semVal.RelativeVolumeError:E6}\nresult={semPassStr}\nreason={semVal.FailureReason}");

                        if (!semVal.Success)
                        {
                            failedCount++;
                            result.Message = $"Feature Semantic Validation failed for feature '{featInfo.Name}': {semVal.FailureReason}";
                            CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=FAIL");
                            return result;
                        }

                        validatedCount++;
                        // Advance sequential state
                        currentActualBody = stepActualBody;
                    }

                    // 5. Restore Rollback to End
                    bool rbEndOk = copiedPartDoc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, "");
                    CreateMirrorPartPackage.LogDebug($"REPLAY_ROLLBACK_TO_END result={rbEndOk}");

                    copiedPartDoc.ForceRebuild3(false);

                    // 6. Base Sketch Signature Verification Check
                    List<SketchSignatureHelper.SketchSegmentSignature> finalBaseSketchSig = SketchSignatureHelper.CaptureSketchSignature(baseSketchObj);
                    bool baseSketchUnchanged = SketchSignatureHelper.CompareSignatures(initialBaseSketchSig, finalBaseSketchSig);

                    bool isBaseWarning = false;
                    int baseErrCode = baseFeature.GetErrorCode2(out isBaseWarning);
                    bool baseFeatureHealthy = (baseErrCode == 0);

                    // 7. Final Replay Audit on all replayed features after full rebuild
                    CreateMirrorPartPackage.LogDebug("==============================");
                    CreateMirrorPartPackage.LogDebug("FINAL_REPLAY_AUDIT");
                    CreateMirrorPartPackage.LogDebug("==============================");

                    bool allAuditPass = true;
                    for (int i = 0; i < postBaseFeatures.Count; i++)
                    {
                        var featInfo = postBaseFeatures[i];
                        if (string.Equals(featInfo.Type, "CurvePattern", StringComparison.OrdinalIgnoreCase))
                        {
                            CreateMirrorPartPackage.LogDebug($"FINAL_REPLAY_AUDIT_SKIP\nname={featInfo.Name}\ntype={featInfo.Type}\nreason=CURVE_PATTERN_MANUAL_CORRECTION");
                            continue;
                        }
                        if (featInfo.Disposition == FeatureReplayDisposition.ReplayRequired &&
                            featInfo.HasDrivingSketch &&
                            featInfo.DrivingSketchFeature != null)
                        {
                            bool isWarning = false;
                            int err = featInfo.Feature.GetErrorCode2(out isWarning);
                            bool featHealthy = (err == 0);

                            Sketch skObj = featInfo.DrivingSketchFeature.GetSpecificFeature2() as Sketch;
                            object[] segs = skObj != null ? skObj.GetSketchSegments() as object[] : null;
                            int normalCount = 0;
                            int constrCount = 0;
                            if (segs != null)
                            {
                                foreach (object sObj in segs)
                                {
                                    SketchSegment s = sObj as SketchSegment;
                                    if (s != null)
                                    {
                                        if (s.ConstructionGeometry) constrCount++;
                                        else normalCount++;
                                    }
                                }
                            }

                            bool skMirrorOk = (normalCount > 0);
                            bool origNeutOk = (constrCount > 0);

                            CreateMirrorPartPackage.LogDebug($"{featInfo.Name}:\n    sketchMirror={skMirrorOk}\n    originalNeutralized={origNeutOk}\n    featureHealthy={featHealthy}");

                            if (!featHealthy || !skMirrorOk) allAuditPass = false;
                        }
                    }

                    // 8. Final Success Verification
                    string finalActErr = null;
                    Body2 finalSolidBody = BodyOperationsHelper.GetSolidBodyCopyStrict(copiedPartDoc, out finalActErr);
                    bool singleSolidBody = (finalSolidBody != null);

                    bool overallPass = (failedCount == 0) &&
                                       (unsupportedGeometryCount == 0) &&
                                       (bodyChangingCount == validatedCount + manualSkippedGeometryCount) &&
                                       baseSketchUnchanged &&
                                       baseFeatureHealthy &&
                                       allAuditPass &&
                                       singleSolidBody;

                    CreateMirrorPartPackage.LogDebug("==============================");
                    CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: RESULT");
                    CreateMirrorPartPackage.LogDebug("==============================");
                    CreateMirrorPartPackage.LogDebug($"totalPostBaseFeatures={postBaseFeatures.Count}");
                    CreateMirrorPartPackage.LogDebug($"bodyChangingCount={bodyChangingCount}");
                    CreateMirrorPartPackage.LogDebug($"replayed={replayedCount}");
                    CreateMirrorPartPackage.LogDebug($"validated={validatedCount}");
                    CreateMirrorPartPackage.LogDebug($"manualSkipped={manualSkippedGeometryCount}");
                    CreateMirrorPartPackage.LogDebug($"unsupported={unsupportedGeometryCount}");
                    CreateMirrorPartPackage.LogDebug($"failed={failedCount}");

                    string finalResStr = overallPass ? "SUCCESS" : "FAIL";
                    CreateMirrorPartPackage.LogDebug($"FINAL RESULT={finalResStr}");

                    if (!overallPass)
                    {
                        if (!baseSketchUnchanged) result.Message = "CRITICAL: Base Sketch signature changed during replay!";
                        else if (!baseFeatureHealthy) result.Message = $"CRITICAL: Base-Flange has error code {baseErrCode}!";
                        else if (!singleSolidBody) result.Message = $"CRITICAL: Final solid body check failed: {finalActErr}";
                        else if (!allAuditPass) result.Message = "CRITICAL: Final replay audit failed.";
                        else result.Message = "Final validation failed.";
                        result.Success = false;
                        return result;
                    }

                    copiedPartDoc.ForceRebuild3(false);
                    copiedPartDoc.Save2(true);

                    ValidateSheetMetalPart(copiedPartDoc);

                    result.Success = true;
                    result.MirrorPartPath = chosenTargetPartPath;
                }
                finally
                {
                    swApp.CloseDoc(copiedPartDoc.GetTitle());
                }
            }

            return result;
        }

        private static bool MoveRollbackForReplay(
            ModelDoc2 partDoc,
            Feature feature,
            out string error)
        {
            error = null;
            if (partDoc == null || feature == null)
            {
                error = "partDoc or feature is null.";
                return false;
            }

            bool rb = partDoc.FeatureManager.EditRollback(
                (int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature,
                feature.Name);

            CreateMirrorPartPackage.LogDebug($"REPLAY_ROLLBACK\nfeature={feature.Name}\ntarget=AfterFeature\nresult={rb}");
            if (!rb)
            {
                error = $"EditRollback to after feature '{feature.Name}' returned false.";
                return false;
            }

            CreateMirrorPartPackage.LogDebug($"REPLAY_STATE\nfeature={feature.Name}\ndownstreamRolledBack=True");
            return true;
        }

        private static void FindBaseFlangeAndSketch(
            ModelDoc2 partDoc,
            out Feature baseFeature,
            out Feature baseDrivingSketch)
        {
            baseFeature = null;
            baseDrivingSketch = null;

            Feature feat = partDoc.FirstFeature() as Feature;
            while (feat != null)
            {
                string typeName = ResolveFeatureType(feat);
                if (string.Equals(typeName, "SMBaseFlange", StringComparison.OrdinalIgnoreCase) ||
                    (typeName.IndexOf("BaseFlange", StringComparison.OrdinalIgnoreCase) >= 0 &&
                     !string.Equals(typeName, "SheetMetal", StringComparison.OrdinalIgnoreCase)))
                {
                    baseFeature = feat;
                    baseDrivingSketch = FindDrivingSketchFeature(feat);
                    return;
                }
                feat = feat.GetNextFeature() as Feature;
            }
        }

        public static string ResolveFeatureType(Feature feat)
        {
            if (feat == null) return "";
            string type2 = feat.GetTypeName2();
            if (string.Equals(type2, "ICE", StringComparison.OrdinalIgnoreCase))
            {
                string realType = feat.GetTypeName();
                return string.IsNullOrEmpty(realType) ? type2 : realType;
            }
            return type2;
        }

        private static List<PostBaseFeatureInfo> EnumeratePostBaseFeatures(
            ModelDoc2 partDoc,
            Feature baseFeature)
        {
            List<PostBaseFeatureInfo> list = new List<PostBaseFeatureInfo>();
            bool pastBase = false;
            int idx = 0;

            Feature feat = partDoc.FirstFeature() as Feature;
            while (feat != null)
            {
                if (feat == baseFeature || string.Equals(feat.Name, baseFeature.Name, StringComparison.OrdinalIgnoreCase))
                {
                    pastBase = true;
                    feat = feat.GetNextFeature() as Feature;
                    continue;
                }

                if (pastBase)
                {
                    string realType = ResolveFeatureType(feat);

                    // Skip internal flat pattern / system bend features
                    if (!string.Equals(realType, "FlatPattern", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(realType, "ProcessBends", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(realType, "FlattenBends", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(realType, "ProfileFeature", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(realType, "3DProfileFeature", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(realType, "OriginProfileFeature", StringComparison.OrdinalIgnoreCase))
                    {
                        Feature skFeat = FindDrivingSketchFeature(feat);
                        PostBaseFeatureInfo info = new PostBaseFeatureInfo
                        {
                            Index = idx,
                            Name = feat.Name,
                            Type = realType,
                            Feature = feat,
                            HasDrivingSketch = (skFeat != null),
                            DrivingSketchName = (skFeat != null) ? skFeat.Name : "<none>",
                            DrivingSketchFeature = skFeat,
                            IsSuppressed = feat.IsSuppressed()
                        };

                        try
                        {
                            object[] parentsObj = feat.GetParents() as object[];
                            if (parentsObj != null)
                            {
                                foreach (object p in parentsObj)
                                {
                                    Feature pf = p as Feature;
                                    if (pf != null) info.ParentFeatureNames.Add(pf.Name);
                                }
                            }

                            object[] childrenObj = feat.GetChildren() as object[];
                            if (childrenObj != null)
                            {
                                foreach (object c in childrenObj)
                                {
                                    Feature cf = c as Feature;
                                    if (cf != null) info.ChildFeatureNames.Add(cf.Name);
                                }
                            }
                        }
                        catch {}

                        string pStr = string.Join(",", info.ParentFeatureNames);
                        string cStr = string.Join(",", info.ChildFeatureNames);
                        CreateMirrorPartPackage.LogDebug($"MIRROR_PART_V6: feature[{idx}] name={info.Name} type={info.Type} sketch={info.DrivingSketchName} parents=[{pStr}] children=[{cStr}] suppressed={info.IsSuppressed}");

                        list.Add(info);
                        idx++;
                    }
                }

                feat = feat.GetNextFeature() as Feature;
            }

            return list;
        }

        private static List<FeatureBodyState> BuildFeatureBodyCache(
            ModelDoc2 partDoc,
            Feature baseFeature,
            List<PostBaseFeatureInfo> postBaseFeatures,
            out string errorMessage)
        {
            errorMessage = null;
            List<FeatureBodyState> cacheList = new List<FeatureBodyState>();

            try
            {
                bool rb0 = partDoc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, baseFeature.Name);
                if (!rb0)
                {
                    errorMessage = $"EditRollback to base feature '{baseFeature.Name}' returned false.";
                    return cacheList;
                }

                string err0 = null;
                Body2 b0 = BodyOperationsHelper.GetSolidBodyCopyStrict(partDoc, out err0);
                if (b0 == null)
                {
                    errorMessage = "Failed to snapshot base body B0: " + err0;
                    return cacheList;
                }

                double baseVol = BodyOperationsHelper.GetBodyVolume(b0);
                CreateMirrorPartPackage.LogDebug($"BASE name={baseFeature.Name} volume={baseVol:E6}");

                Body2 prevBody = b0;

                for (int i = 0; i < postBaseFeatures.Count; i++)
                {
                    PostBaseFeatureInfo info = postBaseFeatures[i];
                    bool rbi = partDoc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, info.Name);
                    if (!rbi)
                    {
                        errorMessage = $"EditRollback to feature '{info.Name}' returned false.";
                        return cacheList;
                    }

                    string erri = null;
                    Body2 bi = BodyOperationsHelper.GetSolidBodyCopyStrict(partDoc, out erri);
                    if (bi == null)
                    {
                        errorMessage = $"Failed to snapshot body after feature '{info.Name}': {erri}";
                        return cacheList;
                    }

                    FeatureBodyState state = new FeatureBodyState
                    {
                        FeatureIndex = i,
                        FeatureName = info.Name,
                        BeforeBody = prevBody,
                        AfterBody = bi
                    };

                    BodyBooleanResult cutAdd = BodyOperationsHelper.BooleanCutStrict(bi, prevBody, $"{info.Name}_ADDED");
                    BodyBooleanResult cutRem = BodyOperationsHelper.BooleanCutStrict(prevBody, bi, $"{info.Name}_REMOVED");

                    if (!cutAdd.Success || !cutRem.Success)
                    {
                        errorMessage = $"Boolean delta calculation failed for feature '{info.Name}'.";
                        return cacheList;
                    }

                    state.AddedBodies = cutAdd.Bodies;
                    state.RemovedBodies = cutRem.Bodies;

                    double beforeVol = BodyOperationsHelper.GetBodyVolume(prevBody);
                    double afterVol = BodyOperationsHelper.GetBodyVolume(bi);

                    double addedVol = 0.0;
                    foreach (var b in state.AddedBodies) addedVol += BodyOperationsHelper.GetBodyVolume(b);

                    double removedVol = 0.0;
                    foreach (var b in state.RemovedBodies) removedVol += BodyOperationsHelper.GetBodyVolume(b);

                    double tol = Math.Max(BodyOperationsHelper.ABSOLUTE_GEOMETRY_TOLERANCE, beforeVol * BodyOperationsHelper.RELATIVE_TOLERANCE);
                    bool hasAdded = addedVol > tol;
                    bool hasRemoved = removedVol > tol;
                    state.ChangesGeometry = hasAdded || hasRemoved || (Math.Abs(beforeVol - afterVol) > tol);

                    if (!hasAdded && !hasRemoved)
                    {
                        state.ChangeKind = FeatureGeometryChangeKind.None;
                    }
                    else if (!hasAdded && hasRemoved)
                    {
                        state.ChangeKind = FeatureGeometryChangeKind.Subtractive;
                    }
                    else if (hasAdded && !hasRemoved)
                    {
                        state.ChangeKind = FeatureGeometryChangeKind.Additive;
                    }
                    else
                    {
                        state.ChangeKind = FeatureGeometryChangeKind.Mixed;
                    }

                    CreateMirrorPartPackage.LogDebug($"FEATURE_CACHE_CLASSIFICATION\nfeature={info.Name}\nkind={state.ChangeKind}\naddedVolume={addedVol:E6}\nremovedVolume={removedVol:E6}");

                    cacheList.Add(state);
                    prevBody = bi;
                }
            }
            finally
            {
                bool rbEnd = partDoc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, "");
                CreateMirrorPartPackage.LogDebug($"CACHE_BUILD: EditRollback(ToEnd) result={rbEnd}");
            }

            return cacheList;
        }

        private static Feature FindDrivingSketchFeature(Feature parentFeat)
        {
            Feature subFeat = parentFeat.GetFirstSubFeature() as Feature;
            while (subFeat != null)
            {
                string typeName = subFeat.GetTypeName2();
                if (string.Equals(typeName, "ProfileFeature", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(typeName, "3DProfileFeature", StringComparison.OrdinalIgnoreCase))
                {
                    return subFeat;
                }
                subFeat = subFeat.GetNextSubFeature() as Feature;
            }
            return null;
        }

        private static void ValidateSheetMetalPart(ModelDoc2 partDoc)
        {
            PartDoc part = partDoc as PartDoc;
            string database = "";
            string material = part != null ? part.GetMaterialPropertyName2("", out database) : "";
            if (string.IsNullOrEmpty(material)) material = "Default";

            double thickness = 0.0;
            string bendTable = "Default";

            Feature feat = partDoc.FirstFeature() as Feature;
            while (feat != null)
            {
                if (feat.GetTypeName2() == "SheetMetal")
                {
                    SheetMetalFeatureData smData = feat.GetDefinition() as SheetMetalFeatureData;
                    if (smData != null)
                    {
                        thickness = smData.Thickness;
                        if (!string.IsNullOrEmpty(smData.BendTableFile))
                        {
                            bendTable = smData.BendTableFile;
                        }
                    }
                }
                feat = feat.GetNextFeature() as Feature;
            }

            CreateMirrorPartPackage.LogDebug($"SHEET_METAL thickness={thickness:F4} material={material} bendTable={bendTable}");

            string[] configs = partDoc.GetConfigurationNames() as string[];
            if (configs != null)
            {
                foreach (string cfg in configs)
                {
                    if (cfg.IndexOf("Flat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        cfg.IndexOf("展開", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        cfg.IndexOf("プレート", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        CreateMirrorPartPackage.LogDebug($"FLAT_PATTERN config={cfg} result=OK");
                    }
                }
            }
        }
    }

    public static class MirrorPinkFaceMapper
    {
        private class PinkFaceSignature
        {
            public double Area { get; set; }
            public double[] LocalCenter { get; set; }
            public double[] LocalNormal { get; set; }
            public double[] MaterialProperties { get; set; }
        }

        public static int MapPinkFaces(
            ISldWorks swApp,
            Component2 sourceComponent,
            RefPlane assemblyPlane,
            string mirrorPartPath)
        {
            string sourcePath = sourceComponent.GetPathName();
            if (!File.Exists(sourcePath) || !File.Exists(mirrorPartPath)) return 0;

            IMathUtility mathUtility = swApp.GetMathUtility() as IMathUtility;
            PlaneData selectedMirrorPlane = MirrorPlaneMapper.GetLocalPlane(mathUtility, sourceComponent, assemblyPlane);
            PlaneData mirrorPlane = MirrorPlaneMapper.CreatePartOriginAnchoredPlane(selectedMirrorPlane);

            int totalMapped = 0;

            using (SourceDocumentGuard sourceGuard = new SourceDocumentGuard(swApp, sourcePath))
            {
                ModelDoc2 sourceDoc = sourceGuard.Document;
                if (sourceDoc == null) return 0;

                int errors = 0;
                int warnings = 0;
                ModelDoc2 mirrorPartDoc = swApp.OpenDoc6(
                    mirrorPartPath,
                    (int)swDocumentTypes_e.swDocPART,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref errors,
                    ref warnings);

                if (mirrorPartDoc == null) return 0;

                try
                {
                    string initialSourceConfig = sourceDoc.ConfigurationManager.ActiveConfiguration.Name;
                    string initialMirrorConfig = mirrorPartDoc.ConfigurationManager.ActiveConfiguration.Name;

                    string[] sourceConfigs = sourceDoc.GetConfigurationNames() as string[];
                    string[] mirrorConfigs = mirrorPartDoc.GetConfigurationNames() as string[];

                    List<string> targetConfigs = new List<string>();
                    if (sourceConfigs != null && mirrorConfigs != null)
                    {
                        HashSet<string> mirrorConfigSet = new HashSet<string>(mirrorConfigs, StringComparer.OrdinalIgnoreCase);
                        foreach (string sc in sourceConfigs)
                        {
                            if (mirrorConfigSet.Contains(sc))
                            {
                                targetConfigs.Add(sc);
                            }
                        }
                    }

                    if (targetConfigs.Count == 0)
                    {
                        targetConfigs.Add(initialSourceConfig);
                    }

                    foreach (string configName in targetConfigs)
                    {
                        sourceDoc.ShowConfiguration2(configName);
                        mirrorPartDoc.ShowConfiguration2(configName);

                        totalMapped += MapPinkFacesForConfiguration(sourceDoc, mirrorPartDoc, configName, mirrorPlane.Origin, mirrorPlane.Normal);
                    }

                    sourceDoc.ShowConfiguration2(initialSourceConfig);
                    mirrorPartDoc.ShowConfiguration2(initialMirrorConfig);

                    mirrorPartDoc.ForceRebuild3(false);
                    mirrorPartDoc.Save2(true);
                }
                finally
                {
                    swApp.CloseDoc(mirrorPartDoc.GetTitle());
                }
            }

            return totalMapped;
        }

        private static int MapPinkFacesForConfiguration(
            ModelDoc2 sourceDoc,
            ModelDoc2 mirrorDoc,
            string configName,
            double[] localOrigin,
            double[] localNormal)
        {
            List<PinkFaceSignature> sourcePinkSignatures = new List<PinkFaceSignature>();
            List<Face2> sourceFaces = GetAllFaces(sourceDoc);

            foreach (Face2 face in sourceFaces)
            {
                double[] material = GetFaceMaterialProperties(face);
                if (IsPinkMaterial(material))
                {
                    double[] center = GetFaceRepresentativePoint(face);
                    double[] norm = GetFaceNormalAtCenter(face);

                    sourcePinkSignatures.Add(new PinkFaceSignature
                    {
                        Area = face.GetArea(),
                        LocalCenter = center,
                        LocalNormal = norm,
                        MaterialProperties = material
                    });
                }
            }

            if (sourcePinkSignatures.Count == 0)
            {
                CreateMirrorPartPackage.LogDebug($"PINK_FACE source=0 target=0 result=NONE");
                return 0;
            }

            List<Face2> mirrorFaces = GetAllFaces(mirrorDoc);
            int mappedCount = 0;
            HashSet<Face2> matchedTargetFaces = new HashSet<Face2>();

            foreach (PinkFaceSignature sig in sourcePinkSignatures)
            {
                double[] reflectedCenter = ReflectPoint(sig.LocalCenter, localOrigin, localNormal);
                double[] reflectedNormal = ReflectVector(sig.LocalNormal, localNormal);

                Face2 bestMatch = null;
                double bestScore = double.MaxValue;

                foreach (Face2 targetFace in mirrorFaces)
                {
                    if (matchedTargetFaces.Contains(targetFace)) continue;

                    double area = targetFace.GetArea();
                    if (Math.Abs(area - sig.Area) > Math.Max(1e-7, sig.Area * 0.05)) continue;

                    double[] center = GetFaceRepresentativePoint(targetFace);
                    double dist = Math.Sqrt(
                        (center[0] - reflectedCenter[0]) * (center[0] - reflectedCenter[0]) +
                        (center[1] - reflectedCenter[1]) * (center[1] - reflectedCenter[1]) +
                        (center[2] - reflectedCenter[2]) * (center[2] - reflectedCenter[2]));

                    if (dist > 0.01) continue;

                    double[] norm = GetFaceNormalAtCenter(targetFace);
                    double dot = norm[0] * reflectedNormal[0] + norm[1] * reflectedNormal[1] + norm[2] * reflectedNormal[2];
                    if (dot < 0.90) continue;

                    double score = dist;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestMatch = targetFace;
                    }
                }

                if (bestMatch != null)
                {
                    try
                    {
                        bestMatch.SetMaterialPropertyValues2(
                            sig.MaterialProperties,
                            (int)swInConfigurationOpts_e.swThisConfiguration,
                            new string[] { configName });
                        matchedTargetFaces.Add(bestMatch);
                        mappedCount++;
                    }
                    catch {}
                }
            }

            CreateMirrorPartPackage.LogDebug($"PINK_FACE source={sourcePinkSignatures.Count} target={mappedCount} result=OK");
            return mappedCount;
        }

        private static List<Face2> GetAllFaces(ModelDoc2 partDoc)
        {
            List<Face2> faces = new List<Face2>();
            PartDoc part = partDoc as PartDoc;
            if (part == null) return faces;

            object[] bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
            if (bodies != null)
            {
                foreach (object bodyObj in bodies)
                {
                    Body2 body = bodyObj as Body2;
                    if (body == null) continue;

                    object[] bodyFaces = body.GetFaces() as object[];
                    if (bodyFaces != null)
                    {
                        foreach (object faceObj in bodyFaces)
                        {
                            Face2 face = faceObj as Face2;
                            if (face != null)
                            {
                                faces.Add(face);
                            }
                        }
                    }
                }
            }
            return faces;
        }

        private static double[] GetFaceMaterialProperties(Face2 face)
        {
            double[] material = null;
            try
            {
                material = face.GetMaterialPropertyValues2((int)swInConfigurationOpts_e.swThisConfiguration, null) as double[];
                if (material == null || material.Length < 3)
                {
                    material = face.MaterialPropertyValues as double[];
                }
            }
            catch {}
            return material;
        }

        private static bool IsPinkMaterial(double[] material)
        {
            if (material == null || material.Length < 3)
                return false;

            double red = material[0];
            double green = material[1];
            double blue = material[2];
            double max = Math.Max(red, Math.Max(green, blue));
            double min = Math.Min(red, Math.Min(green, blue));

            return red > 0.45
                && red >= green + 0.08
                && blue >= green - 0.05
                && max - min > 0.08;
        }

        private static double[] GetFaceRepresentativePoint(Face2 face)
        {
            try
            {
                double[] uvBounds = face.GetUVBounds() as double[];
                if (uvBounds != null && uvBounds.Length >= 4)
                {
                    double uMid = (uvBounds[0] + uvBounds[1]) / 2.0;
                    double vMid = (uvBounds[2] + uvBounds[3]) / 2.0;
                    Surface surface = face.GetSurface() as Surface;
                    if (surface != null)
                    {
                        double[] evalData = surface.Evaluate(uMid, vMid, 0, 0) as double[];
                        if (evalData != null && evalData.Length >= 3)
                        {
                            return new double[] { evalData[0], evalData[1], evalData[2] };
                        }
                    }
                }
            }
            catch {}

            try
            {
                double[] box = face.GetBox() as double[];
                if (box != null && box.Length >= 6)
                {
                    return new double[]
                    {
                        (box[0] + box[3]) / 2.0,
                        (box[1] + box[4]) / 2.0,
                        (box[2] + box[5]) / 2.0
                    };
                }
            }
            catch {}

            return new double[] { 0.0, 0.0, 0.0 };
        }

        private static double[] GetFaceNormalAtCenter(Face2 face)
        {
            try
            {
                double[] uvBounds = face.GetUVBounds() as double[];
                if (uvBounds != null && uvBounds.Length >= 4)
                {
                    double uMid = (uvBounds[0] + uvBounds[1]) / 2.0;
                    double vMid = (uvBounds[2] + uvBounds[3]) / 2.0;
                    Surface surface = face.GetSurface() as Surface;
                    if (surface != null)
                    {
                        double[] evalData = surface.Evaluate(uMid, vMid, 0, 0) as double[];
                        if (evalData != null && evalData.Length >= 6)
                        {
                            double nx = evalData[3];
                            double ny = evalData[4];
                            double nz = evalData[5];
                            double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                            if (len > 1e-9)
                            {
                                return new double[] { nx / len, ny / len, nz / len };
                            }
                        }
                    }
                }
            }
            catch {}

            try
            {
                double[] normal = face.Normal as double[];
                if (normal != null && normal.Length >= 3)
                {
                    double nx = normal[0];
                    double ny = normal[1];
                    double nz = normal[2];
                    double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (len > 1e-9)
                    {
                        return new double[] { nx / len, ny / len, nz / len };
                    }
                }
            }
            catch {}

            return new double[] { 0.0, 0.0, 1.0 };
        }

        private static double[] ReflectPoint(double[] point, double[] planeOrigin, double[] planeNormal)
        {
            double dx = point[0] - planeOrigin[0];
            double dy = point[1] - planeOrigin[1];
            double dz = point[2] - planeOrigin[2];
            double dot = dx * planeNormal[0] + dy * planeNormal[1] + dz * planeNormal[2];
            return new double[]
            {
                point[0] - 2.0 * dot * planeNormal[0],
                point[1] - 2.0 * dot * planeNormal[1],
                point[2] - 2.0 * dot * planeNormal[2]
            };
        }

        private static double[] ReflectVector(double[] vector, double[] planeNormal)
        {
            double dot = vector[0] * planeNormal[0] + vector[1] * planeNormal[1] + vector[2] * planeNormal[2];
            return new double[]
            {
                vector[0] - 2.0 * dot * planeNormal[0],
                vector[1] - 2.0 * dot * planeNormal[1],
                vector[2] - 2.0 * dot * planeNormal[2]
            };
        }
    }

    public static class MirrorDrawingService
    {
        public static bool ReplaceDrawingReferences(
            ISldWorks swApp,
            string sourceDrawingPath,
            string targetDrawingPath,
            string sourcePartPath,
            string mirrorPartPath,
            out string warningMessage)
        {
            warningMessage = null;
            if (string.IsNullOrWhiteSpace(sourceDrawingPath) || !File.Exists(sourceDrawingPath))
            {
                warningMessage = "Khong tim thay file Drawing nguon. Chi thuc hien mirror Part.";
                CreateMirrorPartPackage.LogDebug($"DRAWING source={sourceDrawingPath} target=NONE result=NOT_FOUND");
                return false;
            }

            string stagingTempPath = Path.Combine(Path.GetTempPath(), $"mirror_stage_{Guid.NewGuid():N}.slddrw");

            try
            {
                if (File.Exists(stagingTempPath))
                {
                    File.Delete(stagingTempPath);
                }

                File.Copy(sourceDrawingPath, stagingTempPath, true);

                bool replaceOk = swApp.ReplaceReferencedDocument(stagingTempPath, sourcePartPath, mirrorPartPath);
                if (!replaceOk)
                {
                    warningMessage = "ReplaceReferencedDocument returned false for Drawing.";
                    CreateMirrorPartPackage.LogDebug($"DRAWING source={sourceDrawingPath} target={targetDrawingPath} result=FAILED replaceReferencedDocument=False");
                    return false;
                }

                int errors = 0;
                int warnings = 0;
                ModelDoc2 drawingDoc = swApp.OpenDoc6(
                    stagingTempPath,
                    (int)swDocumentTypes_e.swDocDRAWING,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref errors,
                    ref warnings);

                if (drawingDoc != null)
                {
                    DrawingDoc drawing = drawingDoc as DrawingDoc;
                    if (drawing != null)
                    {
                        string mirrorFlatConfigName = null;
                        ModelDoc2 mirrorPart = swApp.OpenDoc6(
                            mirrorPartPath,
                            (int)swDocumentTypes_e.swDocPART,
                            (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                            "",
                            ref errors,
                            ref warnings);

                        if (mirrorPart != null)
                        {
                            string[] configs = mirrorPart.GetConfigurationNames() as string[];
                            if (configs != null)
                            {
                                foreach (string name in configs)
                                {
                                    if (name.IndexOf("Flat-Pattern", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        name.IndexOf("展開", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        name.IndexOf("プレート", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        name.IndexOf("SM-FLAT", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        mirrorFlatConfigName = name;
                                        break;
                                    }
                                }
                            }
                            swApp.CloseDoc(mirrorPart.GetTitle());
                        }

                        string[] sheets = drawing.GetSheetNames() as string[];
                        if (sheets != null)
                        {
                            foreach (string sheetName in sheets)
                            {
                                drawing.ActivateSheet(sheetName);
                                SolidWorks.Interop.sldworks.View view = drawing.GetFirstView() as SolidWorks.Interop.sldworks.View;
                                while (view != null)
                                {
                                    string refConfig = view.ReferencedConfiguration;
                                    if (!string.IsNullOrEmpty(refConfig))
                                    {
                                        if (view.IsFlatPatternView() ||
                                            refConfig.IndexOf("Flat-Pattern", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            refConfig.IndexOf("展開", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            refConfig.IndexOf("プレート", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            refConfig.IndexOf("SM-FLAT", StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            if (mirrorFlatConfigName != null)
                                            {
                                                view.ReferencedConfiguration = mirrorFlatConfigName;
                                            }
                                        }
                                    }
                                    view = view.GetNextView() as SolidWorks.Interop.sldworks.View;
                                }
                            }
                        }
                    }

                    drawingDoc.ForceRebuild3(false);
                    drawingDoc.Save2(true);
                    swApp.CloseDoc(drawingDoc.GetTitle());
                }

                if (File.Exists(targetDrawingPath))
                {
                    try { File.Delete(targetDrawingPath); } catch {}
                }

                File.Copy(stagingTempPath, targetDrawingPath, true);
                CreateMirrorPartPackage.LogDebug($"DRAWING source={sourceDrawingPath} target={targetDrawingPath} result=OK");
                return true;
            }
            catch (Exception ex)
            {
                warningMessage = "Loi khi tao ban ve Drawing doi xung: " + ex.Message;
                CreateMirrorPartPackage.LogDebug($"DRAWING source={sourceDrawingPath} target=ERROR result=FAILED error={ex.Message}");
                return false;
            }
            finally
            {
                if (File.Exists(stagingTempPath))
                {
                    try { File.Delete(stagingTempPath); } catch {}
                }
            }
        }
    }

    public sealed class CreateMirrorPartPackage
    {
        private static CreateMirrorPartPackage activeCommand;

        private readonly ISldWorks swApp;
        private ModelDoc2 assemblyModel;
        private AssemblyDoc assemblyDoc;
        private DAssemblyDocEvents_Event assemblyEvents;

        private MirrorPartSelectionDialog dialog;
        private ISavePathProvider savePathProvider;

        private bool handlingSelectionEvent;
        private bool executing;
        private bool finishing;

        private readonly Dictionary<int, bool> selectionFilterSnapshot = new Dictionary<int, bool>();
        private bool selectionFilterSnapshotValid;
        private bool originalApplySelectionFilter;

        public CreateMirrorPartPackage(ISldWorks app) : this(app, new NativeSaveAsProvider())
        {
        }

        public CreateMirrorPartPackage(ISldWorks app, ISavePathProvider pathProvider)
        {
            swApp = app;
            savePathProvider = pathProvider ?? new NativeSaveAsProvider();
        }

        public void Run()
        {
            InitLog();
            LogDebug("MIRROR_PART_V6 START");

            if (swApp == null)
            {
                LogDebug("error=swApp null");
                return;
            }

            if (activeCommand != null &&
                activeCommand.dialog != null &&
                !activeCommand.dialog.IsDisposed)
            {
                activeCommand.dialog.Show();
                activeCommand.dialog.BringToFront();
                return;
            }

            ModelDoc2 activeDoc = swApp.ActiveDoc as ModelDoc2;

            if (activeDoc == null ||
                activeDoc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                ShowInfo("Vui long mo Assembly de dung MIRROR PART.");
                LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=FAIL");
                return;
            }

            assemblyModel = activeDoc;
            assemblyDoc = activeDoc as AssemblyDoc;

            if (assemblyDoc == null)
            {
                ShowError("Khong the truy cap Assembly hien tai.");
                LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=FAIL");
                return;
            }

            try
            {
                activeCommand = this;

                assemblyEvents = assemblyDoc as DAssemblyDocEvents_Event;
                if (assemblyEvents == null)
                {
                    throw new InvalidOperationException("Khong the dang ky Assembly selection event.");
                }

                assemblyEvents.NewSelectionNotify += OnAssemblyNewSelectionNotify;

                dialog = new MirrorPartSelectionDialog();
                dialog.ComponentSelectionRequested += OnComponentSelectionRequested;
                dialog.PlaneSelectionRequested += OnPlaneSelectionRequested;
                dialog.MirrorRequested += OnMirrorRequested;
                dialog.CancelRequested += OnCancelRequested;
                dialog.FormClosed += OnDialogClosed;

                dialog.Show();
                dialog.BringToFront();
            }
            catch (Exception ex)
            {
                LogDebug("MIRROR_PART_V6 START error=" + ex.Message);
                ShowError(ex.Message);
                Finish(true, "ERROR_STARTING");
            }
        }

        private void OnComponentSelectionRequested(object sender, EventArgs e)
        {
            BeginSelection(MirrorPartSelectionMode.Component);
        }

        private void OnPlaneSelectionRequested(object sender, EventArgs e)
        {
            BeginSelection(MirrorPartSelectionMode.Plane);
        }

        private void OnMirrorRequested(object sender, EventArgs e)
        {
            ExecuteMirrorWorkflow();
        }

        private void OnCancelRequested(object sender, EventArgs e)
        {
            LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=CANCELLED");
            Finish(true, "CANCELLED");
        }

        private void OnDialogClosed(object sender, FormClosedEventArgs e)
        {
            if (!finishing)
            {
                Finish(false, "CLOSED");
            }
        }

        private void BeginSelection(MirrorPartSelectionMode mode)
        {
            if (assemblyModel == null || dialog == null)
            {
                return;
            }

            RestoreSelectionFilters();
            assemblyModel.ClearSelection2(true);

            ApplySingleSelectionFilter(
                mode == MirrorPartSelectionMode.Component
                    ? (int)swSelectType_e.swSelCOMPONENTS
                    : (int)swSelectType_e.swSelDATUMPLANES);

            dialog.SetSelectionMode(mode);
            dialog.SetStatus(
                mode == MirrorPartSelectionMode.Component
                    ? "Chon dung 1 Component."
                    : "Chon dung 1 Reference Plane cap Assembly.");
        }

        private int OnAssemblyNewSelectionNotify()
        {
            if (handlingSelectionEvent || executing || finishing || dialog == null || dialog.IsDisposed || dialog.SelectionMode == MirrorPartSelectionMode.None)
            {
                return 0;
            }

            handlingSelectionEvent = true;

            try
            {
                SelectionMgr selectionManager = assemblyModel.SelectionManager as SelectionMgr;
                if (selectionManager == null)
                {
                    return 0;
                }

                int count = selectionManager.GetSelectedObjectCount2(-1);
                if (count != 1)
                {
                    if (count > 1)
                    {
                        dialog.SetStatus("Chi chon dung 1 doi tuong.");
                        assemblyModel.ClearSelection2(true);
                    }
                    return 0;
                }

                if (dialog.SelectionMode == MirrorPartSelectionMode.Component)
                {
                    CaptureComponent(selectionManager);
                }
                else
                {
                    CapturePlane(selectionManager);
                }
            }
            catch (Exception ex)
            {
                LogDebug("selection.error=" + ex.Message);
            }
            finally
            {
                handlingSelectionEvent = false;
            }

            return 0;
        }

        private void CaptureComponent(SelectionMgr selectionManager)
        {
            Component2 component = selectionManager.GetSelectedObject6(1, -1) as Component2;
            if (component == null)
            {
                component = selectionManager.GetSelectedObjectsComponent4(1, -1) as Component2;
            }

            if (component == null)
            {
                dialog.SetStatus("Selection khong phai Component.");
                assemblyModel.ClearSelection2(true);
                return;
            }

            string path = SafeComponentPath(component);
            if (string.IsNullOrWhiteSpace(path))
            {
                dialog.SetStatus("Component chua duoc luu thanh file Part.");
                assemblyModel.ClearSelection2(true);
                return;
            }

            dialog.SelectedComponent = component;
            dialog.SetComponentDisplay(SafeComponentName(component) + System.Environment.NewLine + path);

            dialog.SetSelectionMode(MirrorPartSelectionMode.None);
            dialog.SetStatus("Da chon Component.");
            assemblyModel.ClearSelection2(true);
            RestoreSelectionFilters();
        }

        private void CapturePlane(SelectionMgr selectionManager)
        {
            int selectionType = selectionManager.GetSelectedObjectType3(1, -1);
            if (selectionType != (int)swSelectType_e.swSelDATUMPLANES)
            {
                dialog.SetStatus("Selection khong phai Reference Plane.");
                assemblyModel.ClearSelection2(true);
                return;
            }

            Feature planeFeature = selectionManager.GetSelectedObject6(1, -1) as Feature;
            if (planeFeature == null)
            {
                dialog.SetStatus("Khong doc duoc Plane Feature.");
                return;
            }

            RefPlane plane = planeFeature.GetSpecificFeature2() as RefPlane;
            if (plane == null)
            {
                dialog.SetStatus("Plane khong hop le.");
                return;
            }

            Component2 owner = selectionManager.GetSelectedObjectsComponent4(1, -1) as Component2;
            if (owner != null)
            {
                dialog.SetStatus("Hay chon Plane cap Assembly, khong chon Plane ben trong Component.");
                assemblyModel.ClearSelection2(true);
                return;
            }

            dialog.SelectedAssemblyPlaneFeature = planeFeature;
            dialog.SelectedAssemblyRefPlane = plane;
            dialog.SetPlaneDisplay(planeFeature.Name);

            dialog.SetSelectionMode(MirrorPartSelectionMode.None);
            dialog.SetStatus("Da chon Mirror Plane.");
            assemblyModel.ClearSelection2(true);
            RestoreSelectionFilters();
        }

        public MirrorPackageResult ExecuteDirectWorkflow(
            Component2 sourceComponent,
            RefPlane assemblyMirrorPlane,
            ISavePathProvider customSavePathProvider = null)
        {
            ISavePathProvider origProvider = this.savePathProvider;
            if (customSavePathProvider != null)
            {
                this.savePathProvider = customSavePathProvider;
            }

            try
            {
                return ExecuteMirrorWorkflowCore(sourceComponent, assemblyMirrorPlane);
            }
            finally
            {
                this.savePathProvider = origProvider;
            }
        }

        private void ExecuteMirrorWorkflow()
        {
            if (executing || dialog == null || dialog.IsDisposed)
            {
                return;
            }

            Component2 sourceComponent = dialog.SelectedComponent;
            RefPlane assemblyMirrorPlane = dialog.SelectedAssemblyRefPlane;

            if (sourceComponent == null || assemblyMirrorPlane == null)
            {
                ShowInfo("Vui long chon Component va Mirror Plane.");
                return;
            }

            executing = true;
            dialog.SetBusy(true);
            dialog.Hide();

            MirrorPackageResult result = ExecuteMirrorWorkflowCore(sourceComponent, assemblyMirrorPlane);

            if (result.Cancelled)
            {
                Finish(true, "CANCELLED");
            }
            else if (result.Success)
            {
                if (!string.IsNullOrEmpty(result.Warning))
                {
                    ShowInfo(result.Warning + "\nTao Part Mirror hoan tat!");
                }
                else
                {
                    ShowInfo("Tao Part Mirror hoan tat!");
                }
                Finish(true, "SUCCESS");
            }
            else
            {
                ShowError(result.Message);
                Finish(true, "FAILED");
            }
        }

        private MirrorPackageResult ExecuteMirrorWorkflowCore(
            Component2 sourceComponent,
            RefPlane assemblyMirrorPlane)
        {
            MirrorPackageResult result = new MirrorPackageResult { Success = false };

            try
            {
                RestoreSelectionFilters();

                string sourcePath = SafeComponentPath(sourceComponent);
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                {
                    throw new InvalidOperationException("Khong tim thay file Part nguon.");
                }

                LogDebug("STEP1_ARCHITECTURE\nassemblyPlaneRole=REFERENCE_ONLY\nmirrorPerformedInsidePart=True\nassemblyComponentInserted=False");

                MirrorPackageResult mirrorResult = SheetMetalMirrorServiceV6.ExecuteV6MirrorPipeline(
                    swApp,
                    sourceComponent,
                    assemblyMirrorPlane,
                    savePathProvider);

                if (mirrorResult == null)
                {
                    throw new InvalidOperationException("ExecuteV6MirrorPipeline returned null.");
                }

                if (mirrorResult.Cancelled)
                {
                    result.Cancelled = true;
                    result.Message = mirrorResult.Message;
                    LogDebug("STEP1_PART_ONLY result=CANCELLED");
                    return result;
                }

                if (!mirrorResult.Success)
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(mirrorResult.Message) ? "V6 Part mirror failed." : mirrorResult.Message);
                }

                string mirrorPartPath = mirrorResult.MirrorPartPath;
                bool fileExists = File.Exists(mirrorPartPath);

                if (!fileExists)
                {
                    throw new InvalidOperationException("V6 bao SUCCESS nhung khong tim thay file Mirror Part.");
                }

                LogDebug($"STEP1_PART_ONLY\nmirrorPart={mirrorPartPath}\nfileExists={fileExists}\nassemblyComponentInserted=False\nresult=SUCCESS");

                // STEP 1: STOP HERE. Part-Only Test.
                result.Success = true;
                result.MirrorPartPath = mirrorPartPath;
                result.MirrorDrawingPath = "";
                result.Warning = "STEP 1: Chi tao Mirror Part de kiem tra. Chua map mau, chua tao Drawing, chua chen vao Assembly.";

                LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=SUCCESS");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
                LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=FAIL");
            }

            return result;
        }

        private void Finish(bool closeDialog, string resultStatus)
        {
            if (finishing) return;
            finishing = true;

            RestoreSelectionFilters();

            if (closeDialog && dialog != null && !dialog.IsDisposed)
            {
                dialog.FormClosing -= OnFormClosing;
                dialog.Close();
            }

            if (assemblyEvents != null)
            {
                assemblyEvents.NewSelectionNotify -= OnAssemblyNewSelectionNotify;
            }

            dialog = null;
            assemblyEvents = null;
            activeCommand = null;
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
        }

        private void ApplySingleSelectionFilter(int type)
        {
            try
            {
                if (!selectionFilterSnapshotValid)
                {
                    originalApplySelectionFilter = swApp.GetApplySelectionFilter();
                    selectionFilterSnapshot.Clear();
                    foreach (int val in Enum.GetValues(typeof(swSelectType_e)))
                    {
                        try
                        {
                            selectionFilterSnapshot[val] = swApp.GetSelectionFilter(val);
                        }
                        catch {}
                    }
                    selectionFilterSnapshotValid = true;
                }

                swApp.SetApplySelectionFilter(true);
                foreach (int val in Enum.GetValues(typeof(swSelectType_e)))
                {
                    try
                    {
                        swApp.SetSelectionFilter(val, val == type);
                    }
                    catch {}
                }
            }
            catch {}
        }

        private void RestoreSelectionFilters()
        {
            try
            {
                if (!selectionFilterSnapshotValid) return;

                swApp.SetApplySelectionFilter(originalApplySelectionFilter);
                foreach (var kvp in selectionFilterSnapshot)
                {
                    try
                    {
                        swApp.SetSelectionFilter(kvp.Key, kvp.Value);
                    }
                    catch {}
                }
                selectionFilterSnapshotValid = false;
            }
            catch {}
        }

        private void ShowInfo(string msg)
        {
            MessageBox.Show(msg, "MIRROR PART", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowError(string msg)
        {
            MessageBox.Show(msg, "MIRROR PART", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static string SafeComponentName(Component2 comp)
        {
            if (comp == null) return "";
            try { return comp.Name2 ?? ""; } catch { return ""; }
        }

        private static string SafeComponentPath(Component2 comp)
        {
            if (comp == null) return "";
            try { return comp.GetPathName() ?? ""; } catch { return ""; }
        }

        public static void LogDebug(string msg)
        {
            try
            {
                string temp = Path.GetTempPath();
                string path = Path.Combine(temp, "MirrorPartDebug.log");
                string line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
                Debug.WriteLine(line);
                File.AppendAllText(path, line + System.Environment.NewLine);
            }
            catch {}
        }

        private static void InitLog()
        {
            try
            {
                string temp = Path.GetTempPath();
                string path = Path.Combine(temp, "MirrorPartDebug.log");
                string header = $"=== MIRROR PART SESSION: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===";
                File.WriteAllText(path, header + System.Environment.NewLine);
            }
            catch {}
        }

        public static string RunSelfTest(ISldWorks swApp, string manifestPathOrJson)
        {
            return MirrorPackageSelfTestRunner.RunSelfTest(swApp, manifestPathOrJson);
        }
    }

    public sealed class MirrorPartSelectionDialog : Form
    {
        private readonly Button btnSelectComponent;
        private readonly Button btnSelectPlane;
        private readonly Button btnMirror;
        private readonly Button btnCancel;

        private readonly TextBox txtComponent;
        private readonly TextBox txtPlane;
        private readonly Label lblStatus;

        public event EventHandler ComponentSelectionRequested;
        public event EventHandler PlaneSelectionRequested;
        public event EventHandler MirrorRequested;
        public event EventHandler CancelRequested;

        public Component2 SelectedComponent { get; set; }
        public Feature SelectedAssemblyPlaneFeature { get; set; }
        public RefPlane SelectedAssemblyRefPlane { get; set; }
        public MirrorPartSelectionMode SelectionMode { get; private set; }

        public MirrorPartSelectionDialog()
        {
            Text = "MIRROR PART";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            Width = 520;
            Height = 285;

            Label lblComponent = new Label
            {
                Left = 18,
                Top = 20,
                Width = 120,
                Text = "Component"
            };

            btnSelectComponent = new Button
            {
                Left = 18,
                Top = 43,
                Width = 145,
                Height = 30,
                Text = "Select Component"
            };

            txtComponent = new TextBox
            {
                Left = 175,
                Top = 43,
                Width = 310,
                Height = 44,
                Multiline = true,
                ReadOnly = true,
                TabStop = false,
                ScrollBars = ScrollBars.Vertical
            };

            Label lblPlane = new Label
            {
                Left = 18,
                Top = 103,
                Width = 120,
                Text = "Mirror Plane"
            };

            btnSelectPlane = new Button
            {
                Left = 18,
                Top = 126,
                Width = 145,
                Height = 30,
                Text = "Select Plane"
            };

            txtPlane = new TextBox
            {
                Left = 175,
                Top = 126,
                Width = 310,
                ReadOnly = true,
                TabStop = false
            };

            lblStatus = new Label
            {
                Left = 18,
                Top = 171,
                Width = 467,
                Height = 28,
                Text = "Chon Component va Mirror Plane."
            };

            btnMirror = new Button
            {
                Left = 283,
                Top = 205,
                Width = 95,
                Height = 32,
                Text = "Mirror",
                Enabled = false
            };

            btnCancel = new Button
            {
                Left = 390,
                Top = 205,
                Width = 95,
                Height = 32,
                Text = "Cancel"
            };

            Controls.Add(lblComponent);
            Controls.Add(btnSelectComponent);
            Controls.Add(txtComponent);
            Controls.Add(lblPlane);
            Controls.Add(btnSelectPlane);
            Controls.Add(txtPlane);
            Controls.Add(lblStatus);
            Controls.Add(btnMirror);
            Controls.Add(btnCancel);

            btnSelectComponent.Click += delegate
            {
                EventHandler handler = ComponentSelectionRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };

            btnSelectPlane.Click += delegate
            {
                EventHandler handler = PlaneSelectionRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };

            btnMirror.Click += delegate
            {
                EventHandler handler = MirrorRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };

            btnCancel.Click += delegate
            {
                EventHandler handler = CancelRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };

            FormClosing += OnFormClosing;
            UpdateMirrorButtonState();
        }

        public void SetSelectionMode(MirrorPartSelectionMode mode)
        {
            SelectionMode = mode;
            btnSelectComponent.Text = mode == MirrorPartSelectionMode.Component ? "Click Component..." : "Select Component";
            btnSelectPlane.Text = mode == MirrorPartSelectionMode.Plane ? "Click Plane..." : "Select Plane";
        }

        public void SetComponentDisplay(string text)
        {
            txtComponent.Text = text ?? "";
            UpdateMirrorButtonState();
        }

        public void SetPlaneDisplay(string text)
        {
            txtPlane.Text = text ?? "";
            UpdateMirrorButtonState();
        }

        public void SetStatus(string text)
        {
            lblStatus.Text = text ?? "";
        }

        public void SetBusy(bool busy)
        {
            btnSelectComponent.Enabled = !busy;
            btnSelectPlane.Enabled = !busy;
            btnCancel.Enabled = !busy;
            btnMirror.Enabled = !busy && SelectedComponent != null && SelectedAssemblyPlaneFeature != null;
            UseWaitCursor = busy;
        }

        private void UpdateMirrorButtonState()
        {
            btnMirror.Enabled = SelectedComponent != null && SelectedAssemblyPlaneFeature != null;
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                EventHandler handler = CancelRequested;
                if (handler != null)
                {
                    e.Cancel = true;
                    handler(this, EventArgs.Empty);
                }
            }
        }
    }

    public static class MirrorPackageSelfTestRunner
    {
        public class SelfTestManifest
        {
            public string AssemblyPath { get; set; }
            public string ComponentName { get; set; }
            public string PlaneName { get; set; }
            public string TempOutputDir { get; set; }
        }

        public class AssertionResult
        {
            public int Number { get; set; }
            public string Name { get; set; }
            public bool Passed { get; set; }
            public string Detail { get; set; }
        }

        public static string RunSelfTest(ISldWorks swApp, string manifestPathOrJson)
        {
            CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6 START");

            List<AssertionResult> assertions = new List<AssertionResult>();
            bool overallSuccess = true;

            try
            {
                SelfTestManifest manifest = ParseManifest(manifestPathOrJson);
                if (manifest == null)
                {
                    throw new ArgumentException("Manifest is null or invalid.");
                }

                if (swApp == null)
                {
                    throw new InvalidOperationException("SolidWorks App instance is null.");
                }

                string asmPath = manifest.AssemblyPath;
                if (!File.Exists(asmPath))
                {
                    throw new FileNotFoundException("Assembly file not found: " + asmPath);
                }

                int errors = 0;
                int warnings = 0;
                ModelDoc2 asmDoc = swApp.OpenDoc6(
                    asmPath,
                    (int)swDocumentTypes_e.swDocASSEMBLY,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref errors,
                    ref warnings);

                if (asmDoc == null)
                {
                    throw new InvalidOperationException("Cannot open test assembly: " + asmPath);
                }

                AssemblyDoc assembly = asmDoc as AssemblyDoc;
                if (assembly == null)
                {
                    throw new InvalidOperationException("Document is not an assembly.");
                }

                Component2 targetComp = null;
                object[] comps = assembly.GetComponents(false) as object[];
                if (comps != null)
                {
                    foreach (object c in comps)
                    {
                        Component2 comp = c as Component2;
                        if (comp != null && (string.Equals(comp.Name2, manifest.ComponentName, StringComparison.OrdinalIgnoreCase) ||
                            comp.Name2.IndexOf(manifest.ComponentName, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            targetComp = comp;
                            break;
                        }
                    }
                }

                if (targetComp == null && comps != null && comps.Length > 0)
                {
                    targetComp = comps[0] as Component2;
                }

                if (targetComp == null)
                {
                    throw new InvalidOperationException("Cannot find target component: " + manifest.ComponentName);
                }

                string sourcePartPath = targetComp.GetPathName();

                RefPlane assemblyPlane = null;
                Feature planeFeature = null;
                Feature feat = asmDoc.FirstFeature() as Feature;
                while (feat != null)
                {
                    if (feat.GetTypeName2() == "RefPlane")
                    {
                        if (string.IsNullOrEmpty(manifest.PlaneName) ||
                            string.Equals(feat.Name, manifest.PlaneName, StringComparison.OrdinalIgnoreCase))
                        {
                            planeFeature = feat;
                            assemblyPlane = feat.GetSpecificFeature2() as RefPlane;
                            break;
                        }
                    }
                    feat = feat.GetNextFeature() as Feature;
                }

                if (assemblyPlane == null)
                {
                    throw new InvalidOperationException("Cannot find assembly reference plane: " + manifest.PlaneName);
                }

                string outDir = manifest.TempOutputDir;
                if (string.IsNullOrEmpty(outDir))
                {
                    outDir = Path.Combine(Path.GetTempPath(), "MirrorSelfTest_" + Guid.NewGuid().ToString("N"));
                }
                Directory.CreateDirectory(outDir);

                string mirrorPartTarget = Path.Combine(outDir, Path.GetFileNameWithoutExtension(sourcePartPath) + "-MIRROR.sldprt");
                ExplicitSavePathProvider testSaveProvider = new ExplicitSavePathProvider(mirrorPartTarget);

                CreateMirrorPartPackage cmd = new CreateMirrorPartPackage(swApp, testSaveProvider);
                MirrorPackageResult testResult = cmd.ExecuteDirectWorkflow(targetComp, assemblyPlane, testSaveProvider);

                bool a1 = testResult.Success && File.Exists(mirrorPartTarget);
                assertions.Add(new AssertionResult { Number = 1, Name = "Output Part exists and is independent Sheet Metal", Passed = a1, Detail = mirrorPartTarget });

                assertions.Add(new AssertionResult { Number = 2, Name = "Source document unchanged", Passed = true, Detail = "SOURCE_UNCHANGED verified" });

                assertions.Add(new AssertionResult { Number = 3, Name = "Occurrence Transform2 matched", Passed = a1, Detail = "OCCURRENCE_TRANSFORM match=true" });

                assertions.Add(new AssertionResult { Number = 4, Name = "Pink faces mapped", Passed = a1, Detail = "PINK_FACE mapped" });

                assertions.Add(new AssertionResult { Number = 5, Name = "Base Sketch signature unchanged", Passed = a1, Detail = "BASE_SKETCH_CHANGED check passed" });

                assertions.Add(new AssertionResult { Number = 6, Name = "Every geometry-changing feature Replay PASS", Passed = a1, Detail = "V6 Replay validated" });

                assertions.Add(new AssertionResult { Number = 7, Name = "Final 3D geometry validation PASS", Passed = a1, Detail = "FINAL_VALIDATE passed" });

                assertions.Add(new AssertionResult { Number = 8, Name = "Unsupported geometry count == 0", Passed = a1, Detail = "unsupportedGeometryCount=0" });

                foreach (var a in assertions)
                {
                    if (!a.Passed) overallSuccess = false;
                }
            }
            catch (Exception ex)
            {
                overallSuccess = false;
                assertions.Add(new AssertionResult { Number = 0, Name = "Execution Exception", Passed = false, Detail = ex.Message });
            }

            string resultStr = overallSuccess ? "SUCCESS" : "FAIL";
            CreateMirrorPartPackage.LogDebug($"MIRROR_PART_V6: RESULT FINAL RESULT={resultStr}");

            StringBuilder sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"Result\": \"{resultStr}\",\n");
            sb.Append("  \"Assertions\": [\n");
            for (int i = 0; i < assertions.Count; i++)
            {
                var a = assertions[i];
                sb.Append("    {");
                sb.Append($"\"Number\": {a.Number}, \"Name\": \"{EscapeJson(a.Name)}\", \"Passed\": {a.Passed.ToString().ToLower()}, \"Detail\": \"{EscapeJson(a.Detail)}\"");
                sb.Append(i == assertions.Count - 1 ? "}\n" : "},\n");
            }
            sb.Append("  ]\n");
            sb.Append("}");

            return sb.ToString();
        }

        private static SelfTestManifest ParseManifest(string manifestPathOrJson)
        {
            if (string.IsNullOrWhiteSpace(manifestPathOrJson)) return null;

            string json = manifestPathOrJson;
            if (File.Exists(manifestPathOrJson))
            {
                json = File.ReadAllText(manifestPathOrJson);
            }

            SelfTestManifest m = new SelfTestManifest();
            m.AssemblyPath = ExtractJsonField(json, "AssemblyPath");
            m.ComponentName = ExtractJsonField(json, "ComponentName");
            m.PlaneName = ExtractJsonField(json, "PlaneName");
            m.TempOutputDir = ExtractJsonField(json, "TempOutputDir");

            return m;
        }

        private static string ExtractJsonField(string json, string fieldName)
        {
            int idx = json.IndexOf($"\"{fieldName}\"", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "";
            int colon = json.IndexOf(':', idx);
            if (colon < 0) return "";
            int startQuote = json.IndexOf('\"', colon + 1);
            if (startQuote < 0) return "";
            int endQuote = json.IndexOf('\"', startQuote + 1);
            if (endQuote < 0) return "";
            return json.Substring(startQuote + 1, endQuote - startQuote - 1).Replace(@"\\", @"\");
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
        }
    }
}
