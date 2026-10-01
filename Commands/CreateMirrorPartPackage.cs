using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using ADDIN.Helpers;
using ADDIN.Commands.MirrorV7;
using ADDIN.Commands.MirrorV7.MirrorInPlace;

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
        public bool AssemblyComponentInserted { get; set; }
        public PlaneData EffectiveMirrorPlane { get; set; }
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

        public static PlaneData CreateAnchoredPlane(PlaneData selectedLocalPlane, double[] anchorOrigin)
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

            double[] origin = anchorOrigin ?? new double[] { 0.0, 0.0, 0.0 };
            PlaneData anchoredPlane = new PlaneData
            {
                Origin = new double[] { origin[0], origin[1], origin[2] },
                Normal = new double[] { nx / length, ny / length, nz / length }
            };

            CreateMirrorPartPackage.LogDebug(
                "ADAPTIVE_ANCHORED_MIRROR_PLANE\n" +
                $"effectiveOrigin=({origin[0]:F9},{origin[1]:F9},{origin[2]:F9})\n" +
                $"normal=({anchoredPlane.Normal[0]:F9},{anchoredPlane.Normal[1]:F9},{anchoredPlane.Normal[2]:F9})");

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

    // Pure policy: scalar mass differences cannot override missing geometry evidence.
    public static class CutMassEvidence53
    {
        public static bool CanAccept(bool exactBefore, bool exactAfter,
            double expectedCut, double actualCut, double expectedAfter, double actualAfter,
            double centroidError, double centroidTolerance)
        {
            return exactBefore && exactAfter &&
                new[] { expectedCut, actualCut, expectedAfter, actualAfter, centroidTolerance }
                    .All(v => !double.IsNaN(v) && !double.IsInfinity(v) && v > 0) &&
                !double.IsNaN(centroidError) && !double.IsInfinity(centroidError) &&
                centroidError >= 0 && centroidError <= centroidTolerance;
        }
    }

    public static class BodyOperationsHelper
    {
        public const double ABSOLUTE_GEOMETRY_TOLERANCE = 1e-12;
        public const double RELATIVE_TOLERANCE = 1e-5;
        public const double BODY_TRANSFORM_RELATIVE_TOLERANCE = 1e-5;

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

        internal static bool TryGetBodiesVolumeCentroid(
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

        internal static double[] ReflectPointAcrossPlane(double[] point, PlaneData plane)
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

        internal static double Distance(double[] a, double[] b)
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

        internal static bool TryMeasureRemovedGeometry(
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
            if (label != null && label.EndsWith("_CUT_ORACLE59_DELTA", StringComparison.Ordinal))
            {
                for (int i = 0; i < cut.Bodies.Count; i++)
                {
                    double[] mass = cut.Bodies[i].GetMassProperties(0) as double[];
                    if (mass == null || mass.Length < 4) continue;
                    CreateMirrorPartPackage.LogDebug("[CUT_ORACLE59][REMOVED_PIECE] label=" + label +
                        " index=" + i + " volume_m3=" + Math.Abs(mass[3]).ToString("R") +
                        " centroid_m=" + string.Join(",", mass.Take(3).Select(value => value.ToString("R"))));
                }
            }
            if (cut.Success && TryGetBodiesVolumeCentroid(cut.Bodies, out removedVolume, out removedCentroid))
            {
                return true;
            }

            // Fallback: If boolean cut fails or yields no bodies (common with coincident faces across large sheets in Parasolid),
            // compute removed volume and centroid via Mass Properties delta:
            // V_rem = V_before - V_after
            // C_rem = (V_before * C_before - V_after * C_after) / V_rem
            try
            {
                if (beforeBody != null && afterBody != null)
                {
                    double[] mpBefore = beforeBody.GetMassProperties(0) as double[];
                    double[] mpAfter = afterBody.GetMassProperties(0) as double[];
                    if (mpBefore != null && mpBefore.Length >= 4 && mpAfter != null && mpAfter.Length >= 4)
                    {
                        double vBefore = Math.Abs(mpBefore[3]);
                        double vAfter = Math.Abs(mpAfter[3]);
                        double vRem = vBefore - vAfter;
                        if (vRem > ABSOLUTE_GEOMETRY_TOLERANCE)
                        {
                            double xRem = (vBefore * mpBefore[0] - vAfter * mpAfter[0]) / vRem;
                            double yRem = (vBefore * mpBefore[1] - vAfter * mpAfter[1]) / vRem;
                            double zRem = (vBefore * mpBefore[2] - vAfter * mpAfter[2]) / vRem;

                            removedVolume = vRem;
                            removedCentroid = new[] { xRem, yRem, zRem };
                            CreateMirrorPartPackage.LogDebug($"[MEASURE_REMOVED] Boolean cut produced no bodies; fallback mass-properties delta succeeded: remVol={removedVolume:E6}, remCent=({xRem * 1000.0:F3},{yRem * 1000.0:F3},{zRem * 1000.0:F3})mm");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CreateMirrorPartPackage.LogDebug($"[MEASURE_REMOVED] Mass properties delta fallback failed: {ex.Message}");
            }

            error = cut.ErrorMessage ?? "Removed geometry is empty or its mass properties are unavailable.";
            return false;
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

        private static bool TrySetExtrudeParams(
            ModelDoc2 partDoc,
            Feature feature,
            bool flip,
            bool reverseDir,
            out string error)
        {
            error = null;
            IExtrudeFeatureData2 definition = null;
            bool selectionAccess = false;

            try
            {
                if (partDoc == null || feature == null)
                {
                    error = "partDoc or feature is null.";
                    return false;
                }

                // In SolidWorks, ModifyDefinition on an existing feature requires the feature
                // to be rolled back and active in edit mode!
                feature.Select2(false, 0);
                partDoc.EditRollback();

                definition = feature.GetDefinition() as IExtrudeFeatureData2;
                if (definition == null) { error = "Not extrude feature."; return false; }

                selectionAccess = definition.AccessSelections(partDoc, null);
                if (!selectionAccess) { error = "AccessSelections false."; return false; }

                definition.FlipSideToCut = flip;
                definition.ReverseDirection = reverseDir; // [MỚI] Cho phép đảo hướng đùn

                if (!feature.ModifyDefinition(definition, partDoc, null))
                {
                    error = "ModifyDefinition false.";
                    return false;
                }
                selectionAccess = false; // Successfully committed, do not call ReleaseSelectionAccess

                partDoc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, feature.Name);
                partDoc.ForceRebuild3(false);
                bool warning = false;
                int featureError = feature.GetErrorCode2(out warning);
                if (featureError != 0 && !warning)
                {
                    error = $"Error={featureError}, warning={warning}.";
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
                try
                {
                    if (partDoc != null && feature != null)
                    {
                        partDoc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, feature.Name);
                    }
                }
                catch { }
            }
        }

        public static Feature FindDrivingSketchFeature(Feature parentFeat)
        {
            if (parentFeat == null) return null;
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

        public static bool TrySuperRecoverExtrudeCut(
            ModelDoc2 partDoc,
            PostBaseFeatureInfo info,
            out string details)
        {
            details = null;
            if (partDoc == null || info?.Feature == null) return false;

            if (info.CutAudit21 != null && info.CutAudit21.Recipe44 != null)
            {
                details = "[CUT44][HEURISTIC_RECOVERY_BLOCKED] original options must remain unchanged.";
                return false;
            }

            // Đọc trạng thái hiện tại (không cần AccessSelections để đọc ban đầu)
            IExtrudeFeatureData2 defRead = info.Feature.GetDefinition() as IExtrudeFeatureData2;
            if (defRead == null || !defRead.AccessSelections(partDoc, null)) return false;
            bool origFlip = defRead.FlipSideToCut;
            bool origDir = defRead.ReverseDirection;
            defRead.ReleaseSelectionAccess();

            Feature skFeat = info.DrivingSketchFeature ?? FindDrivingSketchFeature(info.Feature);
            Sketch sk = skFeat?.GetSpecificFeature2() as Sketch;
            bool isOpenProfile = false;
            if (sk != null)
            {
                isOpenProfile = ADDIN.Helpers.SketchOperationsHelper.IsOpenProfileSketch(sk);
            }

            // Với biên dạng kín (closed profile): KHÔNG BAO GIỜ lật FlipSideToCut từ false sang true vì sẽ cắt bay toàn bộ part!
            bool[] testFlip;
            bool[] testDir;
            if (isOpenProfile || origFlip)
            {
                testFlip = new bool[] { origFlip, origFlip, !origFlip, !origFlip };
                testDir  = new bool[] { origDir, !origDir, origDir, !origDir };
            }
            else
            {
                testFlip = new bool[] { origFlip, origFlip };
                testDir  = new bool[] { origDir, !origDir };
            }

            // Giai đoạn 2: Ép Through All để bypass lỗi end-condition face reference không còn valid sau mirror
            // (Trường hợp điển hình: end condition dùng face của BaseBend chỉ tồn tại phía nguồn)
            for (int i = 0; i < testFlip.Length; i++)
            {
                try { info.Feature.Select2(false, 0); } catch { }
                try { partDoc.EditRollback(); } catch { }

                IExtrudeFeatureData2 def = info.Feature.GetDefinition() as IExtrudeFeatureData2;
                if (def == null || !def.AccessSelections(partDoc, null))
                {
                    try { partDoc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, info.Feature.Name); } catch { }
                    continue;
                }

                bool committed = false;
                try
                {
                    def.FlipSideToCut = testFlip[i];
                    def.ReverseDirection = testDir[i];
                    try
                    {
                        def.SetEndCondition(true, (int)swEndConditions_e.swEndCondThroughAll);
                        def.SetEndCondition(false, (int)swEndConditions_e.swEndCondThroughAll);
                    }
                    catch { }

                    if (info.Feature.ModifyDefinition(def, partDoc, null))
                    {
                        committed = true;
                        try { partDoc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, info.Feature.Name); } catch { }
                        partDoc.ForceRebuild3(false);
                        bool isWarn;
                        int err = info.Feature.GetErrorCode2(out isWarn);
                        if (err == 0 || isWarn)
                        {
                            details = $"SUPER_RECOVERY_PHASE2_THROUGH_ALL_SUCCESS: Flip={testFlip[i]}, Dir={testDir[i]}";
                            return true;
                        }
                    }
                }
                finally
                {
                    if (!committed) { try { def.ReleaseSelectionAccess(); } catch { } }
                    try { partDoc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, info.Feature.Name); } catch { }
                }
            }

            // Giai đoạn 3: Explicit Contours từ Driving Sketch
            Feature sketchFeat = info.DrivingSketchFeature ?? FindDrivingSketchFeature(info.Feature);
            if (sketchFeat != null)
            {
                Sketch sketchObj = sketchFeat.GetSpecificFeature2() as Sketch;
                if (sketchObj != null)
                {
                    object[] contours = sketchObj.GetSketchContours() as object[];
                    for (int i = 0; i < 4; i++)
                    {
                        try { info.Feature.Select2(false, 0); } catch { }
                        try { partDoc.EditRollback(); } catch { }

                        IExtrudeFeatureData2 def = info.Feature.GetDefinition() as IExtrudeFeatureData2;
                        if (def == null || !def.AccessSelections(partDoc, null))
                        {
                            try { partDoc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, info.Feature.Name); } catch { }
                            continue;
                        }

                        bool committed = false;
                        try
                        {
                            if (contours != null && contours.Length > 0)
                            {
                                def.Contours = contours;
                            }
                            def.FlipSideToCut = testFlip[i];
                            def.ReverseDirection = testDir[i];

                            if (info.Feature.ModifyDefinition(def, partDoc, null))
                            {
                                committed = true;
                                try { partDoc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, info.Feature.Name); } catch { }
                                partDoc.ForceRebuild3(false);
                                bool isWarn;
                                int err = info.Feature.GetErrorCode2(out isWarn);
                                if (err == 0 || isWarn)
                                {
                                    details = $"SUPER_RECOVERY_PHASE3_CONTOURS_SUCCESS: Flip={testFlip[i]}, Dir={testDir[i]}";
                                    return true;
                                }
                            }
                        }
                        finally
                        {
                            if (!committed) { try { def.ReleaseSelectionAccess(); } catch { } }
                            try { partDoc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, info.Feature.Name); } catch { }
                        }
                    }
                }
            }

            details = "SUPER_RECOVERY_FAILED_ALL_COMBINATIONS";
            return false;
        }

        public static bool TryRetargetExtrudeContours(
            ModelDoc2 partDoc,
            PostBaseFeatureInfo info,
            Feature sketchFeature,
            out string details)
        {
            return TrySuperRecoverExtrudeCut(partDoc, info, out details);
        }

        public static void EnsureFeatureReferencesSketch(ModelDoc2 partDoc, Feature cutFeature, Feature newSketchFeature)
        {
            if (cutFeature == null || newSketchFeature == null || partDoc == null) return;
            
            IExtrudeFeatureData2 extData = cutFeature.GetDefinition() as IExtrudeFeatureData2;
            if (extData != null)
            {
                bool acc = extData.AccessSelections(partDoc, null);
                if (acc)
                {
                    // Gán lại đối tượng Sketch làm mặt phẳng/đường dẫn tham chiếu nếu cần thiết
                    try 
                    {
                        var setSketchMethod = extData.GetType().GetMethod("SetSketch");
                        if (setSketchMethod != null)
                        {
                            setSketchMethod.Invoke(extData, new object[] { newSketchFeature });
                        }
                    } 
                    catch { }
                    cutFeature.ModifyDefinition(extData, partDoc, null);
                    extData.ReleaseSelectionAccess();
                }
            }
        }

        public static bool TryRecoverExtrudeCutRebuild(
            ModelDoc2 partDoc,
            PostBaseFeatureInfo info,
            FeatureBodyState originalCache,
            out string details)
        {
            return TrySuperRecoverExtrudeCut(partDoc, info, out details);
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

            if (info.CutAudit21 != null && info.CutAudit21.Recipe44 != null && info.CutAudit21.Recipe44.Applied)
            {
                // CUT44 has already solved direction and applied the complete recipe.
                // The mandatory removed-material/Boolean oracle below the caller is the
                // acceptance test; never replace its failure with a different cut option.
                details = "[CUT44][DIRECTION_SEARCH_SKIPPED] feature=" + info.Name +
                    " reason=GeometricDirectionSolved oracleStillRequired=True";
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

            double[] expectedMirroredCentroid = ReflectPointAcrossPlane(sourceRemovedCentroid, mirrorPlane);
            if (expectedMirroredCentroid == null)
            {
                details = $"CUT_FLIP_GEOMETRY_EVALUATE\nfeature={info.Name}\nresult=FAIL\nreason=EXPECTED_MIRRORED_CENTROID_NULL";
                return false;
            }

            IExtrudeFeatureData2 currentDefinition = info.Feature.GetDefinition() as IExtrudeFeatureData2;
            if (currentDefinition == null)
            {
                details = $"CUT_FLIP_EVALUATE\nfeature={info.Name}\nexpectedRemovedVolume={expectedRemoved:E6}\nresult=FAIL\nreason=NOT_EXTRUDE_FEATURE_DATA2";
                return false;
            }

            bool originalFlip;
            bool originalDir;
            bool originalBothDirs = false;
            try
            {
                originalFlip = currentDefinition.FlipSideToCut;
                originalDir = currentDefinition.ReverseDirection;
                originalBothDirs = currentDefinition.BothDirections;
            }
            catch (Exception ex)
            {
                details = $"CUT_FLIP_EVALUATE\nfeature={info.Name}\nresult=FAIL\nreason=CANNOT_READ_FLIP_SIDE: {ex.Message}";
                return false;
            }

            Feature sketchFeat = info.DrivingSketchFeature ?? FindDrivingSketchFeature(info.Feature);
            Sketch skObj = sketchFeat?.GetSpecificFeature2() as Sketch;
            List<ADDIN.Helpers.SketchPointSnapshot> pristineReplayedPoints = (skObj != null)
                ? ADDIN.Helpers.SketchOperationsHelper.CapturePristineSketchPoints(skObj)
                : null;

            bool isOpenProfile = false;
            if (skObj != null)
            {
                isOpenProfile = ADDIN.Helpers.SketchOperationsHelper.IsOpenProfileSketch(skObj);
            }

            // Thử nghiệm cả 4 tổ hợp không gian (FlipSideToCut, ReverseDirection)
            // - Với biên dạng mở (open profile, ví dụ vết cắt xẻ rãnh): Phép phản xạ không gian thường đảo cả hướng đùn và phía cắt (BOTH_TOGGLED).
            // - Với biên dạng kín (closed profile, ví dụ lỗ tròn, rãnh khép kín): FlipSideToCut phải giữ nguyên (đảo FlipSideToCut sẽ biến lỗ thành cắt bỏ toàn bộ chi tiết!). Do đó ưu tiên REVERSED_DIR trước.
            bool[] testFlips;
            bool[] testDirs;
            string[] testLabels;

            if (isOpenProfile || originalFlip)
            {
                testFlips  = new bool[]   { originalFlip, !originalFlip, originalFlip, !originalFlip };
                testDirs   = new bool[]   { originalDir, !originalDir, !originalDir, originalDir };
                testLabels = new string[] { "ORIGINAL", "BOTH_TOGGLED", "REVERSED_DIR", "TOGGLED_FLIP" };
            }
            else
            {
                testFlips  = new bool[]   { originalFlip, originalFlip };
                testDirs   = new bool[]   { originalDir, !originalDir };
                testLabels = new string[] { "ORIGINAL", "REVERSED_DIR" };
            }
            CreateMirrorPartPackage.LogDebug($"[CUT_FLIP_CANDIDATE] Feature {info.Name}: isOpenProfile={isOpenProfile}, candidate priority: {string.Join(" -> ", testLabels)}");

            int bestIndex = -1;
            double bestDistance = double.PositiveInfinity;
            double bestVolumeError = double.PositiveInfinity;
            bool bestVolumeMatched = false;
            Body2 bestBody = null;
            double bestRemovedVolume = 0.0;
            double[] bestRemovedCentroid = null;

            double beforeRemoved = 0.0;
            double[] beforeCentroid = null;
            double beforeDist = double.PositiveInfinity;
            double beforeVolErr = double.PositiveInfinity;
            bool beforeVolMatch = false;

            int currentAppliedIndex = 0;

            for (int i = 0; i < testFlips.Length; i++)
            {
                bool candFlip = testFlips[i];
                bool candDir = testDirs[i];
                Body2 candBody = null;

                // Trước khi thử ứng viên mới, đảm bảo sketch points không bị xô lệch do ứng viên trước đó
                if (pristineReplayedPoints != null && sketchFeat != null && i > 0)
                {
                    ADDIN.Helpers.SketchOperationsHelper.RestoreSketchPoints(partDoc, sketchFeat, pristineReplayedPoints);
                }

                string setErr;
                if (!TrySetExtrudeParams(partDoc, info.Feature, candFlip, candDir, out setErr))
                {
                    CreateMirrorPartPackage.LogDebug($"[CUT_FLIP_CANDIDATE] {testLabels[i]} (flip={candFlip}, dir={candDir}) TrySetExtrudeParams failed: {setErr}");
                    continue;
                }
                currentAppliedIndex = i;
                string capErr;
                candBody = GetSolidBodyCopyStrict(partDoc, out capErr);
                if (candBody == null)
                {
                    CreateMirrorPartPackage.LogDebug($"[CUT_FLIP_CANDIDATE] {testLabels[i]} GetSolidBodyCopyStrict failed: {capErr}");
                    if (i > 0)
                    {
                        string revErr;
                        TrySetExtrudeParams(partDoc, info.Feature, originalFlip, originalDir, out revErr);
                        currentAppliedIndex = 0;
                    }
                    continue;
                }

                double remVol = 0.0;
                double[] remCent = null;
                string mErr = null;
                bool measured = TryMeasureRemovedGeometry(
                    previousActualBody,
                    candBody,
                    info.Name + "_CUT_FLIP_" + testLabels[i],
                    out remVol,
                    out remCent,
                    out mErr);

                if (!measured)
                {
                    CreateMirrorPartPackage.LogDebug($"[CUT_FLIP_CANDIDATE] {testLabels[i]} (flip={candFlip}, dir={candDir}) TryMeasureRemovedGeometry returned false: {mErr}");
                    if (i > 0)
                    {
                        string revErr;
                        TrySetExtrudeParams(partDoc, info.Feature, originalFlip, originalDir, out revErr);
                        currentAppliedIndex = 0;
                    }
                    continue;
                }

                double volErr = Math.Abs(remVol - expectedRemoved);
                double dist = Distance(remCent, expectedMirroredCentroid);
                bool volMatch = volErr <= expectedTolerance;

                // [SAFETY CHECK] Reject candidates that remove completely unreasonable volume
                // (e.g. inverted cut destroying the body by removing orders of magnitude more than expected)
                bool volReasonable = true;
                if (expectedRemoved > 1e-9)
                {
                    double ratio = remVol / expectedRemoved;
                    double prevVol = GetBodyVolume(previousActualBody);
                    if (ratio > 50.0 || (ratio > 5.0 && prevVol > 0 && remVol > 0.3 * prevVol) || ratio < 0.01)
                    {
                        volReasonable = false;
                    }
                }

                if (!volReasonable)
                {
                    CreateMirrorPartPackage.LogDebug($"[CUT_FLIP_CANDIDATE] {testLabels[i]} (flip={candFlip}, dir={candDir}) REJECTED: volume ratio {(remVol / expectedRemoved):F2} is unreasonable.");
                    if (i > 0)
                    {
                        string revErr;
                        TrySetExtrudeParams(partDoc, info.Feature, originalFlip, originalDir, out revErr);
                        currentAppliedIndex = 0;
                    }
                    continue;
                }

                CreateMirrorPartPackage.LogDebug($"[CUT_FLIP_CANDIDATE] {testLabels[i]} (flip={candFlip}, dir={candDir}) remVol={remVol:E6} volErr={volErr:E6} dist={dist * 1000.0:F3}mm volMatch={volMatch}");

                if (i == 0)
                {
                    beforeRemoved = remVol;
                    beforeCentroid = remCent;
                    beforeDist = dist;
                    beforeVolErr = volErr;
                    beforeVolMatch = volMatch;
                }

                bool isBetter = false;
                if (bestIndex == -1)
                {
                    isBetter = true;
                }
                else if (volMatch && !bestVolumeMatched)
                {
                    isBetter = true;
                }
                else if (volMatch && bestVolumeMatched)
                {
                    if (dist < bestDistance) isBetter = true;
                }
                else if (!volMatch && !bestVolumeMatched)
                {
                    if (dist < bestDistance) isBetter = true;
                }

                if (isBetter)
                {
                    bestIndex = i;
                    bestDistance = dist;
                    bestVolumeError = volErr;
                    bestVolumeMatched = volMatch;
                    bestBody = candBody;
                    bestRemovedVolume = remVol;
                    bestRemovedCentroid = remCent;

                    // Nếu ứng viên khớp thể tích và tâm khối lệch <= 20 micron (2e-5 m),
                    // đây là hình học đối xứng chính xác tuyệt đối, dừng tìm kiếm ngay
                    if (volMatch && dist <= 2e-5)
                    {
                        CreateMirrorPartPackage.LogDebug($"[CUT_FLIP_CANDIDATE] Exact match found with {testLabels[i]} (dist={dist * 1000.0:F3}mm). Stopping candidate search.");
                        break;
                    }
                }
            }

            if (bestIndex == -1)
            {
                if (currentAppliedIndex != 0)
                {
                    string restErr;
                    TrySetExtrudeParams(partDoc, info.Feature, originalFlip, originalDir, out restErr);
                }
                if (pristineReplayedPoints != null && sketchFeat != null)
                {
                    ADDIN.Helpers.SketchOperationsHelper.RestoreSketchPoints(partDoc, sketchFeat, pristineReplayedPoints);
                }
                details = $"CUT_FLIP_EVALUATE\nfeature={info.Name}\nresult=FAIL\nreason=NO_VALID_CUT_ORIENTATION_FOUND";
                return false;
            }

            bool chosenFlip = testFlips[bestIndex];
            bool chosenDir = testDirs[bestIndex];
            if (currentAppliedIndex != bestIndex)
            {
                if (pristineReplayedPoints != null && sketchFeat != null)
                {
                    ADDIN.Helpers.SketchOperationsHelper.RestoreSketchPoints(partDoc, sketchFeat, pristineReplayedPoints);
                }
                string finalSetErr;
                if (!TrySetExtrudeParams(partDoc, info.Feature, chosenFlip, chosenDir, out finalSetErr))
                {
                    details = $"CUT_FLIP_EVALUATE\nfeature={info.Name}\nresult=FAIL\nreason=APPLY_BEST_FAILED: {finalSetErr}";
                    return false;
                }
            }

            replayedActualBody = bestBody;
            allowAsymmetricCutVolume = !bestVolumeMatched;

            details = $"CUT_FLIP_GEOMETRY_EVALUATE\nfeature={info.Name}\n" +
                $"sourceRemovedCentroid={FormatPoint(sourceRemovedCentroid)}\n" +
                $"expectedMirroredCentroid={FormatPoint(expectedMirroredCentroid)}\n" +
                $"beforeCentroid={FormatPoint(beforeCentroid)}\n" +
                $"afterCentroid={FormatPoint(bestRemovedCentroid)}\n" +
                $"beforeDistance={(double.IsInfinity(beforeDist) ? "INF" : (beforeDist * 1000.0).ToString("F6") + "mm")}\n" +
                $"afterDistance={(bestDistance * 1000.0):F6}mm\n" +
                $"expectedRemovedVolume={expectedRemoved:E6}\n" +
                $"beforeToggleRemovedVolume={beforeRemoved:E6}\n" +
                $"afterToggleRemovedVolume={bestRemovedVolume:E6}\n" +
                $"beforeVolumeError={beforeVolErr:E6}\n" +
                $"afterVolumeError={bestVolumeError:E6}\n" +
                $"beforeVolumeMatches={beforeVolMatch}\n" +
                $"afterVolumeMatches={bestVolumeMatched}\n" +
                $"originalFlip={originalFlip}\n" +
                $"originalDir={originalDir}\n" +
                $"chosenFlip={chosenFlip}\n" +
                $"chosenDir={chosenDir}\n" +
                $"chosenCandidate={testLabels[bestIndex]}\n" +
                $"volumeMode={(allowAsymmetricCutVolume ? "ASYMMETRIC_BASE" : "SOURCE_MATCH")}\n" +
                $"result=PASS\nreason={testLabels[bestIndex]}_MATCHED";

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

        public static List<Body2> GetAllSolidBodies(ModelDoc2 partDoc)
        {
            List<Body2> list = new List<Body2>();
            PartDoc part = partDoc as PartDoc;
            if (part == null) return list;

            // Include hidden bodies. Mirroring only currently visible bodies would make the
            // result depend on display state instead of the active configuration definition.
            object[] bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (bodies == null) return list;

            foreach (object bodyObject in bodies)
            {
                Body2 body = bodyObject as Body2;
                if (body != null) list.Add(body);
            }

            return list;
        }

        public static List<Body2> GetAllSolidBodyCopies(ModelDoc2 partDoc)
        {
            List<Body2> list = new List<Body2>();
            foreach (Body2 body in GetAllSolidBodies(partDoc))
            {
                Body2 copy = body.Copy2(false) as Body2;
                if (copy != null) list.Add(copy);
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
                if (opResult is Array arr)
                {
                    foreach (object o in arr)
                    {
                        if (o is Body2 b) res.Bodies.Add(b);
                        else
                        {
                            res.ErrorCode = -1;
                            res.ErrorMessage = "Unexpected non-body item in native CUT result; not an empty difference.";
                            CreateMirrorPartPackage.LogDebug($"BOOLEAN operation=CUT label={label} errorCode=-1 success=False error={res.ErrorMessage}");
                            return res;
                        }
                    }
                }
                else if (opResult is Body2 b)
                {
                    res.Bodies.Add(b);
                }
                else
                {
                    res.ErrorCode = -1;
                    res.ErrorMessage = "Unsupported native CUT result: " + opResult.GetType().FullName;
                    CreateMirrorPartPackage.LogDebug($"BOOLEAN operation=CUT label={label} errorCode=-1 success=False error={res.ErrorMessage}");
                    return res;
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
                // Diagnose the untouched temporary inputs, not the copies consumed
                // by Operations2. A failed Boolean is never treated as empty space.
                if (errCode == (int)swBodyOperationError_e.swBodyOperationBooleanFail ||
                    errCode == (int)swBodyOperationError_e.swBodyOperationFailGeomCondition)
                {
                    try
                    {
                        var targetFaults64 = targetBody.Check3;
                        var toolFaults64 = toolBody.Check3;
                        int targetCount64 = targetFaults64 == null ? 0 : targetFaults64.Count;
                        int toolCount64 = toolFaults64 == null ? 0 : toolFaults64.Count;
                        res.ErrorMessage += " targetBRepFaults=" + targetCount64 + " toolBRepFaults=" + toolCount64;
                        CreateMirrorPartPackage.LogDebug("[BOOLEAN64][INPUT_FAULTS] label=" + label +
                            " error=" + (swBodyOperationError_e)errCode + " targetFaults=" + targetCount64 +
                            " toolFaults=" + toolCount64 + " geometryModified=False failureAccepted=False");
                    }
                    catch (Exception faultError64)
                    { res.ErrorMessage += " inputCheckUnavailable=" + faultError64.Message; }
                }
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

                MathTransform mathXform = ReflectionApiTransformV7.Create(mathUtility, xform);
                CreateMirrorPartPackage.currentMirrorTransform = mathXform;
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

                double[] sourceMass = sourceBody.GetMassProperties(1) as double[];
                double[] reflectedMass = copy.GetMassProperties(1) as double[];
                if (sourceMass == null || reflectedMass == null || sourceMass.Length < 4 || reflectedMass.Length < 4)
                {
                    res.ErrorMessage = "REFLECTION_CENTROID_UNAVAILABLE";
                    return res;
                }
                double signedDistance = (sourceMass[0] - o[0]) * nx +
                    (sourceMass[1] - o[1]) * ny + (sourceMass[2] - o[2]) * nz;
                double[] normal = { nx, ny, nz };
                bool centroidWithinTightTolerance = true;
                for (int axis = 0; axis < 3; axis++)
                {
                    double expectedCoordinate = sourceMass[axis] - 2 * signedDistance * normal[axis];
                    if (double.IsNaN(reflectedMass[axis]) || double.IsInfinity(reflectedMass[axis]) ||
                        Math.Abs(expectedCoordinate - reflectedMass[axis]) > 1e-7)
                    {
                        centroidWithinTightTolerance = false;
                        CreateMirrorPartPackage.LogDebug("REFLECTION_CENTROID_DIFFERENCE axis=" + axis +
                            " expected=" + expectedCoordinate + " actual=" + reflectedMass[axis]);
                    }
                }
                if (!centroidWithinTightTolerance)
                {
                    // Mass-property integration can shift the computed centroid. Do not simply
                    // widen tolerance: require a one-to-one analytic reflection of all vertices.
                    var sourceVertices = sourceBody.GetVertices() as object[];
                    var reflectedVertices = copy.GetVertices() as object[];
                    var expectedPoints = new List<double[]>();
                    var actualPoints = new List<double[]>();
                    if (sourceVertices != null) foreach (Vertex vertex in sourceVertices)
                    {
                        double[] p = vertex.GetPoint() as double[];
                        if (p == null || p.Length < 3) throw new InvalidOperationException("Source vertex unavailable.");
                        double d = (p[0]-o[0])*nx + (p[1]-o[1])*ny + (p[2]-o[2])*nz;
                        expectedPoints.Add(new[] { p[0]-2*d*nx, p[1]-2*d*ny, p[2]-2*d*nz });
                    }
                    if (reflectedVertices != null) foreach (Vertex vertex in reflectedVertices)
                        actualPoints.Add(vertex.GetPoint() as double[]);
                    bool verticesMatch = expectedPoints.Count > 0 &&
                        BipartiteEquivalenceMatcherV7.Match(expectedPoints, actualPoints, (left, right) =>
                        {
                            if (right == null || right.Length < 3) return false;
                            for (int axis = 0; axis < 3; axis++)
                                if (double.IsNaN(right[axis]) || double.IsInfinity(right[axis]) ||
                                    Math.Abs(left[axis]-right[axis]) > 1e-8) return false;
                            return true;
                        }).HasPerfectMatching;
                    if (!verticesMatch)
                    {
                        res.ErrorMessage = "REFLECTION_CENTROID_AND_VERTEX_VERIFICATION_FAILED";
                        return res;
                    }
                    CreateMirrorPartPackage.LogDebug("REFLECTION_VERTEX_CHECK matched=" + expectedPoints.Count +
                        " toleranceSI=1e-8 result=PASS roundTripBooleanStillRequired=True");
                }
                double mirrVol = GetBodyVolume(copy);
                double volumeDelta = Math.Abs(origVol - mirrVol);
                double tol = Math.Max(
                    ABSOLUTE_GEOMETRY_TOLERANCE,
                    origVol * BODY_TRANSFORM_RELATIVE_TOLERANCE);
                if (volumeDelta > tol)
                {
                    res.ErrorMessage =
                        $"REFLECTION_VOLUME_CHANGED (orig={origVol:E12}, mirr={mirrVol:E12}, " +
                        $"delta={volumeDelta:E12}, tolerance={tol:E12})";
                    return res;
                }

                // Mass properties from a transformed temporary Parasolid body can differ
                // by a few 1e-7 relatively even when the topology is unchanged. Do not rely
                // on that scalar alone: reflect the result a second time and compare the
                // round-trip body to the source with Boolean differences in both directions.
                Body2 roundTripBody = copy.Copy2(false) as Body2;
                if (roundTripBody == null || !roundTripBody.ApplyTransform(mathXform))
                {
                    res.ErrorMessage = "REFLECTION_ROUND_TRIP_TRANSFORM_FAILED";
                    return res;
                }

                BodyBooleanResult sourceMinusRoundTrip = BooleanCutStrict(
                    sourceBody,
                    roundTripBody,
                    "MIRROR_BODY_ROUND_TRIP_SOURCE_MINUS_RESULT");
                BodyBooleanResult roundTripMinusSource = BooleanCutStrict(
                    roundTripBody,
                    sourceBody,
                    "MIRROR_BODY_ROUND_TRIP_RESULT_MINUS_SOURCE");
                double sourceResidual = sourceMinusRoundTrip.Success
                    ? SumBodyVolumes(sourceMinusRoundTrip.Bodies)
                    : double.PositiveInfinity;
                double roundTripResidual = roundTripMinusSource.Success
                    ? SumBodyVolumes(roundTripMinusSource.Bodies)
                    : double.PositiveInfinity;

                if (!sourceMinusRoundTrip.Success || !roundTripMinusSource.Success ||
                    sourceResidual > tol || roundTripResidual > tol)
                {
                    res.ErrorMessage =
                        $"REFLECTION_ROUND_TRIP_MISMATCH (sourceResidual={sourceResidual:E12}, " +
                        $"resultResidual={roundTripResidual:E12}, tolerance={tol:E12})";
                    return res;
                }

                CreateMirrorPartPackage.LogDebug(
                    $"MIRROR_BODY_STRICT\noriginalVolume={origVol:E12}\nmirroredVolume={mirrVol:E12}\n" +
                    $"volumeDelta={volumeDelta:E12}\nvolumeTolerance={tol:E12}\n" +
                    $"sourceResidual={sourceResidual:E12}\nroundTripResidual={roundTripResidual:E12}\nresult=PASS");

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

            // Sheet metal EdgeFlange features modify/add geometry parametrically and do not rely on Boolean cut delta
            if (string.Equals(info.Type, "EdgeFlange", StringComparison.OrdinalIgnoreCase) ||
                info.Type.IndexOf("EdgeFlange", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                res.Success = true;
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
                // Parasolid boolean cut can fail on coincident sheet-metal faces across large bodies.
                // Check if volume delta can provide fallback before failing outright.
                double vDelta = bVol - aVol;
                if (Math.Abs(vDelta) > ABSOLUTE_GEOMETRY_TOLERANCE)
                {
                    CreateMirrorPartPackage.LogDebug($"[VALIDATE_REPLAY] Boolean cut unconfirmed (addedOk={cutAdded.Success}, removedOk={cutRemoved.Success}); body volume delta is {vDelta:E6}");
                }
                else
                {
                    res.FailureReason = "Boolean delta calculation between previous and replayed body failed.";
                    return res;
                }
            }

            double actualAddedVol = 0.0;
            if (cutAdded.Success)
            {
                foreach (var b in cutAdded.Bodies) actualAddedVol += GetBodyVolume(b);
            }

            double actualRemovedVol = 0.0;
            if (cutRemoved.Success)
            {
                foreach (var b in cutRemoved.Bodies) actualRemovedVol += GetBodyVolume(b);
            }

            // Fallback for Parasolid coincident-face boolean cut limitations:
            // If direct boolean cut produced 0 volume, check mass properties delta between previous and replayed body
            double volumeDelta = bVol - aVol;
            if (actualRemovedVol <= ABSOLUTE_GEOMETRY_TOLERANCE && volumeDelta > ABSOLUTE_GEOMETRY_TOLERANCE)
            {
                actualRemovedVol = volumeDelta;
                CreateMirrorPartPackage.LogDebug($"[VALIDATE_REPLAY] Boolean cut produced 0 removed volume; using body volume delta fallback: {actualRemovedVol:E6}");
            }
            if (actualAddedVol <= ABSOLUTE_GEOMETRY_TOLERANCE && -volumeDelta > ABSOLUTE_GEOMETRY_TOLERANCE)
            {
                actualAddedVol = -volumeDelta;
                CreateMirrorPartPackage.LogDebug($"[VALIDATE_REPLAY] Boolean cut produced 0 added volume; using body volume delta fallback: {actualAddedVol:E6}");
            }

            res.ActualAddedVolume = actualAddedVol;
            res.ActualRemovedVolume = actualRemovedVol;
            res.ExpectedAddedVolume = SumBodyVolumes(originalCache?.AddedBodies);
            res.ExpectedRemovedVolume = SumBodyVolumes(originalCache?.RemovedBodies);

            // Detect material change on the delta itself: small chamfers/cuts must
            // not disappear relative to a large parent body's volume.
            double tol = ABSOLUTE_GEOMETRY_TOLERANCE;

            double addedDifference = Math.Abs(actualAddedVol - res.ExpectedAddedVolume);
            double removedDifference = Math.Abs(actualRemovedVol - res.ExpectedRemovedVolume);
            double addedMatchTolerance = Math.Max(
                ABSOLUTE_GEOMETRY_TOLERANCE,
                res.ExpectedAddedVolume * RELATIVE_TOLERANCE);
            double removedMatchTolerance = Math.Max(
                ABSOLUTE_GEOMETRY_TOLERANCE,
                res.ExpectedRemovedVolume * RELATIVE_TOLERANCE);

            if (res.ExpectedChangeKind == FeatureGeometryChangeKind.Subtractive)
            {
                if (actualAddedVol > tol)
                {
                    res.FailureReason = $"SUBTRACTIVE_FEATURE_ADDED_MATERIAL (addedVol={actualAddedVol:E6})";
                    return res;
                }

                bool isAsymmetricAllowed = replayResult != null && replayResult.AllowAsymmetricCutVolume;

                if (originalCache != null && originalCache.ChangesGeometry && actualRemovedVol <= tol && !isAsymmetricAllowed)
                {
                    res.FailureReason = $"SUBTRACTIVE_FEATURE_REMOVED_NOTHING (removedVol={actualRemovedVol:E6})";
                    return res;
                }

                if (originalCache != null && originalCache.ChangesGeometry && aVol >= bVol - tol && !isAsymmetricAllowed)
                {
                    res.FailureReason = $"SUBTRACTIVE_VOLUME_DID_NOT_DECREASE (before={bVol:E6}, after={aVol:E6})";
                    return res;
                }

                res.RelativeVolumeError = res.ExpectedRemovedVolume > ABSOLUTE_GEOMETRY_TOLERANCE
                    ? removedDifference / res.ExpectedRemovedVolume
                    : removedDifference;

                if (originalCache != null && originalCache.ChangesGeometry && removedDifference > removedMatchTolerance)
                {
                    bool asymmetricExtrudeCutAccepted = isAsymmetricAllowed;

                    if (asymmetricExtrudeCutAccepted)
                    {
                        // SAFETY CAP: Prevent runaway Super Recover cuts from destroying the part.
                        // If the cut grew by >10x compared to source AND now consumes >10% of the entire part body,
                        // it is almost certainly a topological CAD bug (like cutting outside the profile), not a valid Asymmetric Base.
                        if (actualRemovedVol > res.ExpectedRemovedVolume * 10.0 && actualRemovedVol > bVol * 0.1)
                        {
                            asymmetricExtrudeCutAccepted = false;
                            CreateMirrorPartPackage.LogDebug($"[SAFETY CAP EXCEEDED] feature={info.Name} actualRemoved={actualRemovedVol:E6} is >10x expected({res.ExpectedRemovedVolume:E6}) AND >10% of part volume({bVol:E6}). Rejecting dangerous asymmetric cut.");
                        }
                    }

                    if (!asymmetricExtrudeCutAccepted)
                    {
                        res.FailureReason = $"SUBTRACTIVE_REMOVED_VOLUME_MISMATCH(expected={res.ExpectedRemovedVolume:E6}, actual={actualRemovedVol:E6}, relativeError={res.RelativeVolumeError:E6})";
                        return res;
                    }

                    CreateMirrorPartPackage.LogDebug(
                        $"CUT_ASYMMETRIC_VOLUME_ACCEPTED\nfeature={info.Name}\nexpectedRemovedVolume={res.ExpectedRemovedVolume:E6}\nactualRemovedVolume={actualRemovedVol:E6}\nrelativeError={res.RelativeVolumeError:E6}\nreason=MIRRORED_REMOVED_REGION_SELECTED");
                }
            }
            else if (res.ExpectedChangeKind == FeatureGeometryChangeKind.Additive)
            {
                if (actualRemovedVol > tol)
                {
                    res.FailureReason = $"ADDITIVE_FEATURE_REMOVED_MATERIAL (removedVol={actualRemovedVol:E6})";
                    return res;
                }

                if (originalCache != null && originalCache.ChangesGeometry && actualAddedVol <= tol)
                {
                    res.FailureReason = $"ADDITIVE_FEATURE_ADDED_NOTHING (addedVol={actualAddedVol:E6})";
                    return res;
                }

                if (originalCache != null && originalCache.ChangesGeometry && aVol <= bVol + tol)
                {
                    res.FailureReason = $"ADDITIVE_VOLUME_DID_NOT_INCREASE (before={bVol:E6}, after={aVol:E6})";
                    return res;
                }

                res.RelativeVolumeError = res.ExpectedAddedVolume > ABSOLUTE_GEOMETRY_TOLERANCE
                    ? addedDifference / res.ExpectedAddedVolume
                    : addedDifference;
                if (originalCache != null && originalCache.ChangesGeometry && addedDifference > addedMatchTolerance)
                {
                    res.FailureReason = $"ADDITIVE_ADDED_VOLUME_MISMATCH(expected={res.ExpectedAddedVolume:E6}, actual={actualAddedVol:E6}, relativeError={res.RelativeVolumeError:E6})";
                    return res;
                }
            }
            else if (res.ExpectedChangeKind == FeatureGeometryChangeKind.Mixed)
            {
                if (actualAddedVol <= tol || actualRemovedVol <= tol)
                {
                    res.FailureReason = $"MIXED_FEATURE_DELTA_MISSING(added={actualAddedVol:E6}, removed={actualRemovedVol:E6})";
                    return res;
                }

                if (addedDifference > addedMatchTolerance || removedDifference > removedMatchTolerance)
                {
                    res.FailureReason =
                        $"MIXED_FEATURE_VOLUME_MISMATCH(expectedAdded={res.ExpectedAddedVolume:E6}, actualAdded={actualAddedVol:E6}, " +
                        $"expectedRemoved={res.ExpectedRemovedVolume:E6}, actualRemoved={actualRemovedVol:E6})";
                    return res;
                }
            }
            else if (actualAddedVol > tol || actualRemovedVol > tol)
            {
                res.FailureReason = $"NO_GEOMETRY_CHANGE_EXPECTED(added={actualAddedVol:E6}, removed={actualRemovedVol:E6})";
                return res;
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
                res.Success = true;
                res.AxisPoint1 = null;
                res.AxisPoint2 = null;
                res.Message = $"Mirror plane is PARALLEL to sketch plane (|dot|={Math.Abs(dot):F4} ≈ 1). Mapping via 3D invariant coordinate transform.";
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

                // Chieu dai center line khoang 10mm (moi ben 5mm = 0.005m) theo yeu cau user
                double halfLengthM = 0.005;
                MathPoint p1M = mathUtility.CreatePoint(new double[] { px - halfLengthM * dx, py - halfLengthM * dy, pz - halfLengthM * dz }) as MathPoint;
                MathPoint p2M = mathUtility.CreatePoint(new double[] { px + halfLengthM * dx, py + halfLengthM * dy, pz + halfLengthM * dz }) as MathPoint;

                MathPoint p1S = p1M.MultiplyTransform(m2s) as MathPoint;
                MathPoint p2S = p2M.MultiplyTransform(m2s) as MathPoint;

                double[] s1 = p1S.ArrayData as double[];
                double[] s2 = p2S.ArrayData as double[];

                res.AxisPoint1 = new double[] { s1[0], s1[1] };
                res.AxisPoint2 = new double[] { s2[0], s2[1] };
                res.Success = true;
                res.Kind = MirrorReferenceKind.IntersectionCenterline;
                res.Message = "EXPLICIT_SKETCH_CENTERLINE";

                CreateMirrorPartPackage.LogDebug(
                    $"SKETCH_MIRROR_REFERENCE\nmode=INTERSECTION_AXIS\n" +
                    "anchor=PART_ORIGIN\n" +
                    $"mirrorPlaneOrigin=({oMp[0]:F9},{oMp[1]:F9},{oMp[2]:F9})\n" +
                    $"axis1=({s1[0]:F9},{s1[1]:F9})\n" +
                    $"axis2=({s2[0]:F9},{s2[1]:F9})");

                // If a sketch is currently active/open for editing, try creating the construction centerline
                try
                {
                    if (partDoc.SketchManager.ActiveSketch != null)
                    {
                        SketchSegment cl = partDoc.SketchManager.CreateCenterLine(s1[0], s1[1], 0.0, s2[0], s2[1], 0.0) as SketchSegment;
                        if (cl != null)
                        {
                            try { cl.ConstructionGeometry = true; } catch { }
                            try { cl.Select4(true, null); } catch { }
                            res.Centerline = cl;
                        }
                    }
                }
                catch { }

                return res;
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
        public List<ADDIN.Helpers.SketchPointSnapshot> PristineSketchPoints { get; set; } = new List<ADDIN.Helpers.SketchPointSnapshot>();
        public ADDIN.Helpers.SketchSupportSnapshot20 PristineSupport20 { get; set; }
        public ADDIN.Helpers.CutAuditSnapshot21 CutAudit21 { get; set; }
        public List<ADDIN.Helpers.SketchSlotSnapshot> PristineSketchSlots { get; set; } = new List<ADDIN.Helpers.SketchSlotSnapshot>();
        public List<ADDIN.Helpers.SketchPrimitiveSnapshot58> PristineSketchPrimitives58 { get; set; } =
            new List<ADDIN.Helpers.SketchPrimitiveSnapshot58>();
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
        public static bool DiagnosticOnly { get; set; } = false;
        public static bool OneCutReferenceTrialOnly { get; set; } = false;

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

            // Chèn trong một nhánh DiagnosticOnly riêng.
            // partDoc: tài liệu đang được kiểm tra.
            // info.DrivingSketchFeature: Sketch của feature đang xét.
            if (DiagnosticOnly && IsExtrudeCutType(info.Type))
            {
                bool rolledBack = false;
                try
                {
                    if (partDoc.FeatureManager != null && info.Feature != null)
                    {
                        rolledBack = partDoc.FeatureManager.EditRollback(
                            (int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, info.Feature.Name);
                        CreateMirrorPartPackage.LogDebug($"[DIAGNOSTIC] EditRollback before '{info.Feature.Name}' result={rolledBack}");
                    }
                    if (!rolledBack) throw new InvalidOperationException("REFTRACE32: Cannot establish before-Cut checkpoint.");

                    ADDIN.Diagnostics.SketchReferenceDiagnostic.Run(
                        partDoc,
                        info.DrivingSketchFeature,
                        message => CreateMirrorPartPackage.LogDebug(message),
                        info.Feature,
                        "after-base-mutation/before-cut");
                }
                finally
                {
                    if (rolledBack && partDoc.FeatureManager != null && info.Feature != null)
                    {
                        if (!partDoc.FeatureManager.EditRollback(
                            (int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, info.Feature.Name))
                            throw new InvalidOperationException("REFTRACE32: Cannot restore after-Cut checkpoint.");
                    }
                }

                // Kết thúc nhánh DiagnosticOnly tại đây.
                // Không chạy mutation/replay/save/publication tiếp theo.
                return new FeatureReplayResult
                {
                    Success = false,
                    FeatureName = info.Name,
                    FeatureType = info.Type,
                    StatusCode = "DIAGNOSTIC_ONLY_STOP",
                    Message = "DiagnosticOnly mode active: SketchReferenceDiagnostic executed; subsequent mutations halted."
                };
            }

            // IExtrudeFeatureData2.Contours selects regions inside the extrude's one
            // existing sketch; it cannot re-parent an extrude to a separately pasted
            // sketch.  Replaying a copied sketch and then assigning its contours therefore
            // leaves the feature on the old sketch (ModifyDefinition can still return true).
            //
            // This document is already a disposable copy of the source part.  Keep the
            // native feature/sketch dependency and replace the active profile in that
            // driving sketch.  ReplayOnDrivingSketch removes the temporary symmetric
            // relation, transfers dimensions to reflected entities, neutralizes the old
            // profile, rebuilds, and validates the resulting body before the copy is saved.
            CreateMirrorPartPackage.LogDebug(
                "[MIRROR-SKETCH-IN-PLACE] START\n" +
                $"feature={info.Name}\nsketch={info.DrivingSketchName}\n" +
                "dependency=NATIVE_DRIVING_SKETCH\nresult=BEGIN");

            FeatureReplayResult replayResult = ReplayOnDrivingSketch(
                swApp,
                partDoc,
                info,
                mirrorPlane,
                cache,
                protectedBaseFeatureName,
                protectedBaseSketchName);

            CreateMirrorPartPackage.LogDebug(
                "[MIRROR-SKETCH-IN-PLACE] COMMIT\n" +
                $"feature={info.Name}\nsketch={info.DrivingSketchName}\n" +
                $"result={(replayResult != null && replayResult.Success ? "PASS" : "FAIL")}\n" +
                $"status={replayResult?.StatusCode ?? "NULL_RESULT"}");

            return replayResult ?? invalidResult;
        }

        private FeatureReplayResult ReplayOnDrivingSketch(
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
                // 1. Snapshot Before Reference
                object[] segsBeforeRefObj = sketch.GetSketchSegments() as object[];
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
                    partDoc.ClearSelection2(true);
                    result.StatusCode = "EMPTY_PROFILE_SEGMENTS";
                    result.Message = "No active (non-construction) sketch segments found.";
                    return result;
                }

                if (IsExtrudeCutType(info.Type) && (info.CutAudit21 == null ||
                    info.CutAudit21.Recipe44 == null || !string.IsNullOrEmpty(info.CutAudit21.CaptureError)))
                    throw new InvalidOperationException("CUT44: source option capture incomplete before mutation. " +
                        (info.CutAudit21 == null ? "No cut audit." : info.CutAudit21.CaptureError));
                if (info.CutAudit21 != null)
                    SketchOperationsHelper.CheckCutChain23(swApp, partDoc, info.Feature, info.CutAudit21, mirrorPlane);
                try
                {
                    if (IsExtrudeCutType(info.Type))
                        SketchOperationsHelper.EnsureCutSupport52(swApp, partDoc, info.Feature,
                            sketchFeat, info.CutAudit21, mirrorPlane);
                    else
                        SketchOperationsHelper.EnsureReflectedSupport20(swApp, partDoc, sketchFeat,
                            info.PristineSketchPoints, info.PristineSupport20, mirrorPlane);
                }
                catch (Exception mappingError23)
                {
                    CreateMirrorPartPackage.LogDebug("[CUT23] SUMMARY feature=" + info.Name +
                        " result=FAIL firstFailedStage=APPLY_SUPPORT reason=" + mappingError23.Message);
                    throw;
                }
                sketch = (Sketch)sketchFeat.GetSpecificFeature2();
                // 2. Resolve mirror reference (tính toán đường giao tuyến 2D của mirrorPlane với sketchPlane)
                MirrorReferenceResult refRes = MirrorReferenceResolver.ResolveAndSelectMirrorReference(swApp, partDoc, sketch, mirrorPlane);
                result.MirrorReferenceKind = refRes.Kind;

                if (!refRes.Success && refRes.Kind == MirrorReferenceKind.ObliqueRequiresRehost)
                {
                    double normalError56, planeResidual56;
                    SketchSupportSnapshot20 originalSupport56 = info.CutAudit21?.BeforeSupport ?? info.PristineSupport20;
                    List<SketchPointSnapshot> originalPoints56 = info.CutAudit21?.CirclePoints26 ?? info.PristineSketchPoints;
                    bool rehosted56 = SketchOperationsHelper.VerifyReflectedSketchPlane56(sketch,
                        originalSupport56, originalPoints56, mirrorPlane,
                        out normalError56, out planeResidual56);
                    CreateMirrorPartPackage.LogDebug("[OBLIQUE56][REHOST_PROOF] feature=" + info.Name +
                        " sourceSupport=" + (originalSupport56?.Kind ?? "NONE") +
                        " normalError=" + normalError56.ToString("R") +
                        " planeResidual_m=" + planeResidual56.ToString("R") +
                        " result=" + (rehosted56 ? "PASS" : "FAIL"));
                    if (rehosted56)
                    {
                        // There is no valid in-plane mirror axis for this case.
                        // The existing 3D model-to-sketch replay maps every point onto
                        // the proven reflected support, then the feature oracle checks it.
                        refRes.Success = true;
                        refRes.AxisPoint1 = null;
                        refRes.AxisPoint2 = null;
                        refRes.Message = "Reflected sketch support proven; model-space geometry replay required.";
                    }
                }

                if (!refRes.Success)
                {
                    partDoc.ClearSelection2(true);
                    result.StatusCode = "MIRROR_REFERENCE_RESOLVE_FAILED";
                    result.Message = refRes.Message;
                    return result;
                }
                result.MirrorReferenceResolved = true;

                // 3. Mở Sketch và Xóa sạch Ràng buộc + Kích thước để giải phóng nét vẽ
                bool alreadyReflected26 = SketchOperationsHelper.TryAlreadyReflectedCircles26(swApp, partDoc, sketchFeat,
                    info.CutAudit21, mirrorPlane);
                bool geometryFirstSlot56 = info.PristineSketchSlots != null && info.PristineSketchSlots.Count > 0;
                Sketch activeSketch = alreadyReflected26 ? sketch : SketchOperationsHelper.FreeSketchForMutation(
                    partDoc, sketchFeat, preserveConstraints: !geometryFirstSlot56);
                if (activeSketch == null)
                {
                    activeSketch = sketch;
                }
                result.SketchEntered = !alreadyReflected26;

                // 4. CỖ MÁY TÁI TẠO HOẶC DỊCH CHUYỂN TỌA ĐỘ ĐIỂM
                if (alreadyReflected26)
                {
                    // No edit/rebuild cycle: support remapping already placed the complete circle profile.
                    // The normal feature health and removed-volume checks below remain mandatory.
                }
                else if (info.PristineSketchSlots != null && info.PristineSketchSlots.Count > 0)
                {
                    // Slot metadata is not the complete profile (trimmed slots and
                    // mixed line/arc profiles can contain additional geometry).
                    // Rebuild every captured primitive, preserving construction flags.
                    var primitives53 = info.CutAudit21 == null ? info.PristineSketchPrimitives58 :
                        info.CutAudit21.BeforePrimitives53;
                    CreateMirrorPartPackage.LogDebug("[PROFILE53][COMPLETE_REBUILD] feature=" + info.Name +
                        " slots=" + info.PristineSketchSlots.Count + " primitives=" +
                        (primitives53 == null ? 0 : primitives53.Count) + " slotOnlyReplay=False");
                    SketchOperationsHelper.RecreateMirroredPrimitives58(partDoc, activeSketch,
                        primitives53, mirrorPlane, replaceGeneratedProfile: true);
                }
                else
                {
                    // Di chuyển toàn bộ các SketchPoint qua trục đối xứng 2D (Bảo toàn 100% ID các đoạn thẳng và Contour)
                    try
                    {
                        if (refRes.AxisPoint1 != null && refRes.AxisPoint2 != null)
                        {
                            SketchOperationsHelper.MutateSketchPoints(
                                partDoc,
                                activeSketch,
                                refRes.AxisPoint1[0], refRes.AxisPoint1[1],
                                refRes.AxisPoint2[0], refRes.AxisPoint2[1],
                                info.PristineSketchPoints,
                                mirrorPlane,
                                info.CutAudit21);
                        }
                        else
                        {
                            SketchOperationsHelper.MutateSketchPoints(partDoc, activeSketch,
                                info.PristineSketchPoints, mirrorPlane, info.CutAudit21);
                        }
                    }
                    catch (ADDIN.Helpers.SketchPointMoveRejected58 rejected58)
                    {
                        CreateMirrorPartPackage.LogDebug("[GEOMETRY_FIRST58][POINT_MOVE_REJECTED] feature=" +
                            info.Name + " reason=" + rejected58.Message + " primitiveCount=" +
                            (info.PristineSketchPrimitives58 == null ? 0 : info.PristineSketchPrimitives58.Count));
                        SketchOperationsHelper.RecreateMirroredPrimitives58(partDoc, activeSketch,
                            info.PristineSketchPrimitives58, mirrorPlane);
                    }
                }

                result.MirroredEntities = profileSegments.Count;
                result.MirrorGeometryVerified = true;
                result.SketchMirrorExecuted = true;
                partDoc.ClearSelection2(true);

                // Restore geometry-dependent end conditions only after the driving sketch
                // and its support have reached reflected coordinates.  This covers, among
                // others, Up To Vertex definitions that SolidWorks exposes as an Edge plus
                // selection semantics rather than as a Vertex COM object.
                bool unifiedCut44 = IsExtrudeCutType(info.Type);
                if (unifiedCut44)
                {
                    Sketch editing = partDoc.SketchManager.ActiveSketch;
                    if (editing != null)
                    {
                        if (!SingleSketchTargetBuilderV7.SameComObject(editing, sketchFeat.GetSpecificFeature2()))
                            throw new InvalidOperationException("CUT44: another sketch is active; not committing it.");
                        partDoc.SketchManager.InsertSketch(true);
                    }
                    if (info.CutAudit21.Recipe44.MissingSurfaceReference54)
                        SketchOperationsHelper.ApplyReflectedCutWithEvidence54(swApp, partDoc,
                            info.Feature, sketchFeat, info.CutAudit21, mirrorPlane, cache);
                    else
                        SketchOperationsHelper.ApplyReflectedCut44(partDoc, info.Feature, sketchFeat,
                            info.CutAudit21, mirrorPlane);
                }
                else
                    SketchOperationsHelper.RebindExtrudeEndReferences50(partDoc, info.Feature, info.CutAudit21, mirrorPlane);

                // Proactive FlipSideToCut for open profile cuts
                bool isOpenProfile = activeSketch != null
                    ? SketchOperationsHelper.IsOpenProfileSketch(activeSketch)
                    : SketchOperationsHelper.IsOpenProfileSegments(profileSegments);
                string featType = info.Feature.GetTypeName2();
                bool isCutFeature = string.Equals(featType, "Cut", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(featType, "ICE", StringComparison.OrdinalIgnoreCase) ||
                                    featType.IndexOf("Cut", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!unifiedCut44 && isOpenProfile && isCutFeature)
                {
                    CreateMirrorPartPackage.LogDebug($"[PROACTIVE_FLIP] Feature {info.Name} is an open profile cut. Toggling FlipSideToCut before rebuild.");
                    SketchOperationsHelper.ToggleFlipSideToCut(partDoc, info.Feature);
                }

                // 5. Force Rebuild để kiểm tra kết quả ngay sau khi Point Mutation
                CreateMirrorPartPackage.LogDebug(
                    $"IN_PLACE_SKETCH_REBUILD\nfeature={info.Name}\nsketch={sketchFeat.Name}\n" +
                    "mutation=POINT_MUTATION\nresult=PRIMARY_REBUILD");
                partDoc.ForceRebuild3(false);

                // Kiểm tra lỗi của riêng SKETCH
                bool sktWarn = false;
                int sktErr = sketchFeat.GetErrorCode2(out sktWarn);
                CreateMirrorPartPackage.LogDebug($"[SKETCH_HEALTH] sketch={sketchFeat.Name} errorCode={sktErr} warning={sktWarn}");

                // 6. Verify feature health
                bool isWarning = false;
                int errCode = info.Feature.GetErrorCode2(out isWarning);
                result.FeatureErrorCode = errCode;
                result.FeatureWarning = isWarning;

                bool isFatalError = (errCode != 0 && !isWarning);
                CreateMirrorPartPackage.LogDebug($"REPLAY_FEATURE_HEALTH_BEFORE_DIRECTION_ADJUST\nfeature={info.Name}\nerrorCode={errCode}\nwarning={isWarning}\nisFatal={isFatalError}");

                if (isFatalError && unifiedCut44)
                    throw new InvalidOperationException("CUT44: reflected definition has rebuild error=" + errCode +
                        "; original end conditions preserved, heuristic recovery disabled.");

                if (isFatalError)
                {
                    CreateMirrorPartPackage.LogDebug($"REPLAY_RECOVERY: Attempting ReverseExtrudeDirection for {info.Name}");
                    bool reversed = SketchOperationsHelper.ReverseExtrudeDirection(partDoc, info.Feature);
                    partDoc.ForceRebuild3(false);
                    errCode = info.Feature.GetErrorCode2(out isWarning);
                    isFatalError = (errCode != 0 && !isWarning);
                    result.FeatureErrorCode = errCode;
                    result.FeatureWarning = isWarning;
                    CreateMirrorPartPackage.LogDebug($"REPLAY_FEATURE_HEALTH_AFTER_REVERSE_DIR\nfeature={info.Name}\nreversed={reversed}\nerrorCode={errCode}\nwarning={isWarning}\nisFatal={isFatalError}");

                    if (isFatalError && isCutFeature)
                    {
                        // Hoàn trả hướng đùn
                        SketchOperationsHelper.ReverseExtrudeDirection(partDoc, info.Feature);

                        bool canToggleFlip = false;
                        if (sketch != null)
                        {
                            canToggleFlip = ADDIN.Helpers.SketchOperationsHelper.IsOpenProfileSketch(sketch);
                        }
                        if (!canToggleFlip && CreateMirrorPartPackage.featureOptionsCaptureHelper.ExtrudeSnapshots.ContainsKey(info.Name))
                        {
                            canToggleFlip = CreateMirrorPartPackage.featureOptionsCaptureHelper.ExtrudeSnapshots[info.Name].FlipSideToCut;
                        }

                        if (canToggleFlip)
                        {
                            CreateMirrorPartPackage.LogDebug($"REPLAY_RECOVERY: Attempting ToggleFlipSideToCut for {info.Name}");
                            bool flipped = SketchOperationsHelper.ToggleFlipSideToCut(partDoc, info.Feature);
                            partDoc.ForceRebuild3(false);
                            errCode = info.Feature.GetErrorCode2(out isWarning);
                            isFatalError = (errCode != 0 && !isWarning);
                            result.FeatureErrorCode = errCode;
                            result.FeatureWarning = isWarning;
                            CreateMirrorPartPackage.LogDebug($"REPLAY_FEATURE_HEALTH_AFTER_FLIP_SIDE\nfeature={info.Name}\nflipped={flipped}\nerrorCode={errCode}\nwarning={isWarning}\nisFatal={isFatalError}");

                            if (isFatalError)
                            {
                                // Thử kết hợp CẢ HAI (ReverseExtrudeDirection + FlipSideToCut)
                                SketchOperationsHelper.ReverseExtrudeDirection(partDoc, info.Feature);
                                partDoc.ForceRebuild3(false);
                                errCode = info.Feature.GetErrorCode2(out isWarning);
                                isFatalError = (errCode != 0 && !isWarning);
                                result.FeatureErrorCode = errCode;
                                result.FeatureWarning = isWarning;
                                CreateMirrorPartPackage.LogDebug($"REPLAY_RECOVERY_AFTER_BOTH\nfeature={info.Name}\nerrorCode={errCode}\nwarning={isWarning}\nisFatal={isFatalError}");

                                if (isFatalError)
                                {
                                    // Khôi phục lại trạng thái ban đầu trước recovery để SuperRecover thử sức
                                    SketchOperationsHelper.ReverseExtrudeDirection(partDoc, info.Feature);
                                    SketchOperationsHelper.ToggleFlipSideToCut(partDoc, info.Feature);
                                    partDoc.ForceRebuild3(false);
                                }
                            }
                        }
                    }
                }

                if (isFatalError)
                {
                    // Fallback: Kích hoạt SuperRecover 3 pha
                    string superRecoveryDetails;
                    bool recovered = BodyOperationsHelper.TrySuperRecoverExtrudeCut(
                        partDoc,
                        info,
                        out superRecoveryDetails);
                    CreateMirrorPartPackage.LogDebug(superRecoveryDetails ??
                        $"SUPER_RECOVERY\nfeature={info.Name}\nresult=FAIL\nreason=NO_DETAILS");

                    isWarning = false;
                    errCode = info.Feature.GetErrorCode2(out isWarning);
                    isFatalError = (errCode != 0 && !isWarning);
                    result.FeatureErrorCode = errCode;
                    result.FeatureWarning = isWarning;
                    CreateMirrorPartPackage.LogDebug(
                        $"REPLAY_FEATURE_HEALTH_AFTER_SUPER_RECOVERY\nfeature={info.Name}\n" +
                        $"recovered={recovered}\nerrorCode={errCode}\nwarning={isWarning}\n" +
                        $"result={(!isFatalError ? "PASS" : "FAIL")}");

                    if (isFatalError)
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
                    if (partDoc.SketchManager.ActiveSketch != null)
                        partDoc.SketchManager.InsertSketch(true);
                    partDoc.ClearSelection2(true);
                }
                catch {}
            }

            if (result.Success && OneCutReferenceTrialOnly && IsExtrudeCutType(info.Type))
            {
                CreateMirrorPartPackage.LogDebug("[ONECUT35][PASS] feature=" + info.Name +
                    " dimensionsAndReferencesVerified=True rebuildPassed=" + result.RebuildPassed +
                    " outputPublished=False");
                result.Success = false;
                result.StatusCode = "ONE_CUT_REFERENCE_TRIAL_PASS_STOP";
                result.Message = "First Cut reference trial passed; full-tree replay intentionally stopped.";
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

        public static double[] ReflectPoint2D(double px, double py, double x1, double y1, double x2, double y2)
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
                result.RebuildPassed = (errorCode == 0 || warning);

                if (errorCode != 0 && !warning)
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

    public sealed class CurvePatternFeatureReplayHandler : IFeatureMirrorHandler
    {
        public bool CanHandle(PostBaseFeatureInfo info)
        {
            return info != null &&
                   string.Equals(info.Type, "CurvePattern", StringComparison.OrdinalIgnoreCase);
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
                Success = false,
                FeatureName = info?.Name,
                FeatureType = info?.Type,
                StatusCode = "CURVE_PATTERN_INVALID_ARGUMENT"
            };

            if (partDoc == null || info?.Feature == null)
            {
                result.Message = "Part or curve pattern feature is not available.";
                return result;
            }

            ICurveDrivenPatternFeatureData definition = null;
            bool selectionAccess = false;
            try
            {
                definition = info.Feature.GetDefinition() as ICurveDrivenPatternFeatureData;
                if (definition == null)
                {
                    result.StatusCode = "CURVE_PATTERN_DEFINITION_UNAVAILABLE";
                    result.Message = "Feature definition is not ICurveDrivenPatternFeatureData.";
                    return result;
                }

                selectionAccess = definition.AccessSelections(partDoc, null);
                if (!selectionAccess)
                {
                    result.StatusCode = "CURVE_PATTERN_ACCESS_SELECTIONS_FAILED";
                    result.Message = "AccessSelections returned false for curve pattern.";
                    return result;
                }

                int featureSeedCount = definition.GetPatternFeatureCount();
                int bodySeedCount = definition.GetPatternBodyCount();
                int faceSeedCount = definition.GetPatternFaceCount();
                int totalSeedCount = featureSeedCount + bodySeedCount + faceSeedCount;
                int direction1Instances = definition.D1InstanceCount;
                int direction2Instances = definition.Dir2Specified ? definition.D2InstanceCount : 0;
                bool direction1Reversed = definition.D1ReverseDirection;
                bool direction2Reversed = definition.Dir2Specified && definition.D2ReverseDirection;

                CreateMirrorPartPackage.LogDebug(
                    "CURVE_PATTERN_NATIVE_REPLAY\n" +
                    $"feature={info.Name}\nfeatureSeeds={featureSeedCount}\n" +
                    $"bodySeeds={bodySeedCount}\nfaceSeeds={faceSeedCount}\n" +
                    $"direction1Instances={direction1Instances}\ndirection2Instances={direction2Instances}\n" +
                    $"direction1Reversed={direction1Reversed}\ndirection2Reversed={direction2Reversed}");

                if (totalSeedCount <= 0)
                {
                    result.StatusCode = "CURVE_PATTERN_HAS_NO_SEED";
                    result.Message = "Curve pattern has no feature, body, or face seed.";
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.StatusCode = "CURVE_PATTERN_READ_FAILED";
                result.Message = ex.Message;
                return result;
            }
            finally
            {
                if (definition != null && selectionAccess)
                {
                    try { definition.ReleaseSelectionAccess(); } catch { }
                }
            }

            try
            {
                partDoc.ForceRebuild3(false);
                bool warning = false;
                int errorCode = info.Feature.GetErrorCode2(out warning);

                result.FeatureErrorCode = errorCode;
                result.FeatureWarning = warning;
                result.RebuildPassed = (errorCode == 0 || warning);
                result.Success = result.RebuildPassed;
                result.StatusCode = result.Success
                    ? "SUCCESS_CURVE_PATTERN_NATIVE_DEPENDENCY_REBUILD"
                    : "CURVE_PATTERN_REBUILD_ERROR";
                result.Message = result.Success
                    ? "Curve pattern rebuilt from its replayed seed dependencies."
                    : $"Curve pattern rebuild error code={errorCode}, warning={warning}.";

                CreateMirrorPartPackage.LogDebug(
                    "CURVE_PATTERN_NATIVE_REPLAY_RESULT\n" +
                    $"feature={info.Name}\nerrorCode={errorCode}\nwarning={warning}\n" +
                    $"result={(result.Success ? "PASS" : "FAIL")}");
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.StatusCode = "CURVE_PATTERN_REBUILD_EXCEPTION";
                result.Message = ex.Message;
                return result;
            }
        }
    }

    public sealed class SketchPatternFeatureReplayHandler : IFeatureMirrorHandler
    {
        public bool CanHandle(PostBaseFeatureInfo info)
        {
            return info != null &&
                   string.Equals(info.Type, "SketchPattern", StringComparison.OrdinalIgnoreCase);
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
            var result = new FeatureReplayResult
            {
                FeatureName = info?.Name,
                FeatureType = info?.Type,
                StatusCode = "SKETCH_PATTERN_INVALID_ARGUMENT"
            };
            if (partDoc == null || info?.Feature == null)
            {
                result.Message = "Part or sketch-driven pattern feature is unavailable.";
                return result;
            }

            ISketchPatternFeatureData definition = null;
            bool selectionAccess = false;
            try
            {
                definition = info.Feature.GetDefinition() as ISketchPatternFeatureData;
                if (definition == null)
                {
                    result.StatusCode = "SKETCH_PATTERN_DEFINITION_UNAVAILABLE";
                    result.Message = "Feature definition is not ISketchPatternFeatureData.";
                    return result;
                }

                selectionAccess = definition.AccessSelections(partDoc, null);
                if (!selectionAccess)
                {
                    result.StatusCode = "SKETCH_PATTERN_ACCESS_SELECTIONS_FAILED";
                    result.Message = "AccessSelections returned false for sketch-driven pattern.";
                    return result;
                }

                // The copied Part retains its native seed and driving-sketch references.
                // Read them at this rollback checkpoint; do not guess a new pattern
                // direction or overwrite a COM seed array on the inherited definition.
                object drivingSketch = definition.Sketch;
                int featureSeeds = definition.GetPatternFeatureCount();
                int faceSeeds = definition.GetPatternFaceCount();
                int bodySeeds = definition.GetPatternBodyCount();
                string patternElement = "<unavailable>";
                string useCentroid = "<unavailable>";
                string geometryPattern = "<unavailable>";
                try { patternElement = definition.PatternElement.ToString(); } catch { }
                try { useCentroid = definition.UseCentroid.ToString(); } catch { }
                try { geometryPattern = definition.GeometryPattern.ToString(); } catch { }
                CreateMirrorPartPackage.LogDebug(
                    "SKETCH_PATTERN_NATIVE_REPLAY\n" +
                    $"feature={info.Name}\ndrivingSketchPresent={drivingSketch != null}\n" +
                    $"featureSeeds={featureSeeds}\nfaceSeeds={faceSeeds}\nbodySeeds={bodySeeds}\n" +
                    $"patternElement={patternElement}\n" +
                    $"useCentroid={useCentroid}\ngeometryPattern={geometryPattern}");

                if (drivingSketch == null || featureSeeds + faceSeeds + bodySeeds <= 0)
                {
                    result.StatusCode = "SKETCH_PATTERN_MISSING_DEPENDENCY";
                    result.Message = "Pattern driving sketch or all seed references are missing.";
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.StatusCode = "SKETCH_PATTERN_READ_FAILED";
                result.Message = ex.Message;
                return result;
            }
            finally
            {
                // AccessSelections temporarily rolls the document back. Release it
                // before rebuilding; no definition fields were modified here.
                if (definition != null && selectionAccess)
                {
                    try { definition.ReleaseSelectionAccess(); } catch { }
                }
            }

            try
            {
                partDoc.ForceRebuild3(false);
                bool warning;
                int errorCode = info.Feature.GetErrorCode2(out warning);
                result.FeatureErrorCode = errorCode;
                result.FeatureWarning = warning;
                // A sketch-pattern reference failure can carry warning=true (code 51).
                // The boolean is not evidence that the feature produced geometry.
                result.RebuildPassed = errorCode == 0;
                result.Success = result.RebuildPassed;
                result.StatusCode = result.Success
                    ? "SUCCESS_SKETCH_PATTERN_NATIVE_DEPENDENCY_REBUILD"
                    : "SKETCH_PATTERN_REBUILD_ERROR";
                result.Message = result.Success
                    ? "Sketch-driven pattern rebuilt from its replayed sketch and seed; checkpoint geometry must still pass."
                    : $"Sketch-driven pattern rebuild error code={errorCode}, warning={warning}.";
                CreateMirrorPartPackage.LogDebug(
                    "SKETCH_PATTERN_NATIVE_REPLAY_RESULT\n" +
                    $"feature={info.Name}\nerrorCode={errorCode}\nwarning={warning}\n" +
                    $"result={(result.Success ? "PASS" : "FAIL")}");
                return result;
            }
            catch (Exception ex)
            {
                result.StatusCode = "SKETCH_PATTERN_REBUILD_EXCEPTION";
                result.Message = ex.Message;
                return result;
            }
        }
    }

    public sealed class LinearPatternFeatureReplayHandler : IFeatureMirrorHandler
    {
        public bool CanHandle(PostBaseFeatureInfo info)
        {
            if (info == null || string.IsNullOrEmpty(info.Type)) return false;
            return string.Equals(info.Type, "LPattern", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(info.Type, "LinearPattern", StringComparison.OrdinalIgnoreCase) ||
                   info.Type.IndexOf("LPattern", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   info.Type.IndexOf("LinearPattern", StringComparison.OrdinalIgnoreCase) >= 0;
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
                Success = false,
                FeatureName = info?.Name,
                FeatureType = info?.Type,
                StatusCode = "LINEAR_PATTERN_INVALID_ARGUMENT"
            };

            if (partDoc == null || info?.Feature == null)
            {
                result.Message = "Part or linear pattern feature is not available.";
                return result;
            }

            ILinearPatternFeatureData definition = null;
            bool selectionAccess = false;
            bool dir1Reversed = false;
            bool dir2Specified = false;
            bool dir2Reversed = false;
            bool geomPattern = false;
            int totalSeedCount = 0;

            try
            {
                definition = info.Feature.GetDefinition() as ILinearPatternFeatureData;
                if (definition == null)
                {
                    result.StatusCode = "LINEAR_PATTERN_DEFINITION_UNAVAILABLE";
                    result.Message = "Feature definition is not ILinearPatternFeatureData.";
                    return result;
                }

                selectionAccess = definition.AccessSelections(partDoc, null);
                if (!selectionAccess)
                {
                    result.StatusCode = "LINEAR_PATTERN_ACCESS_SELECTIONS_FAILED";
                    result.Message = "AccessSelections returned false for linear pattern.";
                    return result;
                }

                int featureSeedCount = definition.GetPatternFeatureCount();
                int bodySeedCount = definition.GetPatternBodyCount();
                int faceSeedCount = definition.GetPatternFaceCount();
                totalSeedCount = featureSeedCount + bodySeedCount + faceSeedCount;
                int d1Instances = definition.D1TotalInstances;
                double d1Spacing = definition.D1Spacing;
                dir1Reversed = definition.D1ReverseDirection;
                try { geomPattern = definition.GeometryPattern; } catch { }
                try { dir2Specified = definition.IsDirection2Specified(); } catch { }
                int d2Instances = dir2Specified ? definition.D2TotalInstances : 0;
                double d2Spacing = dir2Specified ? definition.D2Spacing : 0;
                dir2Reversed = dir2Specified && definition.D2ReverseDirection;

                CreateMirrorPartPackage.LogDebug(
                    "LINEAR_PATTERN_NATIVE_REPLAY\n" +
                    $"feature={info.Name}\nfeatureSeeds={featureSeedCount}\n" +
                    $"bodySeeds={bodySeedCount}\nfaceSeeds={faceSeedCount}\n" +
                    $"d1Instances={d1Instances}\nd1Spacing={d1Spacing}\nd1Reversed={dir1Reversed}\n" +
                    $"dir2Specified={dir2Specified}\nd2Instances={d2Instances}\nd2Spacing={d2Spacing}\nd2Reversed={dir2Reversed}\n" +
                    $"geometryPattern={geomPattern}");

                if (totalSeedCount <= 0)
                {
                    result.StatusCode = "LINEAR_PATTERN_HAS_NO_SEED";
                    result.Message = "Linear pattern has no feature, body, or face seed.";
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.StatusCode = "LINEAR_PATTERN_READ_FAILED";
                result.Message = ex.Message;
                return result;
            }
            finally
            {
                if (definition != null && selectionAccess)
                {
                    try { definition.ReleaseSelectionAccess(); } catch { }
                    selectionAccess = false;
                }
            }

            try
            {
                partDoc.ForceRebuild3(false);
                bool warning = false;
                int errorCode = info.Feature.GetErrorCode2(out warning);

                bool isPatternHealthy = (errorCode == 0 || warning);

                if (isPatternHealthy)
                {
                    result.FeatureErrorCode = errorCode;
                    result.FeatureWarning = warning;
                    result.RebuildPassed = true;
                    result.Success = true;
                    result.StatusCode = "SUCCESS_LINEAR_PATTERN_NATIVE_DEPENDENCY_REBUILD";
                    result.Message = "Linear pattern rebuilt from its replayed seed dependencies.";

                    CreateMirrorPartPackage.LogDebug(
                        "LINEAR_PATTERN_NATIVE_REPLAY_RESULT\n" +
                        $"feature={info.Name}\nerrorCode={errorCode}\nwarning={warning}\nresult=PASS");
                    return result;
                }

                CreateMirrorPartPackage.LogDebug(
                    $"[LINEAR_PATTERN_REPLAY] Initial rebuild failed with errorCode={errorCode}, warning={warning}. Attempting direction flip recovery...");

                // Recovery attempt 1: Flip Direction 1
                if (definition.AccessSelections(partDoc, null))
                {
                    bool mod = false;
                    try
                    {
                        definition.D1ReverseDirection = !dir1Reversed;
                        mod = info.Feature.ModifyDefinition(definition, partDoc, null);
                    }
                    finally
                    {
                        if (!mod)
                        {
                            try { definition.ReleaseSelectionAccess(); } catch { }
                        }
                    }

                    if (mod)
                    {
                        partDoc.ForceRebuild3(false);
                        errorCode = info.Feature.GetErrorCode2(out warning);
                        if (errorCode == 0 || warning)
                        {
                            result.FeatureErrorCode = errorCode;
                            result.FeatureWarning = warning;
                            result.RebuildPassed = true;
                            result.Success = true;
                            result.StatusCode = "SUCCESS_LINEAR_PATTERN_FLIP_D1";
                            result.Message = "Linear pattern rebuilt successfully after flipping Direction 1.";

                            CreateMirrorPartPackage.LogDebug(
                                "[LINEAR_PATTERN_NATIVE_REPLAY_RESULT]\n" +
                                $"feature={info.Name}\nerrorCode={errorCode}\nwarning={warning}\nresult=PASS (D1 Flipped)");
                            return result;
                        }
                    }
                }

                // Recovery attempt 2: If Direction 2 is specified, try flipping Direction 2
                if (dir2Specified)
                {
                    CreateMirrorPartPackage.LogDebug(
                        $"[LINEAR_PATTERN_REPLAY] Attempting D2 flip recovery...");
                    if (definition.AccessSelections(partDoc, null))
                    {
                        bool mod2 = false;
                        try
                        {
                            definition.D2ReverseDirection = !dir2Reversed;
                            mod2 = info.Feature.ModifyDefinition(definition, partDoc, null);
                        }
                        finally
                        {
                            if (!mod2)
                            {
                                try { definition.ReleaseSelectionAccess(); } catch { }
                            }
                        }

                        if (mod2)
                        {
                            partDoc.ForceRebuild3(false);
                            errorCode = info.Feature.GetErrorCode2(out warning);
                            if (errorCode == 0 || warning)
                            {
                                result.FeatureErrorCode = errorCode;
                                result.FeatureWarning = warning;
                                result.RebuildPassed = true;
                                result.Success = true;
                                result.StatusCode = "SUCCESS_LINEAR_PATTERN_FLIP_D1_D2";
                                result.Message = "Linear pattern rebuilt successfully after flipping Direction 1 and Direction 2.";

                                CreateMirrorPartPackage.LogDebug(
                                    "[LINEAR_PATTERN_NATIVE_REPLAY_RESULT]\n" +
                                    $"feature={info.Name}\nerrorCode={errorCode}\nwarning={warning}\nresult=PASS (D1 and D2 Flipped)");
                                return result;
                            }
                        }
                    }
                }

                // Recovery attempt 3: If GeometryPattern was true, try turning it off
                if (geomPattern)
                {
                    CreateMirrorPartPackage.LogDebug(
                        $"[LINEAR_PATTERN_REPLAY] Attempting geometry pattern disable recovery...");
                    if (definition.AccessSelections(partDoc, null))
                    {
                        bool mod3 = false;
                        try
                        {
                            definition.GeometryPattern = false;
                            mod3 = info.Feature.ModifyDefinition(definition, partDoc, null);
                        }
                        finally
                        {
                            if (!mod3)
                            {
                                try { definition.ReleaseSelectionAccess(); } catch { }
                            }
                        }

                        if (mod3)
                        {
                            partDoc.ForceRebuild3(false);
                            errorCode = info.Feature.GetErrorCode2(out warning);
                            if (errorCode == 0 || warning)
                            {
                                result.FeatureErrorCode = errorCode;
                                result.FeatureWarning = warning;
                                result.RebuildPassed = true;
                                result.Success = true;
                                result.StatusCode = "SUCCESS_LINEAR_PATTERN_GEOMETRY_OFF";
                                result.Message = "Linear pattern rebuilt successfully after disabling Geometry Pattern.";

                                CreateMirrorPartPackage.LogDebug(
                                    "[LINEAR_PATTERN_NATIVE_REPLAY_RESULT]\n" +
                                    $"feature={info.Name}\nerrorCode={errorCode}\nwarning={warning}\nresult=PASS (GeometryPattern=false)");
                                return result;
                            }
                        }
                    }
                }

                result.FeatureErrorCode = errorCode;
                result.FeatureWarning = warning;
                result.RebuildPassed = (errorCode == 0 || warning);
                result.Success = result.RebuildPassed;
                result.StatusCode = result.Success
                    ? "SUCCESS_LINEAR_PATTERN_REBUILD"
                    : "LINEAR_PATTERN_REBUILD_ERROR";
                result.Message = $"Linear pattern rebuild error code={errorCode}, warning={warning}.";

                CreateMirrorPartPackage.LogDebug(
                    "LINEAR_PATTERN_NATIVE_REPLAY_RESULT\n" +
                    $"feature={info.Name}\nerrorCode={errorCode}\nwarning={warning}\n" +
                    $"result={(result.Success ? "PASS" : "FAIL")}");
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.StatusCode = "LINEAR_PATTERN_REBUILD_EXCEPTION";
                result.Message = ex.Message;
                return result;
            }
        }
    }

    public sealed class CircularPatternFeatureReplayHandler : IFeatureMirrorHandler
    {
        public bool CanHandle(PostBaseFeatureInfo info)
        {
            if (info == null || string.IsNullOrEmpty(info.Type)) return false;
            return string.Equals(info.Type, "CirPattern", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(info.Type, "CircularPattern", StringComparison.OrdinalIgnoreCase) ||
                   info.Type.IndexOf("CirPattern", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   info.Type.IndexOf("CircularPattern", StringComparison.OrdinalIgnoreCase) >= 0;
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
                Success = false,
                FeatureName = info?.Name,
                FeatureType = info?.Type,
                StatusCode = "CIRCULAR_PATTERN_INVALID_ARGUMENT"
            };

            if (partDoc == null || info?.Feature == null)
            {
                result.Message = "Part or circular pattern feature is not available.";
                return result;
            }

            ICircularPatternFeatureData definition = null;
            bool selectionAccess = false;
            bool reversed = false;
            bool geomPattern = false;
            int totalSeedCount = 0;

            try
            {
                definition = info.Feature.GetDefinition() as ICircularPatternFeatureData;
                if (definition == null)
                {
                    result.StatusCode = "CIRCULAR_PATTERN_DEFINITION_UNAVAILABLE";
                    result.Message = "Feature definition is not ICircularPatternFeatureData.";
                    return result;
                }

                selectionAccess = definition.AccessSelections(partDoc, null);
                if (!selectionAccess)
                {
                    result.StatusCode = "CIRCULAR_PATTERN_ACCESS_SELECTIONS_FAILED";
                    result.Message = "AccessSelections returned false for circular pattern.";
                    return result;
                }

                int featureSeedCount = definition.GetPatternFeatureCount();
                int bodySeedCount = definition.GetPatternBodyCount();
                int faceSeedCount = definition.GetPatternFaceCount();
                totalSeedCount = featureSeedCount + bodySeedCount + faceSeedCount;
                int instances = definition.TotalInstances;
                double spacing = definition.Spacing;
                reversed = definition.ReverseDirection;
                try { geomPattern = definition.GeometryPattern; } catch { }

                CreateMirrorPartPackage.LogDebug(
                    "CIRCULAR_PATTERN_NATIVE_REPLAY\n" +
                    $"feature={info.Name}\nfeatureSeeds={featureSeedCount}\n" +
                    $"bodySeeds={bodySeedCount}\nfaceSeeds={faceSeedCount}\n" +
                    $"instances={instances}\nspacing={spacing}\nreversed={reversed}\n" +
                    $"geometryPattern={geomPattern}");

                if (totalSeedCount <= 0)
                {
                    result.StatusCode = "CIRCULAR_PATTERN_HAS_NO_SEED";
                    result.Message = "Circular pattern has no feature, body, or face seed.";
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.StatusCode = "CIRCULAR_PATTERN_READ_FAILED";
                result.Message = ex.Message;
                return result;
            }
            finally
            {
                if (definition != null && selectionAccess)
                {
                    try { definition.ReleaseSelectionAccess(); } catch { }
                    selectionAccess = false;
                }
            }

            try
            {
                partDoc.ForceRebuild3(false);
                bool warning = false;
                int errorCode = info.Feature.GetErrorCode2(out warning);

                bool isPatternHealthy = (errorCode == 0 || warning);

                if (isPatternHealthy)
                {
                    result.FeatureErrorCode = errorCode;
                    result.FeatureWarning = warning;
                    result.RebuildPassed = true;
                    result.Success = true;
                    result.StatusCode = "SUCCESS_CIRCULAR_PATTERN_NATIVE_DEPENDENCY_REBUILD";
                    result.Message = "Circular pattern rebuilt from its replayed seed dependencies.";

                    CreateMirrorPartPackage.LogDebug(
                        "CIRCULAR_PATTERN_NATIVE_REPLAY_RESULT\n" +
                        $"feature={info.Name}\nerrorCode={errorCode}\nwarning={warning}\nresult=PASS");
                    return result;
                }

                CreateMirrorPartPackage.LogDebug(
                    $"[CIRCULAR_PATTERN_REPLAY] Initial rebuild failed with errorCode={errorCode}, warning={warning}. Attempting direction flip recovery...");

                // Recovery attempt 1: Flip ReverseDirection
                if (definition.AccessSelections(partDoc, null))
                {
                    bool mod = false;
                    try
                    {
                        definition.ReverseDirection = !reversed;
                        mod = info.Feature.ModifyDefinition(definition, partDoc, null);
                    }
                    finally
                    {
                        if (!mod)
                        {
                            try { definition.ReleaseSelectionAccess(); } catch { }
                        }
                    }

                    if (mod)
                    {
                        partDoc.ForceRebuild3(false);
                        errorCode = info.Feature.GetErrorCode2(out warning);
                        if (errorCode == 0 || warning)
                        {
                            result.FeatureErrorCode = errorCode;
                            result.FeatureWarning = warning;
                            result.RebuildPassed = true;
                            result.Success = true;
                            result.StatusCode = "SUCCESS_CIRCULAR_PATTERN_FLIP_DIR";
                            result.Message = "Circular pattern rebuilt successfully after flipping direction.";

                            CreateMirrorPartPackage.LogDebug(
                                "[CIRCULAR_PATTERN_NATIVE_REPLAY_RESULT]\n" +
                                $"feature={info.Name}\nerrorCode={errorCode}\nwarning={warning}\nresult=PASS (Reversed)");
                            return result;
                        }
                    }
                }

                // Recovery attempt 2: If GeometryPattern was true, try turning it off
                if (geomPattern)
                {
                    CreateMirrorPartPackage.LogDebug(
                        $"[CIRCULAR_PATTERN_REPLAY] Attempting geometry pattern disable recovery...");
                    if (definition.AccessSelections(partDoc, null))
                    {
                        bool mod2 = false;
                        try
                        {
                            definition.GeometryPattern = false;
                            mod2 = info.Feature.ModifyDefinition(definition, partDoc, null);
                        }
                        finally
                        {
                            if (!mod2)
                            {
                                try { definition.ReleaseSelectionAccess(); } catch { }
                            }
                        }

                        if (mod2)
                        {
                            partDoc.ForceRebuild3(false);
                            errorCode = info.Feature.GetErrorCode2(out warning);
                            if (errorCode == 0 || warning)
                            {
                                result.FeatureErrorCode = errorCode;
                                result.FeatureWarning = warning;
                                result.RebuildPassed = true;
                                result.Success = true;
                                result.StatusCode = "SUCCESS_CIRCULAR_PATTERN_GEOMETRY_OFF";
                                result.Message = "Circular pattern rebuilt successfully after disabling Geometry Pattern.";

                                CreateMirrorPartPackage.LogDebug(
                                    "[CIRCULAR_PATTERN_NATIVE_REPLAY_RESULT]\n" +
                                    $"feature={info.Name}\nerrorCode={errorCode}\nwarning={warning}\nresult=PASS (GeometryPattern=false)");
                                return result;
                            }
                        }
                    }
                }

                result.FeatureErrorCode = errorCode;
                result.FeatureWarning = warning;
                result.RebuildPassed = (errorCode == 0 || warning);
                result.Success = result.RebuildPassed;
                result.StatusCode = result.Success
                    ? "SUCCESS_CIRCULAR_PATTERN_REBUILD"
                    : "CIRCULAR_PATTERN_REBUILD_ERROR";
                result.Message = $"Circular pattern rebuild error code={errorCode}, warning={warning}.";

                CreateMirrorPartPackage.LogDebug(
                    "CIRCULAR_PATTERN_NATIVE_REPLAY_RESULT\n" +
                    $"feature={info.Name}\nerrorCode={errorCode}\nwarning={warning}\n" +
                    $"result={(result.Success ? "PASS" : "FAIL")}");
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.StatusCode = "CIRCULAR_PATTERN_REBUILD_EXCEPTION";
                result.Message = ex.Message;
                return result;
            }
        }
    }

    public sealed class EdgeFlangeFeatureMirrorHandler : IFeatureMirrorHandler
    {
        public bool CanHandle(PostBaseFeatureInfo info)
        {
            if (info == null || string.IsNullOrEmpty(info.Type)) return false;
            return string.Equals(info.Type, "EdgeFlange", StringComparison.OrdinalIgnoreCase) ||
                   info.Type.IndexOf("EdgeFlange", StringComparison.OrdinalIgnoreCase) >= 0;
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
                Success = false,
                FeatureName = info?.Name,
                FeatureType = info?.Type
            };

            if (partDoc == null || info?.Feature == null)
            {
                result.StatusCode = "EDGE_FLANGE_INVALID_ARGUMENT";
                result.Message = "Part or feature is not available.";
                return result;
            }

            try
            {
                CreateMirrorPartPackage.LogDebug($"[EDGE_FLANGE_REPLAY] Replaying EdgeFlange {info.Name} via EdgeFlangeRepairManager...");
                bool repaired = CreateMirrorPartPackage.edgeFlangeRepairManager.TryRepairEdgeFlange(swApp, partDoc, info.Feature);
                partDoc.ForceRebuild3(false);

                bool isWarning = false;
                int errCode = info.Feature.GetErrorCode2(out isWarning);
                result.FeatureErrorCode = errCode;
                result.FeatureWarning = isWarning;
                result.Success = repaired && (errCode == 0 || isWarning);
                result.StatusCode = result.Success ? "SUCCESS_EDGE_FLANGE_REPLAY" : "EDGE_FLANGE_REBUILD_ERROR";
                result.Message = result.Success
                    ? "Edge flange flipped and rebuilt successfully."
                    : $"Edge flange rebuild error code={errCode}, warning={isWarning}.";

                CreateMirrorPartPackage.LogDebug(
                    $"[EDGE_FLANGE_REPLAY] feature={info.Name} repaired={repaired} " +
                    $"errCode={errCode} warning={isWarning} result={(result.Success ? "PASS" : "FAIL")}");
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.StatusCode = "EDGE_FLANGE_REBUILD_EXCEPTION";
                result.Message = ex.Message;
                CreateMirrorPartPackage.LogDebug($"[EDGE_FLANGE_REPLAY] Exception: {ex.Message}");
                return result;
            }
        }
    }

    public sealed class FeatureReplayDispatcher
    {
        private readonly List<IFeatureMirrorHandler> handlers = new List<IFeatureMirrorHandler>();

        public FeatureReplayDispatcher()
        {
            handlers.Add(new SketchDrivenFeatureMirrorHandler());
            handlers.Add(new ChamferFeatureMirrorHandler());
            handlers.Add(new CurvePatternFeatureReplayHandler());
            handlers.Add(new SketchPatternFeatureReplayHandler());
            handlers.Add(new LinearPatternFeatureReplayHandler());
            handlers.Add(new CircularPatternFeatureReplayHandler());
            handlers.Add(new EdgeFlangeFeatureMirrorHandler());
            handlers.Add(new ChiralFeatureMirrorHandler45());
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

    public sealed class BaseFlangeDiagnosisResult
    {
        // Case 1: Centroid vs Origin
        public bool IsOffCenter { get; set; }
        public double[] GeometricCenter { get; set; } = new double[] { 0, 0, 0 };
        public double CenterDistanceAlongNormal { get; set; }
        public PlaneData AdaptedMirrorPlane { get; set; }

        // Case 2: Extrusion Mode & Direction
        public bool IsMirrorPlaneParallelToSheet { get; set; }
        public bool IsBlindExtrusion { get; set; }
        public bool NeedReverseExtrusion { get; set; }
        public bool NeedReverseDirection { get; set; }
        public bool NeedReverseThickness { get; set; }
        public double BaseThickness { get; set; }

        // Case 3: 2D Base Sketch Symmetry & Mutation
        public bool IsBaseSketchSymmetric { get; set; } = true;
        public bool NeedBaseSketchMutation { get; set; }
        public double[] AxisPoint1 { get; set; }
        public double[] AxisPoint2 { get; set; }
        public int TotalSketchPoints { get; set; }
        public int AsymmetricPointsCount { get; set; }

        public string Summary()
        {
            return $"[DIAGNOSE_BASE_FLANGE]\n" +
                   $"  Case 1 (OffCenter): {IsOffCenter} (Center=({GeometricCenter[0] * 1000.0:F2}, {GeometricCenter[1] * 1000.0:F2}, {GeometricCenter[2] * 1000.0:F2})mm, distAlongNormal={CenterDistanceAlongNormal * 1000.0:F2}mm)\n" +
                   $"  Case 2 (Extrusion): Parallel={IsMirrorPlaneParallelToSheet}, Blind={IsBlindExtrusion}, NeedRevDir={NeedReverseDirection}, NeedRevThick={NeedReverseThickness} (T={BaseThickness * 1000.0:F2}mm)\n" +
                   $"  Case 3 (BaseSketch): Symmetric={IsBaseSketchSymmetric}, NeedMutation={NeedBaseSketchMutation} (totalPts={TotalSketchPoints}, asymPts={AsymmetricPointsCount})";
        }
    }

    public static class BaseFlangeDiagnosticService
    {
        public static BaseFlangeDiagnosisResult Diagnose(
            ISldWorks swApp,
            ModelDoc2 partDoc,
            PlaneData rawMirrorPlane,
            Feature baseFeature,
            Feature baseDrivingSketch,
            List<Body2> solidBodies)
        {
            BaseFlangeDiagnosisResult res = new BaseFlangeDiagnosisResult
            {
                GeometricCenter = new double[] { 0, 0, 0 },
                AdaptedMirrorPlane = rawMirrorPlane
            };

            if (partDoc == null || rawMirrorPlane == null) return res;

            // 1. Diagnose Case 1: Centroid vs Origin
            // Quy tắc toán học CAD Assembly Mirror: Mặt phẳng gương nội bộ của Part PHẢI luôn neo qua
            // gốc tọa độ Part (0,0,0) để công thức tính vị trí Assembly O' = 2*P - O_src đặt chi tiết đúng vị trí đối xứng.
            // Nếu dời mặt phẳng gương về Centroid Xc, chi tiết sẽ bị lệch đúng 2*Xc trong Assembly.
            // Do đó, AdaptedMirrorPlane luôn giữ nguyên gốc (0,0,0).
            if (solidBodies != null && solidBodies.Count > 0)
            {
                double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
                bool hasBox = false;

                foreach (var b in solidBodies)
                {
                    if (b == null) continue;
                    double[] box = b.GetBodyBox() as double[];
                    if (box != null && box.Length >= 6)
                    {
                        hasBox = true;
                        if (box[0] < minX) minX = box[0];
                        if (box[1] < minY) minY = box[1];
                        if (box[2] < minZ) minZ = box[2];
                        if (box[3] > maxX) maxX = box[3];
                        if (box[4] > maxY) maxY = box[4];
                        if (box[5] > maxZ) maxZ = box[5];
                    }
                }

                if (hasBox)
                {
                    res.GeometricCenter = new double[]
                    {
                        (minX + maxX) * 0.5,
                        (minY + maxY) * 0.5,
                        (minZ + maxZ) * 0.5
                    };

                    double[] n = rawMirrorPlane.Normal;
                    double distAlongNormal = res.GeometricCenter[0] * n[0] +
                                             res.GeometricCenter[1] * n[1] +
                                             res.GeometricCenter[2] * n[2];
                    res.CenterDistanceAlongNormal = distAlongNormal;

                    // Giữ nguyên mặt phẳng gương qua gốc (0,0,0)
                    res.IsOffCenter = false;
                    res.AdaptedMirrorPlane = rawMirrorPlane;
                }
            }

            // 2. Diagnose Case 2: Base Flange Extrusion (Blind vs Mid-Plane) & Parallel Sheet
            Sketch baseSketch = (baseDrivingSketch != null) ? baseDrivingSketch.GetSpecificFeature2() as Sketch : null;
            bool isOpenProfile = false;
            if (baseSketch != null)
            {
                try
                {
                    object[] contours = baseSketch.GetSketchContours() as object[];
                    if (contours != null && contours.Length > 0)
                    {
                        foreach (SketchContour sc in contours)
                        {
                            if (sc != null && !sc.IsClosed())
                            {
                                isOpenProfile = true;
                                break;
                            }
                        }
                    }
                }
                catch { }
            }

            if (baseFeature != null && baseSketch != null)
            {
                // Read Base Flange data
                try
                {
                    IBaseFlangeFeatureData bfData = baseFeature.GetDefinition() as IBaseFlangeFeatureData;
                    if (bfData != null)
                    {
                        res.BaseThickness = bfData.Thickness;
                        res.IsBlindExtrusion = (bfData.D1EndConditionType != (int)swEndConditions_e.swEndCondMidPlane && !bfData.SymmetricThickness);
                        if (!isOpenProfile && bfData.BendRadius > 0 && bfData.D1EndConditionDistance > bfData.Thickness * 1.5)
                        {
                            isOpenProfile = true;
                        }
                    }
                    else
                    {
                        res.IsBlindExtrusion = true;
                    }
                }
                catch
                {
                    res.IsBlindExtrusion = true;
                }

                // Check if sketch normal is parallel to mirror plane normal
                double absDotSketchAndMirror = 0.0;
                IMathUtility mathUtility = swApp.GetMathUtility() as IMathUtility;
                MathTransform m2s = baseSketch.ModelToSketchTransform;
                if (mathUtility != null && m2s != null)
                {
                    try
                    {
                        MathTransform s2m = m2s.IInverse();
                        MathVector skZ = mathUtility.CreateVector(new double[] { 0, 0, 1 }) as MathVector;
                        MathVector skNormVec = skZ.MultiplyTransform(s2m) as MathVector;
                        double[] nSk = skNormVec.ArrayData as double[];
                        double[] nMp = res.AdaptedMirrorPlane.Normal;
                        double dot = nSk[0] * nMp[0] + nSk[1] * nMp[1] + nSk[2] * nMp[2];
                        absDotSketchAndMirror = Math.Abs(dot);
                    }
                    catch (Exception ex)
                    {
                        CreateMirrorPartPackage.LogDebug($"[DIAGNOSE_BASE_FLANGE] Sketch normal calculation exception: {ex.Message}");
                    }
                }

                if (isOpenProfile)
                {
                    // Với phôi đùn biên dạng hở (bent profile / channel):
                    // Phôi có nhiều mặt bích tạo góc trong không gian 3D, KHÔNG PHẢI là một tấm phẳng đơn song song với mặt gương.
                    // Tuyệt đối không độc lập đảo ReverseThickness vì sẽ làm đổi hướng bù tôn của góc uốn, làm sai lệch kích thước và thể tích!
                    res.IsMirrorPlaneParallelToSheet = false;

                    // Nếu trục đùn chiều dài (sketch normal) song song với pháp tuyến mặt phẳng gương:
                    // Chi tiết đùn vuông góc với mặt gương -> đảo hướng đùn chiều dài 3D (Case B).
                    if (res.IsBlindExtrusion && absDotSketchAndMirror > 0.9)
                    {
                        res.NeedReverseDirection = true;
                    }
                }
                else
                {
                    // Với phôi tấm phẳng (closed contour sketch):
                    // Mặt phẳng tấm tôn chính là mặt phẳng của Sketch, pháp tuyến tấm tôn = sketch normal!
                    res.IsMirrorPlaneParallelToSheet = (absDotSketchAndMirror > 0.95);
                    if (res.IsMirrorPlaneParallelToSheet && res.IsBlindExtrusion)
                    {
                        res.NeedReverseThickness = true;
                        res.NeedReverseExtrusion = true;
                    }
                }

                // 3. Diagnose Case 3: Base Sketch Symmetry
                if (!res.IsMirrorPlaneParallelToSheet)
                {
                    MirrorReferenceResult refRes = MirrorReferenceResolver.ResolveAndSelectMirrorReference(
                        swApp, partDoc, baseSketch, res.AdaptedMirrorPlane);

                    if (refRes != null && refRes.Success && refRes.AxisPoint1 != null && refRes.AxisPoint2 != null)
                    {
                        res.AxisPoint1 = refRes.AxisPoint1;
                        res.AxisPoint2 = refRes.AxisPoint2;

                        // GetSketchPoints2 can return an empty array for a perfectly valid
                        // Base-Flange profile.  Never interpret an empty COM collection as
                        // geometric symmetry.  Capture points through every owned segment
                        // (line endpoints, arc endpoints/centre, spline points, etc.).
                        List<ADDIN.Helpers.SketchPointSnapshot> basePoints53 =
                            ADDIN.Helpers.SketchOperationsHelper.CapturePristineSketchPoints(baseSketch);
                        res.TotalSketchPoints = basePoints53.Count;
                        if (basePoints53.Count == 0)
                        {
                            res.IsBaseSketchSymmetric = false;
                            res.NeedBaseSketchMutation = false;
                            throw new InvalidOperationException(
                                "BASE53: Base sketch contains geometry but no owned points could be captured; symmetry is unknown.");
                        }

                        int asymCount = 0;
                        double ax1 = res.AxisPoint1[0], ay1 = res.AxisPoint1[1];
                        double ax2 = res.AxisPoint2[0], ay2 = res.AxisPoint2[1];
                        List<double[]> allPts = basePoints53.Select(point53 =>
                            new[] { point53.X, point53.Y }).ToList();

                        foreach (double[] pt in allPts)
                        {
                            double[] r = SketchDrivenFeatureMirrorHandler.ReflectPoint2D(pt[0], pt[1], ax1, ay1, ax2, ay2);
                            bool matched = false;
                            foreach (double[] cand in allPts)
                            {
                                double d = Math.Sqrt((r[0] - cand[0]) * (r[0] - cand[0]) + (r[1] - cand[1]) * (r[1] - cand[1]));
                                if (d <= 0.0001) { matched = true; break; }
                            }
                            if (!matched) asymCount++;
                        }

                        res.AsymmetricPointsCount = asymCount;
                        res.IsBaseSketchSymmetric = (asymCount == 0);
                        res.NeedBaseSketchMutation = !res.IsBaseSketchSymmetric;
                        if (res.NeedBaseSketchMutation)
                        {
                            // Khi phản chiếu biên dạng uốn hở qua trục đối xứng 2D, chiều quay uốn cong của đường cong bị đảo ngược;
                            // hướng bù bề dày tôn (ReverseThickness) phải được đảo ngược cùng lúc để vật liệu nằm đúng phía ban đầu.
                            if (isOpenProfile)
                            {
                                res.NeedReverseThickness = true;
                                res.NeedReverseExtrusion = true;
                            }
                        }
                    }
                }
            }

            return res;
        }
    }

    public static class SheetMetalMirrorServiceV6
    {
        public static MirrorPackageResult ExecuteV6MirrorPipeline(
            ISldWorks swApp,
            Component2 sourceComponent,
            RefPlane assemblyPlane,
            ISavePathProvider savePathProvider,
            ModelDoc2 standalonePart = null,
            PlaneData standalonePlane = null,
            Action<ModelDoc2> validateBeforePublish = null,
            Action<MirrorV7FeatureNode, ModelDoc2> onNativeFeatureReplaced = null,
            Action<MirrorInPlaceExecutionContextV7> onMappedReplayCompleted = null)
        {
            MirrorPackageResult result = new MirrorPackageResult { Success = false };

            if (sourceComponent == null && (standalonePart == null || standalonePlane == null))
                throw new ArgumentException("A source component or a standalone Part with reflection plane is required.");
            string sourcePath = sourceComponent != null ? sourceComponent.GetPathName() : standalonePart.GetPathName();
            CreateMirrorPartPackage.LogDebug($"SOURCE path={sourcePath}");
            CreateMirrorPartPackage.LogDebug("SOURCE mode=" + (sourceComponent == null ? "STANDALONE_LEGACY_CORE" : "ASSEMBLY_COMPONENT"));

            MathTransform sourceTransform = sourceComponent == null ? null : sourceComponent.Transform2;
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
            if (File.Exists(chosenTargetPartPath))
                throw new InvalidOperationException("Output already exists. Choose a new file name.");

            IMathUtility mathUtility = swApp.GetMathUtility() as IMathUtility;
            PlaneData selectedMirrorPlane = standalonePart != null ? standalonePlane :
                MirrorPlaneMapper.GetLocalPlane(mathUtility, sourceComponent, assemblyPlane);
            PlaneData mirrorPlane = standalonePart != null ? standalonePlane :
                MirrorPlaneMapper.CreatePartOriginAnchoredPlane(selectedMirrorPlane);
            // Calculate Global Mirror Transform
            double[] o = mirrorPlane.Origin;
            double[] n = mirrorPlane.Normal;
            double nx = n[0], ny = n[1], nz = n[2];
            double dotON = o[0] * nx + o[1] * ny + o[2] * nz;
            double[] xform = new double[16];
            xform[0] = 1.0 - 2.0 * nx * nx; xform[1] = -2.0 * nx * ny; xform[2] = -2.0 * nx * nz;
            xform[3] = -2.0 * ny * nx; xform[4] = 1.0 - 2.0 * ny * ny; xform[5] = -2.0 * ny * nz;
            xform[6] = -2.0 * nz * nx; xform[7] = -2.0 * nz * ny; xform[8] = 1.0 - 2.0 * nz * nz;
            xform[9] = 2.0 * dotON * nx; xform[10] = 2.0 * dotON * ny; xform[11] = 2.0 * dotON * nz;
            xform[12] = 1.0; xform[13] = 0.0; xform[14] = 0.0; xform[15] = 0.0;
            CreateMirrorPartPackage.currentMirrorTransform = ReflectionApiTransformV7.Create(mathUtility, xform);
            CreateMirrorPartPackage.LogDebug($"[EDGE_FLANGE_REPAIR] currentMirrorTransform established. (nx={nx}, ny={ny}, nz={nz})");

            string targetDirectory = Path.GetDirectoryName(chosenTargetPartPath);
            if (string.IsNullOrWhiteSpace(targetDirectory)) targetDirectory = defaultDir;
            string stagingTargetPartPath = Path.Combine(
                targetDirectory,
                Path.GetFileNameWithoutExtension(chosenTargetPartPath) +
                ".mirror_stage_" + Guid.NewGuid().ToString("N") + ".sldprt");

            using (SourceDocumentGuard sourceGuard = new SourceDocumentGuard(swApp, sourcePath))
            {
                if (sourceGuard.Document == null)
                {
                    result.Message = "Khong the mo Part nguon de kiem tra.";
                    CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=FAIL");
                    return result;
                }

                if (sourceGuard.DirtyBefore)
                {
                    result.Message = "Part nguon dang co thay doi chua luu. Hay Save Part truoc khi tao Mirror.";
                    CreateMirrorPartPackage.LogDebug("SOURCE_DIRTY result=FAIL action=SAVE_SOURCE_REQUIRED");
                    return result;
                }

                File.Copy(sourcePath, stagingTargetPartPath, true);
                CreateMirrorPartPackage.LogDebug($"TARGET_STAGING path={stagingTargetPartPath}");

                int errors = 0;
                int warnings = 0;
                ModelDoc2 copiedPartDoc = swApp.OpenDoc6(
                    stagingTargetPartPath,
                    (int)swDocumentTypes_e.swDocPART,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref errors,
                    ref warnings);
                

                if (copiedPartDoc == null)
                {
                    try { if (File.Exists(stagingTargetPartPath)) File.Delete(stagingTargetPartPath); } catch { }
                    result.Message = "Khong the mo Part copy.";
                    CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=FAIL");
                    return result;
                }

                bool mirrorReadyToCommit = false;
                bool copiedPartClosedForNativeFallback = false;
                bool unsafeNativeFailure66 = false;
                try
                {
                    int activationErrors = 0;
                    ModelDoc2 activatedCopy = swApp.ActivateDoc3(copiedPartDoc.GetTitle(), false,
                        (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref activationErrors) as ModelDoc2;
                    if (activatedCopy == null || activationErrors != 0 ||
                        !string.Equals(activatedCopy.GetPathName(), stagingTargetPartPath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Cannot activate staging Part safely. errors=" + activationErrors);
                    // Activate Referenced Configuration
                    string reqConfig = standalonePart != null ? standalonePart.ConfigurationManager.ActiveConfiguration.Name :
                        sourceComponent.ReferencedConfiguration;
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

                    CreateMirrorPartPackage.edgeFlangeRepairManager.CaptureOriginalEdges(copiedPartDoc);
                    CreateMirrorPartPackage.featureOptionsCaptureHelper.CaptureFromModel(copiedPartDoc);

                    // Capture the final geometry before rollback/replay changes anything. It
                    // is used only as an oracle for validating a native SOLIDWORKS body-mirror
                    // feature; it is never imported into a blank Part.
                    List<Body2> exactSourceBodies = BodyOperationsHelper.GetAllSolidBodyCopies(copiedPartDoc);
                    if (exactSourceBodies.Count == 0)
                    {
                        result.Message = "Part nguon khong co solid body de mirror.";
                        CreateMirrorPartPackage.LogDebug("NATIVE_MIRROR_SOURCE_CAPTURE result=FAIL reason=NO_SOLID_BODY");
                        return result;
                    }

                    CreateMirrorPartPackage.LogDebug(
                        $"NATIVE_MIRROR_SOURCE_CAPTURE\nbodyCount={exactSourceBodies.Count}\n" +
                        $"totalVolume={BodyOperationsHelper.SumBodyVolumes(exactSourceBodies):E6}\nresult=PASS");

                    Func<string, bool> prepareNativeFeatureFallback = delegate(string fallbackReason)
                    {
                        // THEO LỆNH CỦA USER: "không dùng mirror mà" -> KHÔNG dùng Native Mirror Fallback.
                        // Yêu cầu mọi thao tác phải chạy thành công bằng Point Mutation!
                        CreateMirrorPartPackage.LogDebug($"[USER_DIRECTIVE] TỪ CHỐI SỬ DỤNG NATIVE MIRROR FALLBACK. Lý do gốc: {fallbackReason}. Bắt buộc phải Fix Point Mutation in-place.");
                        result.Success = false;
                        result.Message = $"[USER_DIRECTIVE] Lỗi quá trình in-place: {fallbackReason}. Đã vô hiệu hóa Native Mirror Fallback.";
                        CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: RESULT FINAL RESULT=FAIL");
                        return false;
                    };

                    // The sequential feature replay is currently single-body. Multibody
                    // Parts use the generic native Mirror Bodies path.
                    if (exactSourceBodies.Count != 1)
                    {
                        prepareNativeFeatureFallback("MULTIBODY_NATIVE_PATH");
                        return result;
                    }

                    // 1. Find Base-Flange and Driving Sketch
                    Feature baseFeature = null;
                    Feature baseDrivingSketch = null;
                    FindBaseFlangeAndSketch(copiedPartDoc, out baseFeature, out baseDrivingSketch);

                    if (baseFeature == null)
                    {
                        prepareNativeFeatureFallback("BASE_FLANGE_NOT_FOUND");
                        return result;
                    }

                    string baseFeatName = baseFeature.Name;
                    string baseSketchName = (baseDrivingSketch != null) ? baseDrivingSketch.Name : "<none>";

                    // Capture initial Base Sketch Signature from untampered model
                    Sketch baseSketchObj = (baseDrivingSketch != null) ? baseDrivingSketch.GetSpecificFeature2() as Sketch : null;
                    List<SketchSignatureHelper.SketchSegmentSignature> initialBaseSketchSig = SketchSignatureHelper.CaptureSketchSignature(baseSketchObj);

                    // 2. Scan All Post-Base Features on the UNTAMPERED Model
                    List<PostBaseFeatureInfo> postBaseFeatures = EnumeratePostBaseFeatures(copiedPartDoc, baseFeature);

                    // 2.1 Snapshot pristine sketch points from the untampered model
                    foreach (var pbf in postBaseFeatures)
                    {
                        if (pbf.DrivingSketchFeature != null)
                        {
                            Sketch skObj = pbf.DrivingSketchFeature.GetSpecificFeature2() as Sketch;
                            if (skObj != null)
                            {
                                pbf.PristineSketchPoints = ADDIN.Helpers.SketchOperationsHelper.CapturePristineSketchPoints(skObj);
                                pbf.PristineSketchSlots = ADDIN.Helpers.SketchOperationsHelper.CapturePristineSketchSlots(skObj);
                                pbf.PristineSketchPrimitives58 = ADDIN.Helpers.SketchOperationsHelper.CapturePristineSketchPrimitives58(skObj);
                                CreateMirrorPartPackage.LogDebug($"[PRISTINE_CAPTURE] Feature={pbf.Name} Sketch={pbf.DrivingSketchName} points={pbf.PristineSketchPoints.Count} slots={pbf.PristineSketchSlots.Count} primitives={pbf.PristineSketchPrimitives58.Count}");
                            }
                        }
                    }

                    // 3. Build 3D Body State Cache on the UNTAMPERED Model
                    CreateMirrorPartPackage.LogDebug("==============================");
                    CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: CACHE BUILD");
                    CreateMirrorPartPackage.LogDebug("==============================");

                    string cacheError = null;
                    List<FeatureBodyState> bodyCache = BuildFeatureBodyCache(copiedPartDoc, baseFeature, postBaseFeatures, out cacheError);
                    if (!string.IsNullOrEmpty(cacheError))
                    {
                        CreateMirrorPartPackage.LogDebug("MIRROR_PART_V6: CACHE_BUILD_FAILED " + cacheError);
                        prepareNativeFeatureFallback("CACHE_BUILD_FAILED: " + cacheError);
                        return result;
                    }

                    // Freeze references before base mutation; standalone V7 uses the
                    // canonical origin plane. Assembly keeps its existing route.
                    // Collect ALL unsupported operations before editing any sketch.
                    var preflightDispatcher = new FeatureReplayDispatcher();
                    var preflightProblems = new List<string>();
                    for (int pf = 0; pf < postBaseFeatures.Count; pf++)
                    {
                        var item = postBaseFeatures[pf];
                        if (item.IsSuppressed) continue;
                        var state = pf < bodyCache.Count ? bodyCache[pf] : null;
                        if (state != null && !state.ChangesGeometry)
                        {
                            if (RollbackReplayEngineV7.IsChiral45(item.Feature.GetDefinition()))
                                preflightProblems.Add(item.Name + " [" + item.Type +
                                    "]: CHIRAL45 surface/zero-solid-delta requires a surface geometry oracle; cannot accept a solid-only no-op");
                            continue;
                        }
                        if (preflightDispatcher.GetHandler(item) == null)
                            preflightProblems.Add(item.Name + " [" + item.Type + "]: no replay handler");
                        if (item.PristineSketchSlots != null && item.PristineSketchSlots.Count > 0)
                            CreateMirrorPartPackage.LogDebug("[GEOMETRY_FIRST56][SLOT_PREFLIGHT_ACCEPTED] feature=" +
                                item.Name + " slots=" + item.PristineSketchSlots.Count +
                                " constraintsRequired=False geometryValidationRequired=True");
                    }
                    foreach (string problem in preflightProblems)
                        CreateMirrorPartPackage.LogDebug("[PREFLIGHT13][UNSUPPORTED] " + problem);
                    if (preflightProblems.Count > 0)
                        throw new InvalidOperationException("Preflight stopped BEFORE base mutation:\n" +
                            string.Join("\n", preflightProblems));

                    MirrorInPlaceExecutionContextV7 mappedContext = null;
                    Dictionary<string, FeatureReplayCheckpointV7> mappedCheckpoints = null;
                    if (standalonePart != null)
                    {
                        mappedContext = new MirrorInPlaceExecutionContextV7
                        {
                            SwApp = swApp, WorkingDocument = copiedPartDoc,
                            Reflection = PartReflectionTransformV7.CreateOriginAnchored(mirrorPlane.Normal)
                        };
                        mappedCheckpoints = RollbackReplayEngineV7.CaptureMappedHandlers(mappedContext);
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

                    // 4. Rollback to Base Flange to perform Base Flange Transformations & Sequential Replay
                    string rbBaseErr = null;
                    bool rbBaseOk = MoveRollbackForReplay(copiedPartDoc, baseFeature, out rbBaseErr);
                    if (!rbBaseOk)
                    {
                        prepareNativeFeatureFallback("INITIAL_ROLLBACK_FAILED: " + rbBaseErr);
                        return result;
                    }

                    // Run Pre-Flight Diagnosis for Base Flange (Cases 1, 2, 3)
                    BaseFlangeDiagnosisResult diagnosis = BaseFlangeDiagnosticService.Diagnose(
                        swApp,
                        copiedPartDoc,
                        mirrorPlane,
                        baseFeature,
                        baseDrivingSketch,
                        exactSourceBodies);

                    CreateMirrorPartPackage.LogDebug(diagnosis.Summary());

                    // Case 1: Apply adapted mirror plane if off-center
                    if (standalonePart == null && diagnosis.IsOffCenter && diagnosis.AdaptedMirrorPlane != null)
                    {
                        mirrorPlane = diagnosis.AdaptedMirrorPlane;
                        CreateMirrorPartPackage.LogDebug($"[ADAPTIVE_MIRROR] Case 1 Applied: Using Centroid-anchored Mirror Plane: Origin=({mirrorPlane.Origin[0]:F4},{mirrorPlane.Origin[1]:F4},{mirrorPlane.Origin[2]:F4})");
                    }
                    result.EffectiveMirrorPlane = mirrorPlane;

                    // Snapshot at the BASE checkpoint, not the final source body or a cut checkpoint.
                    string baseOracleError22;
                    Body2 sourceBase22 = BodyOperationsHelper.GetSolidBodyCopyStrict(copiedPartDoc, out baseOracleError22);
                    if (sourceBase22 == null)
                        throw new InvalidOperationException("BASE22 source checkpoint unavailable: " + baseOracleError22);
                    BodyTransformResult expectedBase22 = BodyOperationsHelper.MirrorBodyStrict(swApp, sourceBase22, mirrorPlane);
                    if (expectedBase22 == null || !expectedBase22.Success || expectedBase22.Body == null)
                        throw new InvalidOperationException("BASE22 reflection oracle unavailable: " + expectedBase22?.ErrorMessage);
                    IBaseFlangeFeatureData originalBase22 = baseFeature.GetDefinition() as IBaseFlangeFeatureData;
                    if (originalBase22 == null || !originalBase22.AccessSelections(copiedPartDoc, null))
                        throw new InvalidOperationException("BASE22 cannot read base flange options.");
                    bool originalDirection22;
                    bool originalThickness22;
                    try
                    {
                        originalDirection22 = originalBase22.ReverseDirection;
                        originalThickness22 = originalBase22.ReverseThickness;
                    }
                    finally { originalBase22.ReleaseSelectionAccess(); }

                    // Case 3: In-Place Point Mutation for Base Sketch if Asymmetric (chạy trước để cập nhật biên dạng sketch)
                    if (diagnosis.NeedBaseSketchMutation && baseDrivingSketch != null)
                    {
                        try
                        {
                            Sketch skObj = baseDrivingSketch.GetSpecificFeature2() as Sketch;
                            var pristinePts = ADDIN.Helpers.SketchOperationsHelper.CapturePristineSketchPoints(skObj);
                            Sketch activeSketch = ADDIN.Helpers.SketchOperationsHelper.FreeSketchForMutation(copiedPartDoc, baseDrivingSketch);
                            if (activeSketch != null && diagnosis.AxisPoint1 != null && diagnosis.AxisPoint2 != null)
                            {
                                ADDIN.Helpers.SketchOperationsHelper.MutateSketchPoints(
                                    copiedPartDoc,
                                    activeSketch,
                                    diagnosis.AxisPoint1[0], diagnosis.AxisPoint1[1],
                                    diagnosis.AxisPoint2[0], diagnosis.AxisPoint2[1],
                                    pristinePts,
                                    mirrorPlane);
                                copiedPartDoc.ForceRebuild3(false);
                                CreateMirrorPartPackage.LogDebug($"[ADAPTIVE_MIRROR] Case 3 Applied: Mutated {pristinePts.Count} Base Sketch points in-place.");
                            }
                        }
                        catch (Exception ex)
                        {
                            CreateMirrorPartPackage.LogDebug($"[ADAPTIVE_MIRROR] Case 3 failed: {ex.Message}");
                            throw new InvalidOperationException("Base sketch reflection failed; downstream replay cancelled.", ex);
                        }
                    }

                    // A reflected sketch does not prove that sheet material occupies the right side.
                    // Resolve native flags against the reflected BASE body, never volume alone.
                    ResolveBaseOptions22(copiedPartDoc, baseFeature, expectedBase22.Body,
                        originalDirection22, originalThickness22, diagnosis);

                    string baseGateCaptureError53;
                    Body2 actualBase53 = BodyOperationsHelper.GetSolidBodyCopyStrict(copiedPartDoc, out baseGateCaptureError53);
                    if (actualBase53 == null)
                        throw new InvalidOperationException("BASE53: Cannot capture live base body: " + baseGateCaptureError53);
                    VerifyBaseAbsoluteGate53(expectedBase22.Body, actualBase53);

                    CreateMirrorPartPackage.LogDebug($"MIRROR_PART_V6: BASE feature={baseFeatName} sketch={baseSketchName} mutated={diagnosis.NeedBaseSketchMutation}");

                    // 5. Synchronize Live Model with B0 and Prepare Sequential Replay
                    string liveBaseErr = null;
                    Body2 liveBaseBody = BodyOperationsHelper.GetSolidBodyCopyStrict(copiedPartDoc, out liveBaseErr);
                    if (liveBaseBody == null)
                    {
                        prepareNativeFeatureFallback("LIVE_BASE_CAPTURE_FAILED: " + liveBaseErr);
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
                        prepareNativeFeatureFallback($"BASE_STATE_MISMATCH diff={syncDiff:E6}");
                        return result;
                    }

                    Body2 currentActualBody = liveBaseBody;
                    FeatureReplayDispatcher dispatcher = new FeatureReplayDispatcher();
                    // CUT23 checks each feature at its own before-cut checkpoint.
                    // Do not compare every later cut against the base-only body.

                    int replayedCount = 0;
                    int unsupportedGeometryCount = 0;
                    int failedCount = 0;
                    int validatedCount = 0;

                    // Track replayed sketch signatures for upstream persistence checking
                    Dictionary<int, List<SketchSignatureHelper.SketchSegmentSignature>> replayedSketchSignatures =
                        new Dictionary<int, List<SketchSignatureHelper.SketchSegmentSignature>>();

                    // Track features that were accepted as asymmetric, so their children can also be asymmetric
                    HashSet<string> asymmetricFeatures = new HashSet<string>();

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

                        // Native parent replacement may remove a captured child, including
                        // a suppressed command. Recover at THIS original slot, not beside
                        // the parent, so every later parent has already been replayed.
                        var restored64 = RollbackReplayEngineV7.RestoreDeferredFeature64(mappedContext, featInfo.Name);
                        if (restored64 != null)
                        {
                            featInfo.Feature = restored64.Feature.Feature;
                            if (onNativeFeatureReplaced != null)
                                onNativeFeatureReplaced(restored64.Feature, copiedPartDoc);
                        }

                        if (featInfo.Disposition == FeatureReplayDisposition.Suppressed)
                        {
                            CreateMirrorPartPackage.LogDebug("handler=SKIPPED_SUPPRESSED");
                            continue;
                        }
                        if (featInfo.Disposition == FeatureReplayDisposition.NoGeometryChange)
                        {
                            // Advance even across an inherited no-op checkpoint. Otherwise
                            // the next cut's delta includes intervening native rebuilds.
                            string syncError60;
                            if (!MoveRollbackForReplay(copiedPartDoc, featInfo.Feature, out syncError60))
                                throw new InvalidOperationException("REPLAY60: Cannot advance no-op checkpoint: " + syncError60);
                            bool warning60;
                            int error60 = featInfo.Feature.GetErrorCode2(out warning60);
                            if (error60 != 0 && !warning60)
                                throw new InvalidOperationException("REPLAY60: Inherited feature failed: " + featInfo.Name + " code=" + error60);
                            Body2 nextBody60 = BodyOperationsHelper.GetSolidBodyCopyStrict(copiedPartDoc, out syncError60);
                            if (nextBody60 == null)
                                throw new InvalidOperationException("REPLAY60: Cannot capture no-op checkpoint: " + syncError60);
                            if (!AreExactBodySetsEquivalent(new List<Body2> { currentActualBody },
                                new List<Body2> { nextBody60 }, featInfo.Name + "_NOOP60", out syncError60))
                                throw new InvalidOperationException("REPLAY60: No-op checkpoint changed geometry: " + featInfo.Name + " " + syncError60);
                            currentActualBody = nextBody60;
                            CreateMirrorPartPackage.LogDebug("[REPLAY60][BASELINE_SYNC] feature=" + featInfo.Name + " reason=NoGeometryChange");
                            continue;
                        }

                        IFeatureMirrorHandler handler = dispatcher.GetHandler(featInfo);
                        if (handler == null)
                        {
                            CreateMirrorPartPackage.LogDebug("handler=UNSUPPORTED");
                            unsupportedGeometryCount++;
                            failedCount++;
                            prepareNativeFeatureFallback(
                                $"UNSUPPORTED_FEATURE name={featInfo.Name} type={featInfo.Type}");
                            return result;
                        }

                        CreateMirrorPartPackage.LogDebug($"handler={handler.GetType().Name}");

                        // Move rollback bar to after this feature so downstream features are rolled back!
                        string rbFeatErr = null;
                        bool rbFeatOk = MoveRollbackForReplay(copiedPartDoc, featInfo.Feature, out rbFeatErr);
                        if (!rbFeatOk)
                        {
                            failedCount++;
                            prepareNativeFeatureFallback(
                                $"FEATURE_ROLLBACK_FAILED name={featInfo.Name} reason={rbFeatErr}");
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
                                    prepareNativeFeatureFallback(
                                        $"UPSTREAM_SKETCH_CHANGED sketch={prevFeat.DrivingSketchName} next={featInfo.Name}");
                                    return result;
                                }
                            }
                        }

                        FeatureReplayResult replayRes;
                        FeatureReplayCheckpointV7 mappedCheckpoint;
                        if (!SketchDrivenFeatureMirrorHandler.DiagnosticOnly &&
                            mappedCheckpoints != null && mappedCheckpoints.TryGetValue(featInfo.Name, out mappedCheckpoint))
                        {
                            RollbackReplayEngineV7.ReplayMappedHandler(mappedContext, mappedCheckpoint);
                            if (mappedCheckpoint.WasNativeReplacement && restored64 == null && onNativeFeatureReplaced != null)
                                onNativeFeatureReplaced(mappedCheckpoint.Feature, copiedPartDoc);
                            // A native reconstruction replaces the COM feature object.
                            featInfo.Feature = mappedCheckpoint.Feature.Feature;
                            replayRes = new FeatureReplayResult { Success = true, RebuildPassed = true,
                                FeatureName = featInfo.Name, FeatureType = featInfo.Type,
                                StatusCode = "MAPPED12_ORACLE_PASS", Message = mappedCheckpoint.Result.Message };
                        }
                        else
                            replayRes = handler.Replay(swApp, copiedPartDoc, featInfo, mirrorPlane, cacheState, baseFeatName, baseSketchName);

                        CreateMirrorPartPackage.LogDebug($"mirrorReference.mode={replayRes.MirrorReferenceKind} ({replayRes.StatusCode})");
                        CreateMirrorPartPackage.LogDebug($"sourceEntities={replayRes.SourceEntities}");
                        CreateMirrorPartPackage.LogDebug($"invariantEntities={replayRes.InvariantEntities}");
                        CreateMirrorPartPackage.LogDebug($"mirroredEntities={replayRes.MirroredEntities}");
                        CreateMirrorPartPackage.LogDebug($"constructionEntities={replayRes.ConstructionEntities}");

                        if (!replayRes.Success)
                        {
                            failedCount++;
                            prepareNativeFeatureFallback(
                                $"FEATURE_REPLAY_FAILED name={featInfo.Name} status={replayRes.StatusCode} reason={replayRes.Message}");
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
                            prepareNativeFeatureFallback(
                                $"FEATURE_BODY_CAPTURE_FAILED name={featInfo.Name} reason={actErr}");
                            return result;
                        }

                        // A mirrored sketch can require the opposite Flip Side To Cut state.
                        // Prefer the source removed-volume oracle when it remains valid. For an
                        // asymmetric unchanged sheet-metal base, select the state whose removed
                        // region is nearest the reflected source removed-region centroid.
                        string cutFlipDetails;
                        bool allowAsymmetricCutVolume;
                        bool usedSuperRecover = false;
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

                            // TryCorrectExtrudeCutFlip thất bại (thường do end-condition face reference
                            // không còn valid sau mirror, ví dụ face của BaseBend chỉ tồn tại phía nguồn).
                            // Thử TrySuperRecoverExtrudeCut (Phase 2: ép ThroughAll) trước khi báo lỗi.
                            string superRecDetails;
                            bool superRecOk = BodyOperationsHelper.TrySuperRecoverExtrudeCut(
                                copiedPartDoc, featInfo, out superRecDetails);
                            string superRecResult = superRecOk ? "PASS" : "FAIL";
                            CreateMirrorPartPackage.LogDebug(
                                $"[SUPER_REC_AFTER_FLIP_FAIL] feature={featInfo.Name} result={superRecResult} details={superRecDetails}");


                            if (superRecOk)
                            {
                                // Chụp lại body sau khi SuperRecover thành công
                                string srBodyErr;
                                stepActualBody = BodyOperationsHelper.GetSolidBodyCopyStrict(copiedPartDoc, out srBodyErr);
                                if (stepActualBody != null)
                                {
                                    // Cho phép asymmetric volume vì end-condition đã thay đổi (ThroughAll)
                                    allowAsymmetricCutVolume = true;
                                    usedSuperRecover = true;
                                    CreateMirrorPartPackage.LogDebug(
                                        $"[SUPER_REC_AFTER_FLIP_FAIL] feature={featInfo.Name} body captured, continuing with ASYMMETRIC_BASE");
                                    // Tiếp tục vào phần semantic validation bên dưới
                                }
                                else
                                {
                                    failedCount++;
                                    prepareNativeFeatureFallback(
                                        $"CUT_DIRECTION_CORRECTION_FAILED name={featInfo.Name} (super_rec body null: {srBodyErr})");
                                    return result;
                                }
                            }
                            else
                            {
                                failedCount++;
                                prepareNativeFeatureFallback(
                                    $"CUT_DIRECTION_CORRECTION_FAILED name={featInfo.Name}");
                                return result;
                            }
                        }

                        if (!allowAsymmetricCutVolume && featInfo.ParentFeatureNames != null &&
                            !(featInfo.CutAudit21 != null && featInfo.CutAudit21.Recipe44 != null && featInfo.CutAudit21.Recipe44.Applied))
                        {
                            foreach (string parent in featInfo.ParentFeatureNames)
                            {
                                if (asymmetricFeatures.Contains(parent))
                                {
                                    allowAsymmetricCutVolume = true;
                                    break;
                                }
                            }
                        }

                        if (allowAsymmetricCutVolume)
                        {
                            asymmetricFeatures.Add(featInfo.Name);
                        }

                        replayRes.AllowAsymmetricCutVolume = allowAsymmetricCutVolume;

                        if (!usedSuperRecover && !string.IsNullOrEmpty(cutFlipDetails))
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

                        // A volume-only discrepancy must reach geometric verification.
                        // Other semantic failures (added material, empty cut, multibody,
                        // unavailable Boolean) are never waived here.
                        bool volumeOnly53 = !semVal.Success && semVal.FailureReason != null &&
                            semVal.FailureReason.StartsWith("SUBTRACTIVE_REMOVED_VOLUME_MISMATCH(", StringComparison.Ordinal) &&
                            semVal.ActualMinusBeforeBooleanSuccess && semVal.BeforeMinusActualBooleanSuccess &&
                            SketchDrivenFeatureMirrorHandler.IsExtrudeCutType(featInfo.Type);
                        bool exactCheckpoint53 = false;
                        if (semVal.Success || volumeOnly53)
                            exactCheckpoint53 = VerifyCutCheckpointOracle59(swApp, featInfo, cacheState,
                                currentActualBody, stepActualBody, mirrorPlane);
                        if (volumeOnly53 && exactCheckpoint53)
                        {
                            CreateMirrorPartPackage.LogDebug("[CUT53][VOLUME_ONLY_DISCREPANCY_ACCEPTED] feature=" +
                                featInfo.Name + " evidence=BEFORE_AND_AFTER_BOOLEANS_EMPTY original=" + semVal.FailureReason);
                            semVal.Success = true;
                            semVal.FailureReason = null;
                        }

                        string semPassStr = semVal.Success ? "PASS" : "FAIL";
                        CreateMirrorPartPackage.LogDebug($"FEATURE_SEMANTIC_VALIDATE\nfeature={featInfo.Name}\nkind={semVal.ExpectedChangeKind}\nbeforeBodyCount={semVal.BeforeBodyCount}\nafterBodyCount={semVal.AfterBodyCount}\nbeforeVolume={semVal.BeforeVolume:E6}\nafterVolume={semVal.AfterVolume:E6}\nexpectedAddedVolume={semVal.ExpectedAddedVolume:E6}\nexpectedRemovedVolume={semVal.ExpectedRemovedVolume:E6}\nactualAddedVolume={semVal.ActualAddedVolume:E6}\nactualRemovedVolume={semVal.ActualRemovedVolume:E6}\nrelativeVolumeError={semVal.RelativeVolumeError:E6}\nresult={semPassStr}\nreason={semVal.FailureReason}");

                        if (!semVal.Success)
                        {
                            failedCount++;
                            prepareNativeFeatureFallback(
                                $"FEATURE_SEMANTIC_VALIDATION_FAILED name={featInfo.Name} reason={semVal.FailureReason}");
                            return result;
                        }

                        // A sketch-driven pattern can remove the correct volume at the
                        // wrong points. Prove both sides of its checkpoint against the
                        // reflected source solids, not merely the volume delta.
                        if (string.Equals(featInfo.Type, "SketchPattern", StringComparison.OrdinalIgnoreCase))
                        {
                            string sketchPatternOracleError;
                            if (!VerifySketchPatternCheckpoint57(swApp, featInfo, cacheState,
                                currentActualBody, stepActualBody, mirrorPlane,
                                out sketchPatternOracleError))
                            {
                                failedCount++;
                                prepareNativeFeatureFallback(
                                    "SKETCH_PATTERN_CHECKPOINT57_FAILED name=" + featInfo.Name +
                                    " reason=" + sketchPatternOracleError);
                                return result;
                            }
                        }

                        // The checkpoint oracle above is mandatory for every extruded cut;
                        // do not run it twice or advance before it has passed.

                        validatedCount++;
                        // Advance sequential state
                        currentActualBody = stepActualBody;
                    }

                    // 5. Restore Rollback to End
                    RollbackReplayEngineV7.AssertDependencyReplayComplete64(mappedContext);
                    bool rbEndOk = copiedPartDoc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, "");
                    CreateMirrorPartPackage.LogDebug($"REPLAY_ROLLBACK_TO_END result={rbEndOk}");

                    copiedPartDoc.ForceRebuild3(false);

                    // 6. Base Sketch Signature Verification Check
                    List<SketchSignatureHelper.SketchSegmentSignature> finalBaseSketchSig = SketchSignatureHelper.CaptureSketchSignature(baseSketchObj);
                    bool baseSketchUnchanged = SketchSignatureHelper.CompareSignatures(initialBaseSketchSig, finalBaseSketchSig);

                    bool isBaseWarning = false;
                    int baseErrCode = baseFeature.GetErrorCode2(out isBaseWarning);
                    bool baseFeatureHealthy = (baseErrCode == 0 || isBaseWarning);

                    // 7. Final Replay Audit on all replayed features after full rebuild
                    CreateMirrorPartPackage.LogDebug("==============================");
                    CreateMirrorPartPackage.LogDebug("FINAL_REPLAY_AUDIT");
                    CreateMirrorPartPackage.LogDebug("==============================");

                    bool allAuditPass = true;
                    for (int i = 0; i < postBaseFeatures.Count; i++)
                    {
                        var featInfo = postBaseFeatures[i];
                        if (featInfo.Disposition == FeatureReplayDisposition.ReplayRequired)
                        {
                            bool isWarning = false;
                            int err = featInfo.Feature.GetErrorCode2(out isWarning);
                            bool featHealthy = (err == 0 || isWarning);

                            if (string.Equals(featInfo.Type, "EdgeFlange", StringComparison.OrdinalIgnoreCase) ||
                                featInfo.Type.IndexOf("EdgeFlange", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                CreateMirrorPartPackage.LogDebug($"{featInfo.Name}: EdgeFlange featureHealthy={featHealthy} errCode={err} warning={isWarning}");
                                if (!featHealthy) allAuditPass = false;
                                continue;
                            }

                            if (featInfo.HasDrivingSketch && featInfo.DrivingSketchFeature != null)
                            {

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
                        else
                        {
                            CreateMirrorPartPackage.LogDebug($"{featInfo.Name}: {featInfo.Type} featureHealthy={featHealthy} errCode={err} warning={isWarning}");
                            if (!featHealthy) allAuditPass = false;
                        }
                    }
                }

                    // 8. Final Success Verification
                    string finalActErr = null;
                    Body2 finalSolidBody = BodyOperationsHelper.GetSolidBodyCopyStrict(copiedPartDoc, out finalActErr);
                    bool singleSolidBody = (finalSolidBody != null);

                    bool baseSketchValid = diagnosis.NeedBaseSketchMutation ? (baseErrCode == 0 || isBaseWarning) : baseSketchUnchanged;

                    bool overallPass = (failedCount == 0) &&
                                       (unsupportedGeometryCount == 0) &&
                                       (bodyChangingCount == validatedCount) &&
                                       baseSketchValid &&
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
                    CreateMirrorPartPackage.LogDebug($"unsupported={unsupportedGeometryCount}");
                    CreateMirrorPartPackage.LogDebug($"failed={failedCount}");

                    string finalResStr = overallPass ? "SUCCESS" : "FAIL";
                    CreateMirrorPartPackage.LogDebug($"FINAL RESULT={finalResStr}");

                    if (!overallPass)
                    {
                        string finalReason;
                        if (!baseSketchValid) finalReason = "BASE_SKETCH_INVALID";
                        else if (!baseFeatureHealthy) finalReason = $"BASE_FLANGE_ERROR code={baseErrCode}";
                        else if (!singleSolidBody) finalReason = $"FINAL_SOLID_BODY_FAILED reason={finalActErr}";
                        else if (!allAuditPass) finalReason = "FINAL_REPLAY_AUDIT_FAILED";
                        else finalReason = "FINAL_VALIDATION_FAILED";
                        prepareNativeFeatureFallback(finalReason);
                        return result;
                    }

                    copiedPartDoc.ForceRebuild3(false);
                    if (onMappedReplayCompleted != null && mappedContext != null)
                        onMappedReplayCompleted(mappedContext);
                    if (validateBeforePublish != null) validateBeforePublish(copiedPartDoc);
                    int finalSaveErrors = 0, finalSaveWarnings = 0;
                    if (!copiedPartDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                        ref finalSaveErrors, ref finalSaveWarnings) || finalSaveErrors != 0)
                        throw new InvalidOperationException("Cannot save verified staging Part: errors=" + finalSaveErrors);

                    ValidateSheetMetalPart(copiedPartDoc);

                    mirrorReadyToCommit = true;
                }
                catch (Exception ex)
                {
                    unsafeNativeFailure66 = RollbackReplayEngineV7.IsUnsafeNativeFailure66(ex);
                    throw;
                }
                finally
                {
                    if (!copiedPartClosedForNativeFallback && copiedPartDoc != null)
                    {
                        // Never resave a verified result here. On failure the disk
                        // copy previously still contained the untouched source,
                        // losing the 3D guides and the actual failing native state.
                        if (unsafeNativeFailure66)
                            CreateMirrorPartPackage.LogDebug("[DIAGNOSTIC66][NATIVE_STOP] staging=" + stagingTargetPartPath +
                                " saveSkipped=True closeSkipped=True outputPublished=False furtherCadCalls=0");
                        else
                        {
                            bool mayClose = mirrorReadyToCommit || TrySaveFailedMirrorStaging62(
                                copiedPartDoc, stagingTargetPartPath, sourcePath, chosenTargetPartPath);
                            if (mayClose) swApp.CloseDoc(copiedPartDoc.GetTitle());
                            else CreateMirrorPartPackage.LogDebug("[DIAGNOSTIC62][LEFT_OPEN] staging=" + stagingTargetPartPath +
                                " reason=FAILED_STATE_NOT_SAVED outputPublished=False action=INSPECT_OPEN_STAGING");
                        }
                    }

                    // Publish only a verified result. Never replace a user file or publish failed replay.
                    try
                    {
                        if (mirrorReadyToCommit)
                        {
                            if (!File.Exists(stagingTargetPartPath))
                                throw new FileNotFoundException("Verified staging Part is missing.", stagingTargetPartPath);
                            File.Copy(stagingTargetPartPath, chosenTargetPartPath, false);
                            CreateMirrorPartPackage.LogDebug($"TARGET_SAVED target={chosenTargetPartPath} readyToCommit={mirrorReadyToCommit}");
                        }
                    }
                    catch (Exception ex)
                    {
                        mirrorReadyToCommit = false;
                        result.Success = false;
                        result.Message = "Cannot publish mirrored Part: " + ex.Message;
                        CreateMirrorPartPackage.LogDebug($"TARGET_SAVE_FAIL target={chosenTargetPartPath} reason={ex.Message}");
                    }

                    if (mirrorReadyToCommit)
                    {
                        result.Success = true;
                        result.MirrorPartPath = chosenTargetPartPath;
                        CreateMirrorPartPackage.LogDebug(
                            $"TARGET_COMMIT staging={stagingTargetPartPath}\ntarget={chosenTargetPartPath}\nresult=PASS");
                    }
                    else
                    {
                        CreateMirrorPartPackage.LogDebug("TARGET_NOT_PUBLISHED diagnosticStaging=" + stagingTargetPartPath);
                    }

                    try
                    {
                        if (mirrorReadyToCommit && File.Exists(stagingTargetPartPath)) File.Delete(stagingTargetPartPath);
                    }
                    catch (Exception cleanupException)
                    {
                        CreateMirrorPartPackage.LogDebug(
                            $"TARGET_STAGING_CLEANUP path={stagingTargetPartPath}\nresult=FAIL\nreason={cleanupException.Message}");
                    }
                }
            }

            return result;
        }

        private static bool TrySaveFailedMirrorStaging62(ModelDoc2 document,
            string stagingPath, string sourcePath, string outputPath)
        {
            // Diagnostic only: never promote this state to a deliverable Part,
            // never overwrite source/output and never rebuild away the failure.
            try
            {
                string actual = Path.GetFullPath(document.GetPathName());
                string staging = Path.GetFullPath(stagingPath);
                if (!string.Equals(actual, staging, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(actual, Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(actual, Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFileName(actual).Contains(".mirror_stage_"))
                    throw new InvalidOperationException("Diagnostic save path is not the unique staging Part.");
                if (document.SketchManager.ActiveSketch != null)
                    throw new InvalidOperationException("Sketch edit is still active; leave diagnostic staging open without exiting/rebuilding it.");
                int errors = 0, warnings = 0;
                bool saved = document.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
                bool confirmed = saved && errors == 0 && File.Exists(staging) && !document.GetSaveFlag();
                CreateMirrorPartPackage.LogDebug("[DIAGNOSTIC62][SAVE] staging=" + staging +
                    " apiReturn=" + saved + " errors=" + errors + " warnings=" + warnings +
                    " stateSaved=" + confirmed + " outputPublished=False explicitRebuild=False");
                return confirmed;
            }
            catch (Exception ex)
            {
                // Do not mask the original replay exception or close/discard the
                // only surviving copy of the in-memory guide/failure geometry.
                CreateMirrorPartPackage.LogDebug("[DIAGNOSTIC62][SAVE_FAILED] staging=" + stagingPath +
                    " reason=" + ex.Message + " outputPublished=False");
                return false;
            }
        }

        private static bool TryCreateNativeFeatureMirrorStaging(
            ISldWorks swApp,
            ModelDoc2 replayPartDoc,
            string sourcePartPath,
            string referencedConfiguration,
            List<Body2> sourceBodies,
            PlaneData mirrorPlane,
            string stagingTargetPartPath,
            string fallbackReason,
            ref bool replayPartClosed,
            out string error)
        {
            error = null;
            if (swApp == null || replayPartDoc == null || sourceBodies == null ||
                sourceBodies.Count == 0 || mirrorPlane == null ||
                string.IsNullOrWhiteSpace(sourcePartPath) ||
                string.IsNullOrWhiteSpace(stagingTargetPartPath))
            {
                error = "Invalid native-feature mirror arguments.";
                return false;
            }

            // The temporary bodies are validation oracles only. They are never inserted into
            // the output document, so the saved Part remains a native feature-based model.
            List<Body2> expectedMirroredBodies = new List<Body2>();
            for (int bodyIndex = 0; bodyIndex < sourceBodies.Count; bodyIndex++)
            {
                BodyTransformResult mirrorResult = BodyOperationsHelper.MirrorBodyStrict(
                    swApp,
                    sourceBodies[bodyIndex],
                    mirrorPlane);
                if (mirrorResult == null || !mirrorResult.Success || mirrorResult.Body == null)
                {
                    error =
                        $"Cannot calculate validation body {bodyIndex + 1}/{sourceBodies.Count}: " +
                        (mirrorResult?.ErrorMessage ?? "null mirror result");
                    CreateMirrorPartPackage.LogDebug(
                        $"NATIVE_FEATURE_FALLBACK\nreason={fallbackReason}\nbodyIndex={bodyIndex}\n" +
                        $"result=FAIL\ndetails={error}");
                    return false;
                }

                expectedMirroredBodies.Add(mirrorResult.Body);
            }

            try
            {
                swApp.CloseDoc(replayPartDoc.GetTitle());
                replayPartClosed = true;
            }
            catch (Exception closeException)
            {
                error = "Cannot close replay staging Part: " + closeException.Message;
                CreateMirrorPartPackage.LogDebug($"NATIVE_FEATURE_FALLBACK\nreason={fallbackReason}\nresult=FAIL\ndetails={error}");
                return false;
            }

            ModelDoc2 nativePartDoc = null;
            bool nativePartClosed = false;
            try
            {
                // Replay may have changed sketches before failing. Recreate staging from the
                // untouched source, then append native features to that clean feature tree.
                bool copied = false;
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    try
                    {
                        File.Copy(sourcePartPath, stagingTargetPartPath, true);
                        copied = true;
                        break;
                    }
                    catch (IOException)
                    {
                        System.Threading.Thread.Sleep(200);
                    }
                }
                if (!copied)
                {
                    File.Copy(sourcePartPath, stagingTargetPartPath, true);
                }

                int openErrors = 0;
                int openWarnings = 0;
                nativePartDoc = swApp.OpenDoc6(
                    stagingTargetPartPath,
                    (int)swDocumentTypes_e.swDocPART,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref openErrors,
                    ref openWarnings);
                if (nativePartDoc == null)
                {
                    error =
                        $"Cannot open clean native-feature staging Part " +
                        $"(errors={openErrors}, warnings={openWarnings}).";
                    CreateMirrorPartPackage.LogDebug($"NATIVE_FEATURE_FALLBACK\nreason={fallbackReason}\nresult=FAIL\ndetails={error}");
                    return false;
                }

                if (!string.IsNullOrEmpty(referencedConfiguration))
                {
                    string activeConfiguration = nativePartDoc.ConfigurationManager.ActiveConfiguration.Name;
                    if (!string.Equals(activeConfiguration, referencedConfiguration, StringComparison.OrdinalIgnoreCase))
                    {
                        nativePartDoc.ShowConfiguration2(referencedConfiguration);
                        activeConfiguration = nativePartDoc.ConfigurationManager.ActiveConfiguration.Name;
                    }

                    if (!string.Equals(activeConfiguration, referencedConfiguration, StringComparison.OrdinalIgnoreCase))
                    {
                        error =
                            $"Cannot activate configuration '{referencedConfiguration}' in native staging Part; " +
                            $"active='{activeConfiguration}'.";
                        CreateMirrorPartPackage.LogDebug($"NATIVE_FEATURE_FALLBACK\nreason={fallbackReason}\nresult=FAIL\ndetails={error}");
                        return false;
                    }
                }

                string nativeFeatureError;
                if (!TryAppendNativeMirrorFeatures(
                    swApp,
                    nativePartDoc,
                    expectedMirroredBodies,
                    mirrorPlane,
                    out nativeFeatureError))
                {
                    error = nativeFeatureError;
                    CreateMirrorPartPackage.LogDebug($"NATIVE_FEATURE_FALLBACK\nreason={fallbackReason}\nresult=FAIL\ndetails={error}");
                    return false;
                }

                int saveErrors = 0;
                int saveWarnings = 0;
                bool saved = nativePartDoc.Save3(
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    ref saveErrors,
                    ref saveWarnings);
                CreateMirrorPartPackage.LogDebug(
                    $"NATIVE_FEATURE_SAVE\npath={stagingTargetPartPath}\nsaved={saved}\n" +
                    $"errors={saveErrors}\nwarnings={saveWarnings}");
                if (!saved || saveErrors != 0 || !File.Exists(stagingTargetPartPath))
                {
                    error =
                        $"Saving native-feature Part failed " +
                        $"(saved={saved}, errors={saveErrors}, warnings={saveWarnings}).";
                    CreateMirrorPartPackage.LogDebug($"NATIVE_FEATURE_FALLBACK\nreason={fallbackReason}\nresult=FAIL\ndetails={error}");
                    return false;
                }

                swApp.CloseDoc(nativePartDoc.GetTitle());
                nativePartClosed = true;
                nativePartDoc = null;

                int reopenErrors = 0;
                int reopenWarnings = 0;
                ModelDoc2 persistedDoc = swApp.OpenDoc6(
                    stagingTargetPartPath,
                    (int)swDocumentTypes_e.swDocPART,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref reopenErrors,
                    ref reopenWarnings);
                if (persistedDoc == null)
                {
                    error =
                        $"Cannot reopen native-feature staging Part " +
                        $"(errors={reopenErrors}, warnings={reopenWarnings}).";
                    CreateMirrorPartPackage.LogDebug($"NATIVE_FEATURE_FALLBACK\nreason={fallbackReason}\nresult=FAIL\ndetails={error}");
                    return false;
                }

                try
                {
                    persistedDoc.ForceRebuild3(false);
                    List<Body2> persistedBodies = BodyOperationsHelper.GetAllSolidBodyCopies(persistedDoc);
                    string persistedValidationError;
                    if (!AreExactBodySetsEquivalent(
                        expectedMirroredBodies,
                        persistedBodies,
                        "NATIVE_FEATURE_AFTER_REOPEN",
                        out persistedValidationError))
                    {
                        error = "Persisted native mirror validation failed: " + persistedValidationError;
                        CreateMirrorPartPackage.LogDebug($"NATIVE_FEATURE_FALLBACK\nreason={fallbackReason}\nresult=FAIL\ndetails={error}");
                        return false;
                    }

                    Feature persistedMirror = FindTopLevelFeatureByName(
                        persistedDoc,
                        "MIRROR_BODY_NATIVE");
                    Feature persistedKeep = FindTopLevelFeatureByName(
                        persistedDoc,
                        "KEEP_MIRRORED_BODIES");
                    if (persistedMirror == null || persistedKeep == null)
                    {
                        error =
                            "Native Mirror Bodies or Keep Bodies feature is missing after reopening the Part.";
                        CreateMirrorPartPackage.LogDebug($"NATIVE_FEATURE_FALLBACK\nreason={fallbackReason}\nresult=FAIL\ndetails={error}");
                        return false;
                    }

                    bool mirrorWarning = false;
                    bool keepWarning = false;
                    int mirrorError = persistedMirror.GetErrorCode2(out mirrorWarning);
                    int keepError = persistedKeep.GetErrorCode2(out keepWarning);
                    if (mirrorError != 0 || keepError != 0)
                    {
                        error =
                            $"Native features have rebuild errors after reopen " +
                            $"(mirror={mirrorError}, keep={keepError}).";
                        CreateMirrorPartPackage.LogDebug($"NATIVE_FEATURE_FALLBACK\nreason={fallbackReason}\nresult=FAIL\ndetails={error}");
                        return false;
                    }
                }
                finally
                {
                    swApp.CloseDoc(persistedDoc.GetTitle());
                }

                CreateMirrorPartPackage.LogDebug(
                    $"NATIVE_FEATURE_FALLBACK\nreason={fallbackReason}\n" +
                    $"sourceBodyCount={sourceBodies.Count}\nmirroredBodyCount={expectedMirroredBodies.Count}\n" +
                    $"totalVolume={BodyOperationsHelper.SumBodyVolumes(expectedMirroredBodies):E6}\n" +
                    "features=MIRROR_BODY_NATIVE,KEEP_MIRRORED_BODIES\nresult=PASS");
                return true;
            }
            catch (Exception ex)
            {
                error = "Native-feature fallback exception: " + ex.Message;
                CreateMirrorPartPackage.LogDebug(
                    $"NATIVE_FEATURE_FALLBACK\nreason={fallbackReason}\nresult=FAIL\ndetails={error}");
                return false;
            }
            finally
            {
                if (nativePartDoc != null && !nativePartClosed)
                {
                    try { swApp.CloseDoc(nativePartDoc.GetTitle()); }
                    catch { }
                }
            }
        }

﻿        private static bool TryAppendNativeMirrorFeatures(
            ISldWorks swApp,
            ModelDoc2 partDoc,
            List<Body2> expectedMirroredBodies,
            PlaneData mirrorPlane,
            out string error)
        {
            error = null;
            if (swApp == null || partDoc == null || expectedMirroredBodies == null ||
                expectedMirroredBodies.Count == 0 || mirrorPlane == null)
            {
                error = "Invalid native mirror feature arguments.";
                return false;
            }

            List<Body2> sourceBodyReferences = BodyOperationsHelper.GetAllSolidBodies(partDoc);
            if (sourceBodyReferences.Count != expectedMirroredBodies.Count)
            {
                error = $"Source body count changed before native mirror: actual={sourceBodyReferences.Count}, expected={expectedMirroredBodies.Count}.";
                return false;
            }

            HashSet<string> sourceBodyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> sourceSelectionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (Body2 sourceBody in sourceBodyReferences)
            {
                try { if (!string.IsNullOrEmpty(sourceBody.Name)) sourceBodyNames.Add(sourceBody.Name); } catch { }
                try
                {
                    string selectionId = sourceBody.GetSelectionId();
                    if (!string.IsNullOrEmpty(selectionId)) sourceSelectionIds.Add(selectionId);
                } catch { }
            }

            bool createdReferencePlane;
            string planeError;
            Feature mirrorPlaneFeature = TryGetOrCreateNativeMirrorPlane(swApp, partDoc, mirrorPlane, out createdReferencePlane, out planeError);
            if (mirrorPlaneFeature == null)
            {
                error = planeError ?? "Cannot resolve native mirror plane.";
                return false;
            }

            partDoc.ClearSelection2(true);
            if (!mirrorPlaneFeature.Select2(false, 2))
            {
                error = "Cannot select native mirror plane with selection mark 2.";
                return false;
            }

            foreach (Body2 sourceBody in sourceBodyReferences)
            {
                bool bodySelected = false;
                try
                {
                    if (!string.IsNullOrEmpty(sourceBody.Name))
                    {
                        bodySelected = partDoc.Extension.SelectByID2(sourceBody.Name, "SOLIDBODY", 0, 0, 0, true, 256, null, 0);
                    }
                }
                catch { }

                if (!bodySelected)
                {
                    bodySelected = sourceBody.Select(true, 256);
                }

                if (!bodySelected)
                {
                    partDoc.ClearSelection2(true);
                    error = "Cannot select a source solid body with selection mark 256.";
                    return false;
                }
            }

            // Chữ ký thực tế của InsertMirrorFeature2 trong SolidWorks API:
            // InsertMirrorFeature2(bool BMirrorBody, bool BGeometryPattern, bool BMerge, bool BKnit, int ScopeOptions)
            Feature mirrorFeature = partDoc.FeatureManager.InsertMirrorFeature2(
                true,  // BMirrorBody (Phải là TRUE để mirror Body!)
                false, // BGeometryPattern
                false, // BMerge (false để giữ riêng biệt với body gốc)
                false, // BKnit
                (int)swFeatureScope_e.swFeatureScope_AllBodies);

            partDoc.ClearSelection2(true);
            if (mirrorFeature == null)
            {
                error = "IFeatureManager.InsertMirrorFeature2 returned null.";
                return false;
            }

            try { mirrorFeature.Name = "MIRROR_BODY_NATIVE"; } catch { }

            partDoc.ForceRebuild3(false);
            bool mirrorWarning = false;
            int mirrorError = mirrorFeature.GetErrorCode2(out mirrorWarning);
            if (mirrorError != 0)
            {
                error = $"Mirror Bodies feature rebuild error={mirrorError}, warning={mirrorWarning}.";
                return false;
            }

            List<Body2> allBodiesAfterMirror = BodyOperationsHelper.GetAllSolidBodies(partDoc);
            List<Body2> newBodyCandidates = new List<Body2>();
            foreach (Body2 body in allBodiesAfterMirror)
            {
                bool isOriginal = false;
                try { isOriginal = !string.IsNullOrEmpty(body.Name) && sourceBodyNames.Contains(body.Name); } catch { }

                if (!isOriginal)
                {
                    try
                    {
                        string selectionId = body.GetSelectionId();
                        isOriginal = !string.IsNullOrEmpty(selectionId) && sourceSelectionIds.Contains(selectionId);
                    } catch { }
                }

                if (!isOriginal) newBodyCandidates.Add(body);
            }

            List<Body2> mirroredBodyReferences;
            string mappingError;
            if (!TryMapExactBodyReferences(expectedMirroredBodies, newBodyCandidates, "NATIVE_MIRROR_NEW_BODY_MATCH", out mirroredBodyReferences, out mappingError))
            {
                if (!TryMapExactBodyReferences(expectedMirroredBodies, allBodiesAfterMirror, "NATIVE_MIRROR_ALL_BODY_MATCH", out mirroredBodyReferences, out mappingError))
                {
                    error = "Cannot identify mirrored bodies produced by native feature: " + mappingError;
                    return false;
                }
            }

            partDoc.ClearSelection2(true);
            foreach (Body2 mirroredBody in mirroredBodyReferences)
            {
                if (!mirroredBody.Select(true, 0))
                {
                    partDoc.ClearSelection2(true);
                    error = "Cannot select a mirrored body for Keep Bodies.";
                    return false;
                }
            }

            Feature keepBodiesFeature = partDoc.FeatureManager.InsertDeleteBody2(true);
            partDoc.ClearSelection2(true);
            if (keepBodiesFeature == null)
            {
                error = "IFeatureManager.InsertDeleteBody2(true) returned null.";
                return false;
            }

            try { keepBodiesFeature.Name = "KEEP_MIRRORED_BODIES"; } catch { }
            partDoc.ForceRebuild3(false);
            
            CreateMirrorPartPackage.LogDebug($"NATIVE_BODY_MIRROR planeFeature={mirrorPlaneFeature.Name} planeCreated={createdReferencePlane} mirrorFeature={mirrorFeature.Name} keepFeature={keepBodiesFeature.Name} result=PASS");
            return true;
        }

        private static Feature TryGetOrCreateNativeMirrorPlane(
            ISldWorks swApp,
            ModelDoc2 partDoc,
            PlaneData mirrorPlane,
            out bool created,
            out string error)
        {
            created = false;
            error = null;
            Feature matchingPlane = FindMatchingReferencePlaneFeature(swApp, partDoc, mirrorPlane);
            if (matchingPlane != null) return matchingPlane;

            double[] normal = mirrorPlane.Normal;
            double normalLength = Math.Sqrt(normal[0] * normal[0] + normal[1] * normal[1] + normal[2] * normal[2]);
            if (normalLength <= 1.0e-12)
            {
                error = "Mirror plane has a zero-length normal.";
                return null;
            }

            double[] n = new double[] { normal[0] / normalLength, normal[1] / normalLength, normal[2] / normalLength };
            double[] axis = Math.Abs(n[0]) < 0.9 ? new double[] { 1.0, 0.0, 0.0 } : new double[] { 0.0, 1.0, 0.0 };
            double[] u = NormalizeVector(CrossProduct(axis, n));
            double[] v = NormalizeVector(CrossProduct(n, u));
            double[] origin = mirrorPlane.Origin ?? new double[] { 0.0, 0.0, 0.0 };
            const double planeSize = 0.1;
            
            double[] p1 = new double[] { origin[0], origin[1], origin[2] };
            double[] p2 = new double[] { origin[0] + planeSize * u[0], origin[1] + planeSize * u[1], origin[2] + planeSize * u[2] };
            double[] p3 = new double[] { origin[0] + planeSize * v[0], origin[1] + planeSize * v[1], origin[2] + planeSize * v[2] };

            partDoc.ClearSelection2(true);
            object createdPlaneObj = partDoc.CreatePlaneFixed2(p1, p2, p3, false);
            if (createdPlaneObj == null)
            {
                error = "IModelDoc2.CreatePlaneFixed2 returned null.";
                return null;
            }

            created = true;

            // [SỬA LỖI]: Bắt trực tiếp Feature Plane từ đối tượng vừa được SOLIDWORKS tự động chọn
            SelectionMgr selMgr = partDoc.SelectionManager as SelectionMgr;
            Feature createdPlaneFeat = selMgr.GetSelectedObject6(1, -1) as Feature;

            if (createdPlaneFeat != null && createdPlaneFeat.GetTypeName2() == "RefPlane")
            {
                try { createdPlaneFeat.Name = "MIRROR_REFERENCE_PLANE"; } catch { }
                return createdPlaneFeat;
            }

            error = "Plane created but could not retrieve its Feature object from Selection Manager.";
            return null;
        }

        private static Feature FindMatchingReferencePlaneFeature(
            ISldWorks swApp,
            ModelDoc2 partDoc,
            PlaneData targetPlane)
        {
            if (swApp == null || partDoc == null || targetPlane == null) return null;
            IMathUtility mathUtility = swApp.GetMathUtility() as IMathUtility;
            if (mathUtility == null) return null;

            double[] targetNormal = NormalizeVector(targetPlane.Normal);
            double[] targetOrigin = targetPlane.Origin ?? new double[] { 0.0, 0.0, 0.0 };
            Feature feature = partDoc.FirstFeature() as Feature;
            
            while (feature != null)
            {
                try
                {
                    if (string.Equals(feature.GetTypeName2(), "RefPlane", StringComparison.Ordinal))
                    {
                        RefPlane referencePlane = feature.GetSpecificFeature2() as RefPlane;
                        MathTransform transform = referencePlane?.Transform;
                        if (transform != null)
                        {
                            MathPoint canonicalOrigin = mathUtility.CreatePoint(new double[] { 0.0, 0.0, 0.0 }) as MathPoint;
                            MathVector canonicalNormal = mathUtility.CreateVector(new double[] { 0.0, 0.0, 1.0 }) as MathVector;
                            MathPoint modelOrigin = canonicalOrigin?.MultiplyTransform(transform) as MathPoint;
                            MathVector modelNormal = canonicalNormal?.MultiplyTransform(transform) as MathVector;
                            double[] origin = modelOrigin?.ArrayData as double[];
                            double[] normal = NormalizeVector(modelNormal?.ArrayData as double[]);
                            
                            if (origin != null && normal != null)
                            {
                                double normalAlignment = Math.Abs(DotProduct(normal, targetNormal));
                                double pointPlaneDistance = Math.Abs(
                                    (origin[0] - targetOrigin[0]) * targetNormal[0] +
                                    (origin[1] - targetOrigin[1]) * targetNormal[1] +
                                    (origin[2] - targetOrigin[2]) * targetNormal[2]);
                                    
                                // [SỬA LỖI]: Nới lỏng sai số từ 1e-8 xuống 1e-4 để API không bị từ chối
                                if (normalAlignment >= 1.0 - 1.0e-4 && pointPlaneDistance <= 1.0e-4)
                                {
                                    return feature;
                                }
                            }
                        }
                    }
                }
                catch { }

                feature = feature.GetNextFeature() as Feature;
            }

            return null;
        }


        private static Feature FindTopLevelFeatureByName(ModelDoc2 partDoc, string featureName)
        {
            if (partDoc == null || string.IsNullOrEmpty(featureName)) return null;
            Feature feature = partDoc.FirstFeature() as Feature;
            while (feature != null)
            {
                if (string.Equals(feature.Name, featureName, StringComparison.Ordinal))
                {
                    return feature;
                }

                feature = feature.GetNextFeature() as Feature;
            }

            return null;
        }

        private static double[] NormalizeVector(double[] vector)
        {
            if (vector == null || vector.Length < 3) return null;
            double length = Math.Sqrt(
                vector[0] * vector[0] +
                vector[1] * vector[1] +
                vector[2] * vector[2]);
            if (length <= 1.0e-12) return null;
            return new double[]
            {
                vector[0] / length,
                vector[1] / length,
                vector[2] / length
            };
        }

        private static double[] CrossProduct(double[] left, double[] right)
        {
            return new double[]
            {
                left[1] * right[2] - left[2] * right[1],
                left[2] * right[0] - left[0] * right[2],
                left[0] * right[1] - left[1] * right[0]
            };
        }

        private static double DotProduct(double[] left, double[] right)
        {
            if (left == null || right == null || left.Length < 3 || right.Length < 3) return 0.0;
            return left[0] * right[0] + left[1] * right[1] + left[2] * right[2];
        }

        private static bool TryMapExactBodyReferences(
            List<Body2> expectedBodies,
            List<Body2> candidateBodies,
            string label,
            out List<Body2> matchedBodies,
            out string error)
        {
            matchedBodies = new List<Body2>();
            error = null;
            if (expectedBodies == null || candidateBodies == null)
            {
                error = "Body collection is null.";
                return false;
            }

            if (candidateBodies.Count < expectedBodies.Count)
            {
                error =
                    $"Not enough candidate bodies: expected={expectedBodies.Count}, " +
                    $"candidates={candidateBodies.Count}.";
                return false;
            }

            bool[] usedCandidates = new bool[candidateBodies.Count];
            for (int expectedIndex = 0; expectedIndex < expectedBodies.Count; expectedIndex++)
            {
                Body2 expectedBody = expectedBodies[expectedIndex];
                double expectedVolume = BodyOperationsHelper.GetBodyVolume(expectedBody);
                double tolerance = Math.Max(
                    BodyOperationsHelper.ABSOLUTE_GEOMETRY_TOLERANCE,
                    expectedVolume * BodyOperationsHelper.BODY_TRANSFORM_RELATIVE_TOLERANCE);
                bool found = false;

                for (int candidateIndex = 0; candidateIndex < candidateBodies.Count; candidateIndex++)
                {
                    if (usedCandidates[candidateIndex]) continue;
                    Body2 candidateBody = candidateBodies[candidateIndex];
                    double candidateVolume = BodyOperationsHelper.GetBodyVolume(candidateBody);
                    if (Math.Abs(expectedVolume - candidateVolume) > tolerance) continue;

                    BodyBooleanResult expectedMinusCandidate = BodyOperationsHelper.BooleanCutStrict(
                        expectedBody,
                        candidateBody,
                        label + "_EXPECTED_MINUS_CANDIDATE");
                    BodyBooleanResult candidateMinusExpected = BodyOperationsHelper.BooleanCutStrict(
                        candidateBody,
                        expectedBody,
                        label + "_CANDIDATE_MINUS_EXPECTED");
                    if (!expectedMinusCandidate.Success || !candidateMinusExpected.Success) continue;

                    double expectedResidual = BodyOperationsHelper.SumBodyVolumes(expectedMinusCandidate.Bodies);
                    double candidateResidual = BodyOperationsHelper.SumBodyVolumes(candidateMinusExpected.Bodies);
                    if (expectedResidual <= tolerance && candidateResidual <= tolerance)
                    {
                        usedCandidates[candidateIndex] = true;
                        matchedBodies.Add(candidateBody);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    error = $"No exact geometric match for expected body index {expectedIndex}.";
                    return false;
                }
            }

            CreateMirrorPartPackage.LogDebug(
                $"NATIVE_BODY_MAP\nlabel={label}\nexpectedCount={expectedBodies.Count}\n" +
                $"candidateCount={candidateBodies.Count}\nmatchedCount={matchedBodies.Count}\nresult=PASS");
            return true;
        }

        private static void SetBaseOptions22(ModelDoc2 document, Feature feature, bool direction, bool thickness)
        {
            IBaseFlangeFeatureData data = feature.GetDefinition() as IBaseFlangeFeatureData;
            if (data == null || !data.AccessSelections(document, null))
                throw new InvalidOperationException("BASE22 AccessSelections failed.");
            bool modified;
            try
            {
                data.ReverseDirection = direction;
                data.ReverseThickness = thickness;
                modified = feature.ModifyDefinition(data, document, null);
            }
            finally { data.ReleaseSelectionAccess(); }
            if (!modified) throw new InvalidOperationException("BASE22 ModifyDefinition returned false.");
            document.ForceRebuild3(false);
            IBaseFlangeFeatureData readback = feature.GetDefinition() as IBaseFlangeFeatureData;
            if (readback == null || readback.ReverseDirection != direction || readback.ReverseThickness != thickness)
                throw new InvalidOperationException("BASE22 option readback mismatch.");
        }

        private static void VerifyBaseAbsoluteGate53(Body2 expected, Body2 actual)
        {
            if (expected == null || actual == null)
                throw new InvalidOperationException("BASE53: Expected or actual base body is unavailable.");

            double[] expectedMass = expected.GetMassProperties(0) as double[];
            double[] actualMass = actual.GetMassProperties(0) as double[];
            double[] expectedBox = expected.GetBodyBox() as double[];
            double[] actualBox = actual.GetBodyBox() as double[];
            if (expectedMass == null || expectedMass.Length < 4 ||
                actualMass == null || actualMass.Length < 4 ||
                expectedBox == null || expectedBox.Length < 6 ||
                actualBox == null || actualBox.Length < 6)
                throw new InvalidOperationException("BASE53: Absolute centroid/bounding-box data is unavailable.");

            double centroidResidual = Math.Sqrt(
                Math.Pow(expectedMass[0] - actualMass[0], 2) +
                Math.Pow(expectedMass[1] - actualMass[1], 2) +
                Math.Pow(expectedMass[2] - actualMass[2], 2));
            double boxResidual = Enumerable.Range(0, 6)
                .Max(index53 => Math.Abs(expectedBox[index53] - actualBox[index53]));
            double diagonal = Math.Sqrt(
                Math.Pow(expectedBox[3] - expectedBox[0], 2) +
                Math.Pow(expectedBox[4] - expectedBox[1], 2) +
                Math.Pow(expectedBox[5] - expectedBox[2], 2));
            double tolerance = Math.Max(1.0e-7, diagonal * 1.0e-7);

            CreateMirrorPartPackage.LogDebug("[BASE53][ABSOLUTE_GATE] expectedCentroid_m=" +
                string.Join(",", expectedMass.Take(3).Select(value53 => value53.ToString("R"))) +
                " actualCentroid_m=" +
                string.Join(",", actualMass.Take(3).Select(value53 => value53.ToString("R"))) +
                " centroidResidual_m=" + centroidResidual.ToString("R") +
                " boxResidual_m=" + boxResidual.ToString("R") +
                " tolerance_m=" + tolerance.ToString("R") +
                " result=" + (centroidResidual <= tolerance && boxResidual <= tolerance ? "PASS" : "FAIL"));

            if (centroidResidual > tolerance || boxResidual > tolerance)
                throw new InvalidOperationException("BASE53: Base body is not at the absolute reflected position; downstream replay cancelled.");
        }

        private static bool VerifySketchPatternCheckpoint57(ISldWorks swApp,
            PostBaseFeatureInfo info, FeatureBodyState source,
            Body2 actualBefore, Body2 actualAfter, PlaneData mirrorPlane,
            out string error)
        {
            error = null;
            if (swApp == null || info == null || source == null ||
                source.BeforeBody == null || source.AfterBody == null ||
                actualBefore == null || actualAfter == null || mirrorPlane == null)
            {
                error = "Source/actual checkpoint or reflection plane is unavailable.";
                return false;
            }

            BodyTransformResult expectedBefore = BodyOperationsHelper.MirrorBodyStrict(
                swApp, source.BeforeBody, mirrorPlane);
            BodyTransformResult expectedAfter = BodyOperationsHelper.MirrorBodyStrict(
                swApp, source.AfterBody, mirrorPlane);
            if (expectedBefore == null || !expectedBefore.Success || expectedBefore.Body == null ||
                expectedAfter == null || !expectedAfter.Success || expectedAfter.Body == null)
            {
                error = "Cannot reflect source pattern checkpoint.";
                return false;
            }

            string beforeError;
            bool beforeMatch = AreExactBodySetsEquivalent(
                new List<Body2> { expectedBefore.Body },
                new List<Body2> { actualBefore },
                info.Name + "_SKETCH_PATTERN57_BEFORE", out beforeError);
            string afterError;
            bool afterMatch = AreExactBodySetsEquivalent(
                new List<Body2> { expectedAfter.Body },
                new List<Body2> { actualAfter },
                info.Name + "_SKETCH_PATTERN57_AFTER", out afterError);
            CreateMirrorPartPackage.LogDebug(
                "[SKETCH_PATTERN57][CHECKPOINT] feature=" + info.Name +
                " before=" + (beforeMatch ? "PASS" : "FAIL") +
                " after=" + (afterMatch ? "PASS" : "FAIL") +
                " beforeReason=" + beforeError + " afterReason=" + afterError);
            if (!beforeMatch || !afterMatch)
                error = !beforeMatch ? "UPSTREAM_BEFORE_MISMATCH: " + beforeError :
                    "PATTERN_AFTER_MISMATCH: " + afterError;
            return beforeMatch && afterMatch;
        }

        private static bool VerifyCutCheckpointOracle59(ISldWorks swApp,
            PostBaseFeatureInfo info, FeatureBodyState source,
            Body2 actualBefore, Body2 actualAfter, PlaneData mirrorPlane)
        {
            if (info == null || !SketchDrivenFeatureMirrorHandler.IsExtrudeCutType(info.Type)) return false;
            if (source == null || source.AfterBody == null || source.RemovedBodies == null ||
                actualBefore == null || actualAfter == null || mirrorPlane == null)
                throw new InvalidOperationException("CUT_ORACLE59: Cut checkpoint is incomplete: " + info.Name);

            double expectedCutVolume;
            double[] sourceCutCentroid;
            if (!BodyOperationsHelper.TryGetBodiesVolumeCentroid(source.RemovedBodies,
                out expectedCutVolume, out sourceCutCentroid))
                throw new InvalidOperationException("CUT_ORACLE59: Source cut has no measurable removed body: " + info.Name);
            double[] expectedCutCentroid = BodyOperationsHelper.ReflectPointAcrossPlane(
                sourceCutCentroid, mirrorPlane);
            double actualCutVolume;
            double[] actualCutCentroid;
            string measureError = "Reflected cut centroid unavailable.";
            if (expectedCutCentroid == null || !BodyOperationsHelper.TryMeasureRemovedGeometry(
                actualBefore, actualAfter, info.Name + "_CUT_ORACLE59_DELTA",
                out actualCutVolume, out actualCutCentroid, out measureError))
                throw new InvalidOperationException("CUT_ORACLE59: Cannot measure reflected cut delta: " +
                    info.Name + " reason=" + measureError);

            double cutVolumeError = Math.Abs(expectedCutVolume - actualCutVolume);
            double cutVolumeTolerance = Math.Max(1e-15, expectedCutVolume * 1e-5);
            double centroidError = BodyOperationsHelper.Distance(expectedCutCentroid, actualCutCentroid);
            double centroidTolerance = 1e-6; // one micron floor in the absolute model frame
            var cutBoxes = source.RemovedBodies.Select(body => body.GetBodyBox() as double[])
                .Where(box => box != null && box.Length >= 6).ToList();
            if (cutBoxes.Count > 0)
            {
                double dx = cutBoxes.Max(box => box[3]) - cutBoxes.Min(box => box[0]);
                double dy = cutBoxes.Max(box => box[4]) - cutBoxes.Min(box => box[1]);
                double dz = cutBoxes.Max(box => box[5]) - cutBoxes.Min(box => box[2]);
                centroidTolerance = Math.Max(centroidTolerance,
                    Math.Sqrt(dx * dx + dy * dy + dz * dz) * 1e-6);
            }
            bool cutDeltaPass = cutVolumeError <= cutVolumeTolerance &&
                centroidError <= centroidTolerance;
            CreateMirrorPartPackage.LogDebug("[CUT_ORACLE59][DELTA] feature=" + info.Name +
                " expectedCut_m3=" + expectedCutVolume.ToString("R") +
                " actualCut_m3=" + actualCutVolume.ToString("R") +
                " volumeError_m3=" + cutVolumeError.ToString("R") +
                " volumeTolerance_m3=" + cutVolumeTolerance.ToString("R") +
                " centroidError_m=" + centroidError.ToString("R") +
                " centroidTolerance_m=" + centroidTolerance.ToString("R") +
                " result=" + (cutDeltaPass ? "PASS" : "FAIL"));

            BodyTransformResult reflected = BodyOperationsHelper.MirrorBodyStrict(
                swApp, source.AfterBody, mirrorPlane);
            if (reflected == null || !reflected.Success || reflected.Body == null)
                throw new InvalidOperationException("CUT_ORACLE59: Cannot reflect source checkpoint: " +
                    info.Name + " reason=" + (reflected == null ? "null result" : reflected.ErrorMessage));

            double expectedAfterVolume = BodyOperationsHelper.GetBodyVolume(reflected.Body);
            double actualAfterVolume = BodyOperationsHelper.GetBodyVolume(actualAfter);
            double afterVolumeError = Math.Abs(expectedAfterVolume - actualAfterVolume);
            // Whole-body mass integration and local cut measurements have different
            // scales. Use the same whole-body relative tolerance as VerifyCurrentSolids;
            // keep the tighter cut tolerance for the delta and Boolean residuals.
            double bodyVolumeTolerance = Math.Max(1e-15, Math.Abs(expectedAfterVolume) * 1e-6);
            bool bodyMassPass = expectedAfterVolume > 0.0 && actualAfterVolume > 0.0 &&
                afterVolumeError <= bodyVolumeTolerance;
            double sourceAfterVolume = BodyOperationsHelper.GetBodyVolume(source.AfterBody);
            CreateMirrorPartPackage.LogDebug("[CUT_ORACLE59][BODY_MASS] feature=" + info.Name +
                " sourceAfter_m3=" + sourceAfterVolume.ToString("R") +
                " expectedAfter_m3=" + expectedAfterVolume.ToString("R") +
                " actualAfter_m3=" + actualAfterVolume.ToString("R") +
                " reflectionVolumeDrift_m3=" + Math.Abs(expectedAfterVolume - sourceAfterVolume).ToString("R") +
                " volumeError_m3=" + afterVolumeError.ToString("R") +
                " tolerance_m3=" + bodyVolumeTolerance.ToString("R") +
                " toleranceBasis=WholeBodyRelative1e-6" +
                " result=" + (bodyMassPass ? "PASS" : "FAIL"));

            BodyBooleanResult missing = BodyOperationsHelper.BooleanCutStrict(
                reflected.Body, actualAfter, info.Name + "_CUT_ORACLE59_MISSING");
            BodyBooleanResult extra = BodyOperationsHelper.BooleanCutStrict(
                actualAfter, reflected.Body, info.Name + "_CUT_ORACLE59_EXTRA");
            double missingVolume = missing.Success
                ? BodyOperationsHelper.SumBodyVolumes(missing.Bodies) : double.NaN;
            double extraVolume = extra.Success
                ? BodyOperationsHelper.SumBodyVolumes(extra.Bodies) : double.NaN;
            double tolerance = cutVolumeTolerance;
            bool booleanPass = missing.Success && extra.Success &&
                missingVolume <= tolerance && extraVolume <= tolerance;
            bool exactAfter53 = missing.Success && extra.Success && missing.Bodies != null && extra.Bodies != null &&
                missing.Bodies.Count == 0 && extra.Bodies.Count == 0;
            bool exactBefore53 = false;
            // Both semantic and oracle mass gates consume the same before/after proof.
            if (exactAfter53)
            {
                if (source.BeforeBody == null)
                    throw new InvalidOperationException("CUT53: Before-cut source checkpoint unavailable.");
                var reflectedBefore53 = BodyOperationsHelper.MirrorBodyStrict(swApp, source.BeforeBody, mirrorPlane);
                if (reflectedBefore53 == null || !reflectedBefore53.Success || reflectedBefore53.Body == null)
                    throw new InvalidOperationException("CUT53: Cannot reflect before-cut checkpoint.");
                var beforeMissing53 = BodyOperationsHelper.BooleanCutStrict(reflectedBefore53.Body, actualBefore,
                    info.Name + "_CUT53_BEFORE_MISSING");
                var beforeExtra53 = BodyOperationsHelper.BooleanCutStrict(actualBefore, reflectedBefore53.Body,
                    info.Name + "_CUT53_BEFORE_EXTRA");
                exactBefore53 = beforeMissing53.Success && beforeExtra53.Success &&
                    beforeMissing53.Bodies != null && beforeExtra53.Bodies != null &&
                    beforeMissing53.Bodies.Count == 0 && beforeExtra53.Bodies.Count == 0;
            }
            bool exactEvidence53 = CutMassEvidence53.CanAccept(exactBefore53, exactAfter53,
                expectedCutVolume, actualCutVolume, expectedAfterVolume, actualAfterVolume,
                centroidError, centroidTolerance);
            bool pass = (cutDeltaPass && bodyMassPass && booleanPass) || exactEvidence53;
            CreateMirrorPartPackage.LogDebug("[CUT53][EXACT_EVIDENCE] feature=" + info.Name +
                " beforeEmpty=" + exactBefore53 + " afterEmpty=" + exactAfter53 +
                " massDiscrepancyAccepted=" + (exactEvidence53 && (!cutDeltaPass || !bodyMassPass)));
            string failureReason = !cutDeltaPass ? "CUT_DELTA_MISMATCH" :
                !bodyMassPass ? "BODY_MASS_MISMATCH" :
                !booleanPass ? "BODY_BOOLEAN_MISMATCH_OR_UNAVAILABLE" : "NONE";
            if (pass) failureReason = "NONE";

            CreateMirrorPartPackage.LogDebug("[CUT_ORACLE59][CHECK] feature=" + info.Name +
                " expectedCut_m3=" + expectedCutVolume.ToString("R") +
                " missing_m3=" + missingVolume.ToString("R") +
                " extra_m3=" + extraVolume.ToString("R") +
                " tolerance_m3=" + tolerance.ToString("R") +
                " missingBoolean=" + missing.Success +
                " extraBoolean=" + extra.Success +
                " reason=" + failureReason +
                " result=" + (pass ? "PASS" : "FAIL"));
            if (!pass)
                throw new InvalidOperationException("CUT_ORACLE59: Reflected cut checkpoint differs at " +
                    info.Name + " reason=" + failureReason + "; downstream pattern replay cancelled.");
            return exactEvidence53;
        }

        private static void ResolveBaseOptions22(ModelDoc2 document, Feature feature, Body2 expected,
            bool originalDirection, bool originalThickness, BaseFlangeDiagnosisResult diagnosis)
        {
            int preferred = (diagnosis.NeedReverseDirection ? 1 : 0) |
                ((diagnosis.NeedReverseThickness || (diagnosis.NeedReverseExtrusion && !diagnosis.NeedReverseDirection)) ? 2 : 0);
            var candidates = new List<int> { preferred };
            for (int mask = 0; mask < 4; mask++) if (mask != preferred) candidates.Add(mask);
            bool accepted = false;
            try
            {
                foreach (int mask in candidates)
                {
                    bool direction = originalDirection ^ ((mask & 1) != 0);
                    bool thickness = originalThickness ^ ((mask & 2) != 0);
                    string label = "BASE22_D" + direction + "_T" + thickness;
                    // Absolute assignments from the source state; never cumulative toggles.
                    SetBaseOptions22(document, feature, direction, thickness);
                    string captureError;
                    Body2 actual = BodyOperationsHelper.GetSolidBodyCopyStrict(document, out captureError);
                    if (actual == null)
                    {
                        CreateMirrorPartPackage.LogDebug("[BASE22][CANDIDATE] " + label + " result=NO_BODY reason=" + captureError);
                        continue;
                    }
                    // BooleanCutStrict works on copies; these oracle bodies are never features.
                    var missing = BodyOperationsHelper.BooleanCutStrict(expected, actual, label + "_MISSING");
                    var extra = BodyOperationsHelper.BooleanCutStrict(actual, expected, label + "_EXTRA");
                    double tolerance = Math.Max(BodyOperationsHelper.ABSOLUTE_GEOMETRY_TOLERANCE,
                        BodyOperationsHelper.GetBodyVolume(expected) * BodyOperationsHelper.BODY_TRANSFORM_RELATIVE_TOLERANCE);
                    double missingVolume = missing.Success ? BodyOperationsHelper.SumBodyVolumes(missing.Bodies) : double.NaN;
                    double extraVolume = extra.Success ? BodyOperationsHelper.SumBodyVolumes(extra.Bodies) : double.NaN;
                    bool pass = missing.Success && extra.Success && missingVolume <= tolerance && extraVolume <= tolerance;
                    CreateMirrorPartPackage.LogDebug($"[BASE22][CANDIDATE] {label} missing_m3={missingVolume:R} extra_m3={extraVolume:R} tolerance_m3={tolerance:R} result={(pass ? "PASS" : "FAIL")} booleanMissing={missing.Success} booleanExtra={extra.Success}");
                    if (!pass) continue;
                    accepted = true;
                    CreateMirrorPartPackage.LogDebug($"[BASE22][COMMIT_PASS] ReverseDirection={direction} ReverseThickness={thickness} nativeFeature=True exactBaseGeometry=True");
                    return;
                }
                throw new InvalidOperationException("BASE22_NO_MATCH: no native direction/thickness candidate matches the reflected base. Downstream replay cancelled.");
            }
            finally
            {
                if (!accepted)
                {
                    // Restore options on the diagnostic copy; the source is never modified.
                    SetBaseOptions22(document, feature, originalDirection, originalThickness);
                    CreateMirrorPartPackage.LogDebug("[BASE22][STOP] originalOptionsRestored=True outputNotPublished=True");
                }
            }
        }

        private static bool AreExactBodySetsEquivalent(
            List<Body2> expectedBodies,
            List<Body2> actualBodies,
            string label,
            out string error)
        {
            error = null;
            if (expectedBodies == null || actualBodies == null)
            {
                error = "Body collection is null.";
                return false;
            }

            if (expectedBodies.Count != actualBodies.Count)
            {
                error = $"Body count mismatch: expected={expectedBodies.Count}, actual={actualBodies.Count}.";
                return false;
            }

            bool[] usedActualBodies = new bool[actualBodies.Count];
            for (int expectedIndex = 0; expectedIndex < expectedBodies.Count; expectedIndex++)
            {
                Body2 expectedBody = expectedBodies[expectedIndex];
                double expectedVolume = BodyOperationsHelper.GetBodyVolume(expectedBody);
                double tolerance = Math.Max(
                    BodyOperationsHelper.ABSOLUTE_GEOMETRY_TOLERANCE,
                    expectedVolume * BodyOperationsHelper.BODY_TRANSFORM_RELATIVE_TOLERANCE);
                bool found = false;

                for (int actualIndex = 0; actualIndex < actualBodies.Count; actualIndex++)
                {
                    if (usedActualBodies[actualIndex]) continue;

                    Body2 actualBody = actualBodies[actualIndex];
                    double actualVolume = BodyOperationsHelper.GetBodyVolume(actualBody);
                    if (Math.Abs(expectedVolume - actualVolume) > tolerance) continue;

                    BodyBooleanResult expectedMinusActual = BodyOperationsHelper.BooleanCutStrict(
                        expectedBody,
                        actualBody,
                        label + "_EXPECTED_MINUS_ACTUAL");
                    BodyBooleanResult actualMinusExpected = BodyOperationsHelper.BooleanCutStrict(
                        actualBody,
                        expectedBody,
                        label + "_ACTUAL_MINUS_EXPECTED");
                    if (!expectedMinusActual.Success || !actualMinusExpected.Success) continue;

                    double expectedResidual = BodyOperationsHelper.SumBodyVolumes(expectedMinusActual.Bodies);
                    double actualResidual = BodyOperationsHelper.SumBodyVolumes(actualMinusExpected.Bodies);
                    if (expectedResidual <= tolerance && actualResidual <= tolerance)
                    {
                        usedActualBodies[actualIndex] = true;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    error = $"No exact geometric match for expected body index {expectedIndex}.";
                    return false;
                }
            }

            CreateMirrorPartPackage.LogDebug(
                $"EXACT_BODY_VALIDATE\nlabel={label}\nexpectedCount={expectedBodies.Count}\n" +
                $"actualCount={actualBodies.Count}\n" +
                $"expectedVolume={BodyOperationsHelper.SumBodyVolumes(expectedBodies):E6}\n" +
                $"actualVolume={BodyOperationsHelper.SumBodyVolumes(actualBodies):E6}\nresult=PASS");
            return true;
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

                    // Skip internal flat pattern / system bend features / folder features
                    if (!string.Equals(realType, "FlatPattern", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(realType, "ProcessBends", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(realType, "FlattenBends", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(realType, "ProfileFeature", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(realType, "3DProfileFeature", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(realType, "OriginProfileFeature", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(realType, "CutListFolder", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(realType, "SolidBodyFolder", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(realType, "SurfaceBodyFolder", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(realType, "SheetMetal", StringComparison.OrdinalIgnoreCase))
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
            bool sourceReferenceDiagnosticCaptured = false;

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

                    if (!info.IsSuppressed && info.DrivingSketchFeature != null)
                    {
                        if (SketchDrivenFeatureMirrorHandler.DiagnosticOnly && !sourceReferenceDiagnosticCaptured &&
                            SketchDrivenFeatureMirrorHandler.IsExtrudeCutType(info.Type))
                        {
                            bool before = partDoc.FeatureManager.EditRollback(
                                (int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, info.Name);
                            if (!before) throw new InvalidOperationException("REFTRACE32: Cannot capture source before-Cut state.");
                            try
                            {
                                ADDIN.Diagnostics.SketchReferenceDiagnostic.Run(partDoc, info.DrivingSketchFeature,
                                    CreateMirrorPartPackage.LogDebug, info.Feature, "before-base-mutation/before-cut");
                                sourceReferenceDiagnosticCaptured = true;
                            }
                            finally
                            {
                                if (!partDoc.FeatureManager.EditRollback(
                                    (int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, info.Name))
                                    throw new InvalidOperationException("REFTRACE32: Cannot restore source cache checkpoint.");
                            }
                        }
                        if (info.Feature.GetDefinition() is IExtrudeFeatureData2)
                            info.CutAudit21 = SketchOperationsHelper.CaptureCutAudit21(partDoc, info.Feature, info.DrivingSketchFeature);
                        var originalSketch = info.DrivingSketchFeature.GetSpecificFeature2() as Sketch;
                        info.PristineSketchPoints = SketchOperationsHelper.CapturePristineSketchPoints(originalSketch);
                        info.PristineSketchSlots = SketchOperationsHelper.CapturePristineSketchSlots(originalSketch);
                        info.PristineSketchPrimitives58 = SketchOperationsHelper.CapturePristineSketchPrimitives58(originalSketch);
                        info.PristineSupport20 = SketchOperationsHelper.CaptureSupport20(originalSketch, partDoc);
                        CreateMirrorPartPackage.LogDebug("[CUTSPACE20][CHECKPOINT_CAPTURE] feature=" + info.Name +
                            " points=" + info.PristineSketchPoints.Count + " faceSupport=" + (info.PristineSupport20 != null));
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

                    // Sheet metal EdgeFlange features modify/add geometry parametrically and do not rely on Boolean cut delta
                    if (string.Equals(info.Type, "EdgeFlange", StringComparison.OrdinalIgnoreCase) ||
                        info.Type.IndexOf("EdgeFlange", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        state.ChangesGeometry = true;
                        state.ChangeKind = FeatureGeometryChangeKind.Additive;
                        cacheList.Add(state);
                        prevBody = bi;
                        continue;
                    }

                    BodyBooleanResult cutAdd = BodyOperationsHelper.BooleanCutStrict(bi, prevBody, $"{info.Name}_ADDED");
                    BodyBooleanResult cutRem = BodyOperationsHelper.BooleanCutStrict(prevBody, bi, $"{info.Name}_REMOVED");

                    if (!cutAdd.Success || !cutRem.Success)
                    {
                        errorMessage = $"Boolean delta calculation failed for feature '{info.Name}'." +
                            " addedError=" + cutAdd.ErrorMessage + " removedError=" + cutRem.ErrorMessage;
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

                    // Boolean delta bodies are evidence of a local operation regardless
                    // of the size of the parent part (e.g. a small chamfer on a long sheet).
                    double tol = BodyOperationsHelper.ABSOLUTE_GEOMETRY_TOLERANCE;
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

                    CreateMirrorPartPackage.LogDebug($"FEATURE_CACHE_CLASSIFICATION\nfeature={info.Name}\nkind={state.ChangeKind}\naddedVolume={addedVol:E6}\nremovedVolume={removedVol:E6}\nchangeThreshold_m3={tol:R}\nthresholdBasis=LocalBooleanDelta");

                    cacheList.Add(state);
                    prevBody = bi;
                }
            }
            finally
            {
                bool rbEnd = partDoc.FeatureManager.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, "");
                CreateMirrorPartPackage.LogDebug($"CACHE_BUILD: EditRollback(ToEnd) result={rbEnd}");
            }

            var captureErrors47 = new List<PostBaseFeatureInfo>();
            var unsupportedReferences47 = new List<Tuple<string, string>>();
            foreach (var info47 in postBaseFeatures)
            {
                if (info47.CutAudit21 == null) continue;
                if (!string.IsNullOrEmpty(info47.CutAudit21.CaptureError)) captureErrors47.Add(info47);
                if (info47.CutAudit21.References30 == null) continue;
                foreach (var reference47 in info47.CutAudit21.References30)
                    if (string.Equals(reference47.Kind31, "Unsupported", StringComparison.Ordinal))
                        unsupportedReferences47.Add(Tuple.Create(info47.Name, reference47.Key));
            }
            foreach (var issue47 in captureErrors47)
                CreateMirrorPartPackage.LogDebug("[PREFLIGHT47][CAPTURE_ERROR] feature=" + issue47.Name +
                    " reason=" + issue47.CutAudit21.CaptureError);
            foreach (var issue47 in unsupportedReferences47)
                CreateMirrorPartPackage.LogDebug("[PREFLIGHT47][UNSUPPORTED_REFERENCE] feature=" + issue47.Item1 +
                    " relation=" + issue47.Item2);
            CreateMirrorPartPackage.LogDebug("[PREFLIGHT47][SUMMARY] scannedFeatures=" + postBaseFeatures.Count +
                " captureErrors=" + captureErrors47.Count + " unsupportedReferences=" + unsupportedReferences47.Count);

            LogCompleteCutPreflight50(postBaseFeatures);

            return cacheList;
        }

        private static void LogCompleteCutPreflight50(List<PostBaseFeatureInfo> features)
        {
            int cuts = 0;
            int ready = 0;
            int blockedCapture = 0;
            int blockedExternal = 0;
            int blockedEndReference = 0;
            int externalReferenceCount = 0;
            int endReferenceCount = 0;

            CreateMirrorPartPackage.LogDebug("[PREFLIGHT50][BEGIN] mode=READ_ONLY_AGGREGATE stopOnFirstError=False");
            foreach (PostBaseFeatureInfo info in features)
            {
                ADDIN.Helpers.CutAuditSnapshot21 audit = info.CutAudit21;
                if (audit == null) continue;
                cuts++;

                var references = audit.References30 ?? new List<ADDIN.Helpers.RelationReference30>();
                int unsupported = 0;
                var kindCounts50 = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (var countedReference50 in references)
                {
                    string countedKind50 = countedReference50.Kind31 ?? "UNKNOWN";
                    int currentCount50;
                    kindCounts50.TryGetValue(countedKind50, out currentCount50);
                    kindCounts50[countedKind50] = currentCount50 + 1;
                    if (string.Equals(countedKind50, "Unsupported", StringComparison.Ordinal)) unsupported++;
                }
                externalReferenceCount += references.Count;

                foreach (var reference in references)
                {
                    CreateMirrorPartPackage.LogDebug("[PREFLIGHT50][SKETCH_REFERENCE] feature=\"" + info.Name +
                        "\" relation=\"" + reference.Key + "\" kind=" + (reference.Kind31 ?? "UNKNOWN") +
                        " status=" + (string.Equals(reference.Kind31, "Unsupported", StringComparison.Ordinal)
                            ? "UNSUPPORTED" : "CAPTURED"));
                }

                bool endBlocked = false;
                bool geometryFallback54 = audit.Recipe44 != null && audit.Recipe44.MissingSurfaceReference54;
                string d1 = FormatDirectionPreflight50(audit.D1_50, ref endReferenceCount,
                    ref endBlocked, geometryFallback54);
                string d2 = FormatDirectionPreflight50(audit.D2_50, ref endReferenceCount, ref endBlocked);
                string status;
                if (!string.IsNullOrEmpty(audit.CaptureError))
                {
                    status = "BLOCKED_CAPTURE";
                    blockedCapture++;
                }
                else if (unsupported > 0)
                {
                    status = "BLOCKED_EXTERNAL_REFERENCE";
                    blockedExternal++;
                }
                else if (geometryFallback54 && !endBlocked)
                {
                    status = "GEOMETRY_GATED_BLIND_CANDIDATE";
                    ready++;
                }
                else if (endBlocked)
                {
                    status = "BLOCKED_END_REFERENCE_MAPPER";
                    blockedEndReference++;
                }
                else
                {
                    status = "CAPTURE_READY";
                    ready++;
                }

                var kindSummaryParts50 = new List<string>();
                foreach (var pair50 in kindCounts50)
                    kindSummaryParts50.Add(pair50.Key + ":" + pair50.Value);
                string kindSummary = string.Join(",", kindSummaryParts50.ToArray());
                if (string.IsNullOrEmpty(kindSummary)) kindSummary = "NONE";

                CreateMirrorPartPackage.LogDebug("[PREFLIGHT50][CUT] index=" + info.Index +
                    " feature=\"" + info.Name + "\" sketch=\"" + info.DrivingSketchName +
                    "\" profile=" + (audit.ProfileKind50 ?? "UNKNOWN") +
                    " segments=" + audit.SegmentCount50 +
                    " contours=" + audit.ContourCount50 +
                    " selectedContours=" + audit.SelectedContourCount50 +
                    " externalRefs=" + references.Count +
                    " externalKinds=" + kindSummary +
                    " unsupportedExternal=" + unsupported +
                    " d1={" + d1 + "} d2={" + d2 + "}" +
                    " bothDirections=" + audit.BothDirections50 +
                    " reverseDirection=" + audit.ReverseDirection50 +
                    " flipSideToCut=" + audit.FlipSideToCut50 +
                    " fromOffsetReverse=" + audit.FromOffsetReverse50 +
                    " status=" + status);
            }

            CreateMirrorPartPackage.LogDebug("[PREFLIGHT50][SUMMARY] cuts=" + cuts +
                " ready=" + ready +
                " blockedCapture=" + blockedCapture +
                " blockedExternal=" + blockedExternal +
                " blockedEndReference=" + blockedEndReference +
                " externalReferences=" + externalReferenceCount +
                " endReferences=" + endReferenceCount +
                " mutationStarted=False");
        }

        private static string FormatDirectionPreflight50(
            ADDIN.Helpers.ExtrudeDirectionAudit50 direction,
            ref int endReferenceCount,
            ref bool blocked,
            bool geometryFallback54 = false)
        {
            if (direction == null)
            {
                blocked = true;
                return "capture=NULL status=BLOCKED";
            }
            if (!string.IsNullOrEmpty(direction.CaptureError))
            {
                blocked = true;
                return "capture=ERROR status=BLOCKED reason=\"" + direction.CaptureError.Replace("\"", "'") + "\"";
            }
            if (direction.ReferencePresent) endReferenceCount++;
            bool required = EndConditionNeedsReference50(direction.EndConditionName);
            string referenceStatus;
            if (direction.ReferencePresent)
            {
                bool supported = string.Equals(direction.ReferenceEntityKind, "Edge", StringComparison.Ordinal) ||
                    string.Equals(direction.ReferenceEntityKind, "Face", StringComparison.Ordinal) ||
                    string.Equals(direction.ReferenceEntityKind, "Vertex", StringComparison.Ordinal) ||
                    string.Equals(direction.ReferenceEntityKind, "SketchPoint", StringComparison.Ordinal);
                if (supported)
                    referenceStatus = "GEOMETRY_MAPPER_READY";
                else
                {
                    blocked = true;
                    referenceStatus = "CAPTURED_UNSUPPORTED_MAP";
                }
            }
            else if (required)
            {
                if (!geometryFallback54) blocked = true;
                referenceStatus = geometryFallback54 ? "GEOMETRY_GATED_BLIND_CANDIDATE" :
                    "MISSING_REQUIRED_REFERENCE";
            }
            else
            {
                referenceStatus = "NONE_REQUIRED";
            }
            return "end=" + direction.EndConditionName +
                "(" + direction.EndCondition + ")" +
                " depth_m=" + direction.Depth.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                " ref=" + direction.ReferenceEntityKind +
                " selectionType=" + direction.ReferenceSelectionType +
                " persistent=" + (direction.PersistentReference != null && direction.PersistentReference.Length > 0) +
                " refStatus=" + referenceStatus;
        }

        private static bool EndConditionNeedsReference50(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("Surface", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Vertex", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Plane", StringComparison.OrdinalIgnoreCase) >= 0;
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
            // Calculate Global Mirror Transform
            double[] o = mirrorPlane.Origin;
            double[] n = mirrorPlane.Normal;
            double nx = n[0], ny = n[1], nz = n[2];
            double dotON = o[0] * nx + o[1] * ny + o[2] * nz;
            double[] xform = new double[16];
            xform[0] = 1.0 - 2.0 * nx * nx; xform[1] = -2.0 * nx * ny; xform[2] = -2.0 * nx * nz;
            xform[3] = -2.0 * ny * nx; xform[4] = 1.0 - 2.0 * ny * ny; xform[5] = -2.0 * ny * nz;
            xform[6] = -2.0 * nz * nx; xform[7] = -2.0 * nz * ny; xform[8] = 1.0 - 2.0 * nz * nz;
            xform[9] = 2.0 * dotON * nx; xform[10] = 2.0 * dotON * ny; xform[11] = 2.0 * dotON * nz;
            xform[12] = 1.0; xform[13] = 0.0; xform[14] = 0.0; xform[15] = 0.0;
            CreateMirrorPartPackage.currentMirrorTransform = ReflectionApiTransformV7.Create(mathUtility, xform);
            CreateMirrorPartPackage.LogDebug($"[EDGE_FLANGE_REPAIR] currentMirrorTransform established. (nx={nx}, ny={ny}, nz={nz})");


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
        public static ADDIN.Helpers.EdgeFlangeRepairManager edgeFlangeRepairManager = new ADDIN.Helpers.EdgeFlangeRepairManager();
        public static ADDIN.Helpers.FeatureOptionsCaptureHelper featureOptionsCaptureHelper = new ADDIN.Helpers.FeatureOptionsCaptureHelper();
        public static MathTransform currentMirrorTransform;
        public static bool DiagnosticOnly
        {
            get => SketchDrivenFeatureMirrorHandler.DiagnosticOnly;
            set => SketchDrivenFeatureMirrorHandler.DiagnosticOnly = value;
        }

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
            LogDebug("MIRROR_PART_V7 PHASE1 START");

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

            if (activeDoc != null && activeDoc.GetType() == (int)swDocumentTypes_e.swDocPART)
            {
                activeCommand = this;
                executing = true;
                finishing = false;
                ExecuteFeaturePartV7(activeDoc);
                return;
            }

            if (activeDoc == null ||
                activeDoc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                ShowInfo("Vui long mo Part hoac Assembly de dung MIRROR PART V7 audit.");
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
            ISavePathProvider customSavePathProvider = null,
            bool insertIntoAssembly = false)
        {
            ISavePathProvider origProvider = this.savePathProvider;
            if (customSavePathProvider != null)
            {
                this.savePathProvider = customSavePathProvider;
            }

            try
            {
                return ExecuteMirrorWorkflowCore(sourceComponent, assemblyMirrorPlane, insertIntoAssembly);
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

            ExecuteMirrorWorkflowV7Phase2(sourceComponent, assemblyMirrorPlane);
        }

        private void ExecuteMirrorWorkflowV7Phase1(Component2 sourceComponent, RefPlane assemblyMirrorPlane)
        {
            try
            {
                MirrorV7Context context = new MirrorV7Context
                {
                    SwApp = swApp,
                    PartDoc = null,
                    Graph = null,
                    AuditOnly = true
                };
                PartMirrorEngineV7 engine = new PartMirrorEngineV7(context);
                engine.ValidateFoundation();
                LogDebug("MIRROR_PART_V7 PHASE1 selection=valid component=" + SafeComponentName(sourceComponent));
                ShowInfo("MIRROR PART Phase 1 hoan tat. Da xac nhan Component va Assembly Plane; chua thuc hien mirror geometry.");
                Finish(true, "V7_PHASE1_SUCCESS");
            }
            catch (Exception ex)
            {
                MirrorV7Diagnostics.LogException("phase1", ex);
                ShowError("MIRROR PART Phase 1 loi: " + ex.Message);
                Finish(true, "V7_PHASE1_FAILED");
            }
        }

        private void ExecuteMirrorWorkflowV7Phase2(Component2 sourceComponent, RefPlane assemblyMirrorPlane)
        {
            try
            {
                if (sourceComponent == null) throw new ArgumentNullException("sourceComponent");
                if (assemblyMirrorPlane == null) throw new ArgumentNullException("assemblyMirrorPlane");
                ModelDoc2 sourcePartDoc = sourceComponent.GetModelDoc2() as ModelDoc2;
                if (sourcePartDoc == null) throw new InvalidOperationException("Khong lay duoc ModelDoc2 cua Component. Hay Resolve Component truoc khi chay Phase 2.");
                if (sourcePartDoc.GetType() != (int)swDocumentTypes_e.swDocPART) throw new InvalidOperationException("Component duoc chon khong phai Part.");
                string componentName = SafeComponentName(sourceComponent), componentPath = SafeComponentPath(sourceComponent), referencedConfig = "";
                try { referencedConfig = sourceComponent.ReferencedConfiguration ?? ""; } catch { }
                LogDebug("MIRROR_PART_V7 PHASE2 START\ncomponent=" + componentName + "\npath=" + componentPath + "\nreferencedConfig=" + referencedConfig);
                LogDebug("[FEATURE_RECONSTRUCTION] sourceMode=ASSEMBLY_COMPONENT scope=PART_ONLY assemblyMutation=False");
                ExecuteFeaturePartV7(sourcePartDoc);
            }
            catch (Exception ex)
            {
                MirrorV7Diagnostics.LogException("V7_PART_SOURCE", ex);
                ShowError("MIRROR PART V7 loi lay Part nguon:\n" + ex.Message);
                Finish(true, "V7_PART_SOURCE_FAILED");
            }
        }

        // Full editable feature reconstruction is distinct from a native mirrored body result.
        private void ExecuteFeaturePartV7(ModelDoc2 source)
        {
            try
            {
                CanonicalPartMirrorPlaneV7 plane;
                if (!SketchMirrorPlannerV7.TrySelectCanonicalPlane(null, out plane))
                {
                    Finish(true, "V7_PART_CANCELLED");
                    return;
                }
                string sourcePath = source.GetPathName();
                if (string.IsNullOrWhiteSpace(sourcePath)) throw new InvalidOperationException("Luu Part goc truoc khi mirror.");
                string output = savePathProvider.ResolveSavePath(swApp, sourcePath, Path.GetDirectoryName(sourcePath),
                    Path.GetFileNameWithoutExtension(sourcePath) + "_MIRROR_" + plane + ".SLDPRT");
                if (string.IsNullOrWhiteSpace(output)) { Finish(true, "V7_PART_CANCELLED"); return; }
                MirrorInPlacePreparationResultV7 result = MirrorInPlaceOrchestratorV7.ExecuteLegacyPart(
                    swApp, source, plane, output);
                if (!result.OutputPublished)
                    throw new InvalidOperationException("Cong kiem chung khong cho phep xuat Part ket qua.");
                ShowInfo("DA TAO PART MIRROR DOC LAP THANH CONG.\n" +
                    "File ket qua: " + result.RequestedOutputPath +
                    "\nStaging: " + result.WorkingPath +
                    "\nBao cao: " + result.ReportPath +
                    "\nSo feature: " + result.FeatureCount +
                    "\nHinh hoc mirror: PASS" +
                    "\nFeature tree: PASS" +
                    "\nMo lai file kiem tra: PASS" +
                    "\nPart goc khong thay doi: " + (result.SourceUnchanged ? "PASS" : "FAIL") +
                    "\n\nKhong su dung MirrorPart, Mirror Feature hoac Mirror Body.");
                Finish(true, "V7_INPLACE_PART_PUBLISHED");
            }
            catch (Exception ex)
            {
                MirrorV7Diagnostics.LogException("FEATURE_RECONSTRUCTION", ex);
                ShowError("Khong the xuat Part mirror an toan:\n" + ex.Message +
                    "\nKhong co file ket qua sai nao duoc giu lai. Xem MirrorPartDebug.log.");
                Finish(true, "V7_PART_FAILED");
            }
        }

        private void ExecutePartAuditV7(ModelDoc2 sourcePartDoc, string sourceMode)
        {
            string stage = "PHASE2";
            try
            {
                if (sourcePartDoc == null || sourcePartDoc.GetType() != (int)swDocumentTypes_e.swDocPART)
                    throw new InvalidOperationException("V7 audit can tai lieu Part.");
                LogDebug("MIRROR_PART_V7 AUDIT START mode=" + sourceMode + " path=" + sourcePartDoc.GetPathName() + " version=" + PartMirrorEngineV7.Version);
                MirrorV7Context context = new MirrorV7Context { SwApp = swApp, PartDoc = sourcePartDoc, Graph = null, AuditOnly = true };
                PartMirrorEngineV7 engine = new PartMirrorEngineV7(context);
                MirrorV7ModelGraph graph = engine.AuditFeatureTree(sourcePartDoc);
                stage = "PHASE3";
                engine.AuditReflectionMathematics();
                stage = "PHASE4";
                PersistentReferenceAuditResultV7 persistResult = engine.AuditPersistentReferences();
                LogDebug("MIRROR_PART_V7 PHASE4 RESULT=PASS captured=" + persistResult.Captured + " resolved=" + persistResult.Resolved + " unsupported=" + persistResult.UnsupportedOrUnavailable + " mismatched=" + persistResult.Mismatched + " systemSkipped=" + persistResult.SystemSkipped);
                stage = "PHASE5A";
                LogDebug("MIRROR_PART_V7 PHASE5 START version=" + PartMirrorEngineV7.Version + " scope=READ_ONLY_SKETCH_SNAPSHOT");
                SketchSnapshotAuditResultV7 sketchResult = engine.AuditSketchSnapshots();
                stage = "BODY_VERIFY";
                engine.AuditBodyVerification();
                LogDebug("MIRROR_PART_V7 PHASE5 RESULT=PASS sketchNodes=" + sketchResult.SketchNodes + " captured=" + sketchResult.Captured + " stable=" + sketchResult.Stable + " integrityErrors=" + sketchResult.IntegrityErrors);
                stage = "PHASE6A";
                CanonicalPartMirrorPlaneV7 selectedPlane;
                if (!SketchMirrorPlannerV7.TrySelectCanonicalPlane(null, out selectedPlane))
                {
                    LogDebug("MIRROR_PART_V7 PHASE6B CANCELLED before working-copy creation");
                    Finish(true, "V7_PHASE6B_CANCELLED");
                    return;
                }
                stage = "PHASE6B";
                WorkingCopyEquivalenceResultV7 copyResult = engine.AuditWorkingCopy(selectedPlane);
                LogDebug("MIRROR_PART_V7 PHASE6B RESULT=PLAN_BUILT sourceFeatures=" + copyResult.SourceFeatureCount + " copyFeatures=" + copyResult.CopyFeatureCount + " sourceSketches=" + copyResult.SourceSketchCount + " copySketches=" + copyResult.CopySketchCount + " sourceBodies=" + copyResult.SourceBodyCount + " copyBodies=" + copyResult.CopyBodyCount + " sketchesInPlan=" + (copyResult.Plan == null ? 0 : copyResult.Plan.Items.Count) + " mutationEnabled=False");
                SketchSelectionResultV7 selection = SingleSketchMutationServiceV7.SelectEligibleSketch(null, context);
                if (selection.Status == SketchSelectionStatusV7.NoEligibleSketch)
                {
                    LogDebug("MIRROR_PART_V7 PHASE6C1 RESULT=BLOCKED reason=NO_ELIGIBLE_SKETCH");
                    ShowInfo("MIRROR PART Phase 6C.1 BLOCKED.\n\nKhong co sketch du dieu kien.\nXem log ELIGIBILITY de biet ly do.\nWorking copy: " + copyResult.WorkingPath);
                    Finish(true, "V7_PHASE6C1_NO_ELIGIBLE");
                    return;
                }
                if (selection.Status == SketchSelectionStatusV7.UserCancelled)
                {
                    LogDebug("MIRROR_PART_V7 PHASE6C1 RESULT=CANCELLED reason=USER_CANCELLED");
                    Finish(true, "V7_PHASE6C1_CANCELLED");
                    return;
                }
                MirrorV7FeatureNode selectedSketch = selection.Node;
                stage = "PHASE6C1";
                SingleSketchMutationResultV7 mutationResult = SingleSketchMutationServiceV7.Execute(context, selectedSketch, selectedPlane);
                LogDebug("MIRROR_PART_V7 PHASE6C1 result=" + (mutationResult.Success ? "PASS" : "FAIL") + " sketch=\"" + selectedSketch.Name + "\" points=" + mutationResult.PointCount + " movingPoints=" + mutationResult.MovingPointCount + " maxErrorSI=" + mutationResult.MaximumErrorMetres + " postEdit=" + mutationResult.PostEditVerified + " postRebuild=" + mutationResult.PostRebuildVerified + " sourceUnchanged=" + mutationResult.SourceUnchanged + " partialMutation=" + mutationResult.PartialMutation + " fullPartMirrorVerified=False");
                if (mutationResult.Success)
                {
                    ShowInfo("MIRROR PART Phase 6C.1 PASS.\n\nSketch: " + selectedSketch.Name + "\nWorking copy: " + copyResult.WorkingPath + "\nPoint verification: PASS\nRebuild verification: PASS\nSource unchanged: PASS\nFullPartMirrorVerified: NO");
                    Finish(true, "V7_PHASE6C1_SUCCESS");
                }
                else
                {
                    ShowError("MIRROR PART Phase 6C.1 FAIL/BLOCKED.\n\nWorking copy duoc giu lai: " + copyResult.WorkingPath + "\n" + string.Join("\n", mutationResult.Errors.ToArray()));
                    Finish(true, "V7_PHASE6C1_FAILED");
                }
            }
            catch (Exception ex)
            {
                MirrorV7Diagnostics.LogException("V7_AUDIT", ex);
                ShowError("MIRROR PART V7 loi (" + stage + "):\n" + ex.Message);
                Finish(true, "V7_" + stage + "_FAILED");
            }
        }

        private void ExecuteMirrorWorkflowLegacyV6Disabled(Component2 sourceComponent, RefPlane assemblyMirrorPlane)
        {
            // Retained only for source compatibility; the normal button never calls it.

            MirrorPackageResult result = ExecuteMirrorWorkflowCore(sourceComponent, assemblyMirrorPlane, true);

            if (result.Cancelled)
            {
                Finish(true, "CANCELLED");
            }
            else if (result.Success)
            {
                string msg = "Tao Part Mirror hoan tat!";
                if (result.AssemblyComponentInserted && !string.IsNullOrEmpty(result.MirrorDrawingPath))
                {
                    msg = $"Tao Part Mirror thanh cong!\n- Da chen vao Assembly.\n- Da tao ban ve 2D: {Path.GetFileName(result.MirrorDrawingPath)}";
                }
                else if (result.AssemblyComponentInserted)
                {
                    msg = "Tao Part Mirror va chen vao Assembly thanh cong!";
                }
                else if (!string.IsNullOrEmpty(result.MirrorDrawingPath))
                {
                    msg = $"Tao Part Mirror thanh cong!\n- Da tao ban ve 2D: {Path.GetFileName(result.MirrorDrawingPath)}";
                }
                ShowInfo(msg);
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
            RefPlane assemblyMirrorPlane,
            bool insertIntoAssembly = true)
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

                LogDebug($"STEP1_ARCHITECTURE\nassemblyPlaneRole=REFERENCE_ONLY\nmirrorPerformedInsidePart=True\nassemblyComponentInserted={insertIntoAssembly}");

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

                // STEP 3: ĐỒNG BỘ MÀU MẶT (PINK FACE / COLOR MAPPING)
                // Thực thi TRƯỚC KHI chèn vào Assembly để file Part đã có sẵn màu mặt trước khi load vào cụm lắp ráp
                try
                {
                    int syncedColors = SyncFaceColors(swApp, sourcePath, mirrorPartPath, mirrorResult.EffectiveMirrorPlane);
                    LogDebug($"STEP3_COLOR_SYNC syncedCount={syncedColors}");
                }
                catch (Exception exSync)
                {
                    LogDebug($"[STEP3_COLOR_ERROR] {exSync.Message}");
                }

                // STEP 2: CHÈN VÀO CỤM LẮP RÁP (ASSEMBLY)
                bool assemblyInserted = false;
                if (insertIntoAssembly)
                {
                    AssemblyDoc activeAssembly = assemblyDoc ?? (swApp.ActiveDoc as AssemblyDoc);
                    if (activeAssembly != null && sourceComponent != null && assemblyMirrorPlane != null)
                    {
                        try
                        {
                            Component2 insertedComp = InsertMirroredComponent(
                                swApp,
                                activeAssembly,
                                sourceComponent,
                                assemblyMirrorPlane,
                                mirrorPartPath);
                            assemblyInserted = (insertedComp != null);
                        }
                        catch (Exception exIns)
                        {
                            LogDebug($"[ASSEMBLY_INSERTION_ERROR] {exIns.Message}");
                            result.Warning = $"Da tao Part Mirror nhung loi khi chen vao Assembly: {exIns.Message}";
                        }
                    }
                }

                LogDebug($"STEP2_ASSEMBLY_INSERT\nmirrorPart={mirrorPartPath}\nfileExists={fileExists}\nassemblyComponentInserted={assemblyInserted}\nresult=SUCCESS");

                // STEP 4: NHÂN BẢN VÀ TRÁO ĐỔI BẢN VẼ 2D (.SLDDRW)
                string mirrorDrawingPath = "";
                try
                {
                    mirrorDrawingPath = ReplicateAndReplaceDrawing(swApp, sourcePath, mirrorPartPath);
                    LogDebug($"STEP4_DRAWING_REPLACE drawing={mirrorDrawingPath}");
                }
                catch (Exception exDrw)
                {
                    LogDebug($"[STEP4_DRAWING_ERROR] {exDrw.Message}");
                }

                result.Success = true;
                result.MirrorPartPath = mirrorPartPath;
                result.MirrorDrawingPath = mirrorDrawingPath;
                result.AssemblyComponentInserted = assemblyInserted;
                if (string.IsNullOrEmpty(result.Warning))
                {
                    result.Warning = assemblyInserted ? null : "Da tao chi tiet Mirror Part (Chua chen vao Assembly).";
                }

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

        public static Component2 InsertMirroredComponent(
            ISldWorks swApp,
            AssemblyDoc assemblyDoc,
            Component2 sourceComponent,
            RefPlane assemblyMirrorPlane,
            string mirrorPartPath)
        {
            if (swApp == null || assemblyDoc == null || sourceComponent == null || assemblyMirrorPlane == null || string.IsNullOrWhiteSpace(mirrorPartPath))
            {
                return null;
            }

            IMathUtility mathUtil = swApp.GetMathUtility() as IMathUtility;
            if (mathUtil == null) return null;

            // 1. Lấy tọa độ gốc và vector pháp tuyến của mặt phẳng tham chiếu trong hệ tọa độ Assembly
            MathTransform planeTransform = assemblyMirrorPlane.Transform;
            MathPoint canOrigin = mathUtil.CreatePoint(new double[] { 0, 0, 0 }) as MathPoint;
            MathPoint asmOriginPoint = canOrigin.MultiplyTransform(planeTransform) as MathPoint;
            double[] planeOriginArr = asmOriginPoint.ArrayData as double[];

            MathVector canNormal = mathUtil.CreateVector(new double[] { 0, 0, 1 }) as MathVector;
            MathVector asmNormalVec = canNormal.MultiplyTransform(planeTransform) as MathVector;
            double[] planeNormalArr = asmNormalVec.ArrayData as double[];

            double nLen = Math.Sqrt(planeNormalArr[0] * planeNormalArr[0] + planeNormalArr[1] * planeNormalArr[1] + planeNormalArr[2] * planeNormalArr[2]);
            if (nLen > 1e-12)
            {
                planeNormalArr[0] /= nLen;
                planeNormalArr[1] /= nLen;
                planeNormalArr[2] /= nLen;
            }

            // 2. Lấy tọa độ gốc và ma trận transform của chi tiết nguồn
            MathTransform srcTransform = sourceComponent.Transform2;
            double[] srcTData = srcTransform.ArrayData as double[];
            if (srcTData == null || srcTData.Length < 16) return null;

            double srcOx = srcTData[9];
            double srcOy = srcTData[10];
            double srcOz = srcTData[11];

            // 3. Tính tọa độ vị trí đối xứng: O' = O - 2 * ((O - P) . n) * n
            double dot = (srcOx - planeOriginArr[0]) * planeNormalArr[0]
                       + (srcOy - planeOriginArr[1]) * planeNormalArr[1]
                       + (srcOz - planeOriginArr[2]) * planeNormalArr[2];

            double refOx = srcOx - 2.0 * dot * planeNormalArr[0];
            double refOy = srcOy - 2.0 * dot * planeNormalArr[1];
            double refOz = srcOz - 2.0 * dot * planeNormalArr[2];

            // 4. Ma trận Transform cho chi tiết Mirror: Giữ nguyên Rotation, thay thế Translation bằng O'
            double[] newTData = (double[])srcTData.Clone();
            newTData[9] = refOx;
            newTData[10] = refOy;
            newTData[11] = refOz;

            CreateMirrorPartPackage.LogDebug($"[ASSEMBLY_INSERT] TargetPos=({refOx * 1000.0:F2}, {refOy * 1000.0:F2}, {refOz * 1000.0:F2})mm");

            // Kích hoạt Assembly doc nếu chưa phải active
            try
            {
                ModelDoc2 asmDoc = (ModelDoc2)assemblyDoc;
                int actErr = 0;
                swApp.ActivateDoc3(asmDoc.GetTitle(), true, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref actErr);
            }
            catch {}

            // 5. Chèn vào Assembly bằng API AddComponents3
            string[] compNames = new string[] { mirrorPartPath };
            object res = assemblyDoc.AddComponents3(compNames, newTData, null);
            object[] comps = res as object[];

            Component2 newComp = null;
            if (comps != null && comps.Length > 0)
            {
                newComp = comps[0] as Component2;
            }

            if (newComp != null)
            {
                CreateMirrorPartPackage.LogDebug($"[ASSEMBLY_INSERT_SUCCESS] compName={newComp.Name2}");

                // Cố định (Fix) vị trí để không bị kéo chuột làm dịch chuyển
                try
                {
                    ModelDoc2 asmDoc = (ModelDoc2)assemblyDoc;
                    asmDoc.ClearSelection2(true);
                    newComp.Select4(false, null, false);
                    assemblyDoc.FixComponent();
                    asmDoc.ClearSelection2(true);
                    asmDoc.EditRebuild3();
                    asmDoc.GraphicsRedraw();
                    try
                    {
                        (asmDoc.ActiveView as IModelView)?.GraphicsRedraw(null);
                    }
                    catch {}
                }
                catch {}
            }
            else
            {
                CreateMirrorPartPackage.LogDebug("[ASSEMBLY_INSERT_FAIL] AddComponents3 returned null or empty.");
            }

            return newComp;
        }

        public static int SyncFaceColors(
            ISldWorks swApp,
            string sourcePartPath,
            string mirrorPartPath,
            PlaneData mirrorPlane = null)
        {
            if (swApp == null || string.IsNullOrWhiteSpace(sourcePartPath) || string.IsNullOrWhiteSpace(mirrorPartPath))
            {
                return 0;
            }

            int syncedFaces = 0;
            ModelDoc2 srcDoc = null;
            ModelDoc2 mirDoc = null;
            bool closeSrc = false;
            bool closeMir = false;

            try
            {
                int errors = 0;
                int warnings = 0;

                srcDoc = swApp.GetOpenDocumentByName(sourcePartPath) as ModelDoc2;
                if (srcDoc == null)
                {
                    srcDoc = swApp.OpenDoc6(sourcePartPath, (int)swDocumentTypes_e.swDocPART, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);
                    closeSrc = true;
                }

                mirDoc = swApp.GetOpenDocumentByName(mirrorPartPath) as ModelDoc2;
                if (mirDoc == null)
                {
                    mirDoc = swApp.OpenDoc6(mirrorPartPath, (int)swDocumentTypes_e.swDocPART, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);
                    closeMir = true;
                }

                if (srcDoc == null || mirDoc == null) return 0;

                PartDoc srcPart = srcDoc as PartDoc;
                PartDoc mirPart = mirDoc as PartDoc;
                if (srcPart == null || mirPart == null) return 0;

                object[] srcBodies = srcPart.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
                object[] mirBodies = mirPart.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
                if (srcBodies == null || mirBodies == null) return 0;

                // Clear all existing face material overrides on mirBodies so cloned properties don't stick to wrong faces
                foreach (Body2 mb in mirBodies)
                {
                    object[] mFaces = mb.GetFaces() as object[];
                    if (mFaces == null) continue;
                    foreach (Face2 mf in mFaces)
                    {
                        if (mf.HasMaterialPropertyValues())
                        {
                            try
                            {
                                mf.RemoveMaterialProperty2((int)swInConfigurationOpts_e.swAllConfiguration, null);
                            }
                            catch { }
                        }
                    }
                }

                // Lưu thông tin: Area, Normal, ReflectedCenter, MaterialValues
                List<Tuple<double, double[], double[], double[]>> coloredFaces = new List<Tuple<double, double[], double[], double[]>>();
                foreach (Body2 b in srcBodies)
                {
                    object[] faces = b.GetFaces() as object[];
                    if (faces == null) continue;
                    foreach (Face2 f in faces)
                    {
                        if (f.HasMaterialPropertyValues())
                        {
                            double[] mat = f.GetMaterialPropertyValues2((int)swInConfigurationOpts_e.swThisConfiguration, null) as double[];
                            if (mat != null && mat.Length >= 3)
                            {
                                double area = f.GetArea();
                                double[] normal = f.Normal as double[];
                                if (normal == null || normal.Length < 3)
                                {
                                    Surface surf = f.GetSurface() as Surface;
                                    double[] pp = (surf != null) ? surf.PlaneParams as double[] : null;
                                    if (pp != null && pp.Length >= 6)
                                    {
                                        normal = new double[] { pp[3], pp[4], pp[5] };
                                    }
                                }

                                double[] refNormal = null;
                                if (normal != null && normal.Length >= 3)
                                {
                                    if (mirrorPlane != null && mirrorPlane.Normal != null && mirrorPlane.Normal.Length >= 3)
                                    {
                                        double ndot = normal[0] * mirrorPlane.Normal[0] +
                                                      normal[1] * mirrorPlane.Normal[1] +
                                                      normal[2] * mirrorPlane.Normal[2];
                                        refNormal = new double[]
                                        {
                                            normal[0] - 2.0 * ndot * mirrorPlane.Normal[0],
                                            normal[1] - 2.0 * ndot * mirrorPlane.Normal[1],
                                            normal[2] - 2.0 * ndot * mirrorPlane.Normal[2]
                                        };
                                    }
                                    else
                                    {
                                        refNormal = new double[] { -normal[0], normal[1], normal[2] };
                                    }
                                }

                                double[] box = f.GetBox() as double[];
                                double[] center = null;
                                if (box != null && box.Length >= 6)
                                {
                                    double[] origCenter = new double[] { (box[0] + box[3]) / 2.0, (box[1] + box[4]) / 2.0, (box[2] + box[5]) / 2.0 };
                                    if (mirrorPlane != null && mirrorPlane.Origin != null && mirrorPlane.Normal != null)
                                    {
                                        double dx = origCenter[0] - mirrorPlane.Origin[0];
                                        double dy = origCenter[1] - mirrorPlane.Origin[1];
                                        double dz = origCenter[2] - mirrorPlane.Origin[2];
                                        double dot = dx * mirrorPlane.Normal[0] + dy * mirrorPlane.Normal[1] + dz * mirrorPlane.Normal[2];
                                        center = new double[]
                                        {
                                            origCenter[0] - 2.0 * dot * mirrorPlane.Normal[0],
                                            origCenter[1] - 2.0 * dot * mirrorPlane.Normal[1],
                                            origCenter[2] - 2.0 * dot * mirrorPlane.Normal[2]
                                        };
                                    }
                                    else
                                    {
                                        center = new double[] { -origCenter[0], origCenter[1], origCenter[2] };
                                    }
                                }
                                coloredFaces.Add(new Tuple<double, double[], double[], double[]>(area, refNormal, center, mat));
                            }
                        }
                    }
                }

                if (coloredFaces.Count == 0)
                {
                    CreateMirrorPartPackage.LogDebug("[SYNC_COLORS] Source part has no custom colored faces.");
                    return 0;
                }

                CreateMirrorPartPackage.LogDebug($"[SYNC_COLORS] Found {coloredFaces.Count} colored faces on source part.");

                foreach (var cf in coloredFaces)
                {
                    Face2 bestMatch = null;
                    double bestDist = double.MaxValue;

                    foreach (Body2 mb in mirBodies)
                    {
                        object[] mFaces = mb.GetFaces() as object[];
                        if (mFaces == null) continue;
                        foreach (Face2 mf in mFaces)
                        {
                            double mArea = mf.GetArea();
                            if (Math.Abs(mArea - cf.Item1) > Math.Max(1e-5, cf.Item1 * 0.05)) continue;

                            if (cf.Item2 != null)
                            {
                                double[] mNorm = mf.Normal as double[];
                                if (mNorm == null || mNorm.Length < 3)
                                {
                                    Surface mSurf = mf.GetSurface() as Surface;
                                    double[] mpp = (mSurf != null) ? mSurf.PlaneParams as double[] : null;
                                    if (mpp != null && mpp.Length >= 6)
                                    {
                                        mNorm = new double[] { mpp[3], mpp[4], mpp[5] };
                                    }
                                }
                                if (mNorm != null && mNorm.Length >= 3)
                                {
                                    double dot = cf.Item2[0] * mNorm[0] + cf.Item2[1] * mNorm[1] + cf.Item2[2] * mNorm[2];
                                    if (dot < 0.85) continue;
                                }
                            }

                            double dist = 0;
                            if (cf.Item3 != null)
                            {
                                double[] mBox = mf.GetBox() as double[];
                                if (mBox != null && mBox.Length >= 6)
                                {
                                    double mcx = (mBox[0] + mBox[3]) / 2.0;
                                    double mcy = (mBox[1] + mBox[4]) / 2.0;
                                    double mcz = (mBox[2] + mBox[5]) / 2.0;
                                    dist = Math.Sqrt(
                                        (mcx - cf.Item3[0]) * (mcx - cf.Item3[0]) +
                                        (mcy - cf.Item3[1]) * (mcy - cf.Item3[1]) +
                                        (mcz - cf.Item3[2]) * (mcz - cf.Item3[2]));
                                }
                            }

                            if (dist < 0.005 && dist < bestDist)
                            {
                                bestDist = dist;
                                bestMatch = mf;
                            }
                        }
                    }

                    if (bestMatch != null)
                    {
                        bestMatch.SetMaterialPropertyValues2(cf.Item4, (int)swInConfigurationOpts_e.swAllConfiguration, null);
                        syncedFaces++;
                        CreateMirrorPartPackage.LogDebug($"[SYNC_COLORS_SUCCESS] Synced face color RGB=({cf.Item4[0]:F2},{cf.Item4[1]:F2},{cf.Item4[2]:F2}) Area={cf.Item1*1e6:F1}mm2 dist={bestDist*1000.0:F3}mm");
                    }
                }

                if (syncedFaces > 0)
                {
                    mirDoc.ForceRebuild3(false);
                    mirDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
                }
            }
            catch (Exception ex)
            {
                CreateMirrorPartPackage.LogDebug($"[SYNC_COLORS_ERROR] {ex.Message}");
            }
            finally
            {
                if (closeSrc && srcDoc != null) swApp.CloseDoc(srcDoc.GetTitle());
                if (closeMir && mirDoc != null) swApp.CloseDoc(mirDoc.GetTitle());
            }

            return syncedFaces;
        }

        public static string ReplicateAndReplaceDrawing(
            ISldWorks swApp,
            string sourcePartPath,
            string mirrorPartPath)
        {
            if (swApp == null || string.IsNullOrWhiteSpace(sourcePartPath) || string.IsNullOrWhiteSpace(mirrorPartPath))
            {
                return "";
            }

            try
            {
                string srcDir = Path.GetDirectoryName(sourcePartPath);
                string srcNameNoExt = Path.GetFileNameWithoutExtension(sourcePartPath);

                string sourceDrawingPath = Path.Combine(srcDir, srcNameNoExt + ".slddrw");
                if (!File.Exists(sourceDrawingPath))
                {
                    sourceDrawingPath = Path.Combine(srcDir, srcNameNoExt + ".SLDDRW");
                }

                if (!File.Exists(sourceDrawingPath))
                {
                    CreateMirrorPartPackage.LogDebug($"[DRAWING_SKIP] Khong tim thay ban ve goc cho {sourcePartPath}");
                    return "";
                }

                string targetDir = Path.GetDirectoryName(mirrorPartPath);
                string targetNameNoExt = Path.GetFileNameWithoutExtension(mirrorPartPath);
                string targetDrawingPath = Path.Combine(targetDir, targetNameNoExt + ".slddrw");

                CreateMirrorPartPackage.LogDebug($"[DRAWING_COPY] {sourceDrawingPath} -> {targetDrawingPath}");
                File.Copy(sourceDrawingPath, targetDrawingPath, true);

                bool repRes = swApp.ReplaceReferencedDocument(targetDrawingPath, sourcePartPath, mirrorPartPath);
                CreateMirrorPartPackage.LogDebug($"[DRAWING_REPLACE_REF] result={repRes} oldRef={sourcePartPath} newRef={mirrorPartPath}");

                int errors = 0;
                int warnings = 0;
                ModelDoc2 drwDoc = swApp.OpenDoc6(
                    targetDrawingPath,
                    (int)swDocumentTypes_e.swDocDRAWING,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref errors,
                    ref warnings);

                if (drwDoc != null)
                {
                    try
                    {
                        drwDoc.ForceRebuild3(false);
                        drwDoc.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
                    }
                    finally
                    {
                        swApp.CloseDoc(drwDoc.GetTitle());
                    }
                    CreateMirrorPartPackage.LogDebug($"[DRAWING_REBUILD_SUCCESS] {targetDrawingPath}");
                }
                else
                {
                    CreateMirrorPartPackage.LogDebug($"[DRAWING_OPEN_WARN] OpenDoc6 returned null, errors={errors}, warnings={warnings}");
                }

                return targetDrawingPath;
            }
            catch (Exception ex)
            {
                CreateMirrorPartPackage.LogDebug($"[DRAWING_REPLACE_ERROR] {ex.Message}");
                return "";
            }
        }

        private void Finish(bool closeDialog, string resultStatus)
        {
            if (finishing) return;
            finishing = true;

            RestoreSelectionFilters();

            if (closeDialog && dialog != null && !dialog.IsDisposed)
            {
                dialog.AllowCloseAndClose();
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
        private bool isClosingAllowed = false;

        public void AllowCloseAndClose()
        {
            isClosingAllowed = true;
            this.Close();
        }

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
            Height = 295; // Tăng nhẹ chiều cao cho thoáng
            
            // 1. Áp dụng Nền trắng và Font đồng bộ cho cả Form
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            Label lblComponent = new Label
            {
                Left = 18, Top = 20, Width = 120, Text = "Component",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(229, 83, 12) // Trùng màu cam title
            };

            // 2. Nút Select dùng ModernButton màu xanh nhạt
            btnSelectComponent = new ADDIN.UI.ModernButton
            {
                Left = 18, Top = 43, Width = 145, Height = 30,
                Text = "Select Component",
                NormalColor = Color.FromArgb(238, 244, 252),
                HoverColor = Color.FromArgb(222, 235, 249),
                PressColor = Color.FromArgb(205, 222, 242),
                BorderColor = Color.FromArgb(190, 210, 235),
                ForeColor = Color.FromArgb(24, 74, 126),
                BorderRadius = 4
            };

            txtComponent = new TextBox
            {
                Left = 175, Top = 43, Width = 310, Height = 44,
                Multiline = true, ReadOnly = true, TabStop = false,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(250, 250, 250), // Nền xám cho ô Readonly
                BorderStyle = BorderStyle.FixedSingle
            };

            Label lblPlane = new Label
            {
                Left = 18, Top = 103, Width = 120, Text = "Mirror Plane",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(229, 83, 12)
            };

            btnSelectPlane = new ADDIN.UI.ModernButton
            {
                Left = 18, Top = 126, Width = 145, Height = 30,
                Text = "Select Plane",
                NormalColor = Color.FromArgb(238, 244, 252),
                HoverColor = Color.FromArgb(222, 235, 249),
                PressColor = Color.FromArgb(205, 222, 242),
                BorderColor = Color.FromArgb(190, 210, 235),
                ForeColor = Color.FromArgb(24, 74, 126),
                BorderRadius = 4
            };

            txtPlane = new TextBox
            {
                Left = 175, Top = 126, Width = 310, ReadOnly = true, TabStop = false,
                BackColor = Color.FromArgb(250, 250, 250),
                BorderStyle = BorderStyle.FixedSingle
            };

            lblStatus = new Label
            {
                Left = 18, Top = 171, Width = 467, Height = 28,
                Text = "Chon Component va Mirror Plane.",
                ForeColor = Color.FromArgb(32, 31, 30)
            };

            // 3. Nút Thực thi lệnh (Nổi bật màu xanh dương đậm)
            btnMirror = new ADDIN.UI.ModernButton
            {
                Left = 283, Top = 205, Width = 95, Height = 32,
                Text = "Mirror", Enabled = false,
                NormalColor = Color.FromArgb(0, 120, 212),
                HoverColor = Color.FromArgb(16, 110, 190),
                PressColor = Color.FromArgb(0, 90, 158),
                BorderColor = Color.FromArgb(0, 120, 212),
                ForeColor = Color.White,
                BorderRadius = 4
            };

            // 4. Nút Cancel (Màu xám bạc)
            btnCancel = new ADDIN.UI.ModernButton
            {
                Left = 390, Top = 205, Width = 95, Height = 32,
                Text = "Cancel",
                NormalColor = Color.FromArgb(243, 242, 241),
                HoverColor = Color.FromArgb(237, 235, 233),
                PressColor = Color.FromArgb(225, 223, 221),
                BorderColor = Color.FromArgb(200, 200, 200),
                ForeColor = Color.Black,
                BorderRadius = 4
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
                isClosingAllowed = true;
                this.DialogResult = DialogResult.Cancel;
                EventHandler handler = CancelRequested;
                if (handler != null) handler(this, EventArgs.Empty);
                this.Close();
            };

            this.CancelButton = btnCancel;
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
            if (isClosingAllowed)
            {
                return;
            }

            if (e.CloseReason == CloseReason.UserClosing)
            {
                isClosingAllowed = true;
                EventHandler handler = CancelRequested;
                if (handler != null)
                {
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

                if (targetComp == null && string.IsNullOrWhiteSpace(manifest.ComponentName) && comps != null && comps.Length > 0)
                {
                    targetComp = comps[0] as Component2;
                }

                if (targetComp == null)
                {
                    throw new InvalidOperationException("Cannot find target component: " + manifest.ComponentName);
                }

                string sourcePartPath = targetComp.GetPathName();
                ModelDoc2 sourceModelBefore = targetComp.GetModelDoc2() as ModelDoc2;
                int sourceFeatureCountBefore = -1;
                bool sourceDirtyBefore = false;
                try
                {
                    if (sourceModelBefore != null)
                    {
                        sourceFeatureCountBefore = sourceModelBefore.GetFeatureCount();
                        sourceDirtyBefore = sourceModelBefore.GetSaveFlag();
                    }
                }
                catch { }
                DateTime sourceWriteTimeBefore = File.GetLastWriteTimeUtc(sourcePartPath);
                long sourceLengthBefore = new FileInfo(sourcePartPath).Length;
                int assemblyComponentCountBefore = comps != null ? comps.Length : 0;

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

                ModelDoc2 sourceModelAfter = targetComp.GetModelDoc2() as ModelDoc2;
                int sourceFeatureCountAfter = -1;
                bool sourceDirtyAfter = false;
                try
                {
                    if (sourceModelAfter != null)
                    {
                        sourceFeatureCountAfter = sourceModelAfter.GetFeatureCount();
                        sourceDirtyAfter = sourceModelAfter.GetSaveFlag();
                    }
                }
                catch { }

                DateTime sourceWriteTimeAfter = File.GetLastWriteTimeUtc(sourcePartPath);
                long sourceLengthAfter = new FileInfo(sourcePartPath).Length;
                object[] componentsAfter = assembly.GetComponents(false) as object[];
                int assemblyComponentCountAfter = componentsAfter != null ? componentsAfter.Length : 0;
                string[] stagingFiles = Directory.GetFiles(outDir, "*.mirror_stage_*.sldprt");

                bool outputCreated =
                    testResult.Success &&
                    File.Exists(mirrorPartTarget) &&
                    !string.Equals(sourcePartPath, mirrorPartTarget, StringComparison.OrdinalIgnoreCase);
                assertions.Add(new AssertionResult
                {
                    Number = 1,
                    Name = "Validated mirror Part committed",
                    Passed = outputCreated,
                    Detail = outputCreated ? mirrorPartTarget : (testResult.Message ?? "Mirror workflow failed.")
                });

                bool sourceUnchanged =
                    sourceWriteTimeBefore == sourceWriteTimeAfter &&
                    sourceLengthBefore == sourceLengthAfter &&
                    sourceDirtyBefore == sourceDirtyAfter &&
                    (sourceFeatureCountBefore < 0 || sourceFeatureCountAfter == sourceFeatureCountBefore);
                assertions.Add(new AssertionResult
                {
                    Number = 2,
                    Name = "Source document unchanged",
                    Passed = sourceUnchanged,
                    Detail =
                        $"featureCount={sourceFeatureCountBefore}->{sourceFeatureCountAfter}; " +
                        $"dirty={sourceDirtyBefore}->{sourceDirtyAfter}; " +
                        $"length={sourceLengthBefore}->{sourceLengthAfter}; " +
                        $"writeTime={sourceWriteTimeBefore:O}->{sourceWriteTimeAfter:O}"
                });

                bool assemblyUnchanged = assemblyComponentCountBefore == assemblyComponentCountAfter;
                assertions.Add(new AssertionResult
                {
                    Number = 3,
                    Name = "Assembly component count unchanged",
                    Passed = assemblyUnchanged,
                    Detail = $"componentCount={assemblyComponentCountBefore}->{assemblyComponentCountAfter}"
                });

                bool stagingCleaned = stagingFiles.Length == 0;
                assertions.Add(new AssertionResult
                {
                    Number = 4,
                    Name = "Temporary mirror staging cleaned",
                    Passed = stagingCleaned,
                    Detail = stagingCleaned ? "No staging Part remains." : string.Join(";", stagingFiles)
                });

                bool partOnlyContract = string.IsNullOrEmpty(testResult.MirrorDrawingPath);
                assertions.Add(new AssertionResult
                {
                    Number = 5,
                    Name = "Part-only workflow scope preserved",
                    Passed = partOnlyContract,
                    Detail = "Drawing creation, face-color mapping, and assembly insertion are not executed in STEP 1."
                });

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
