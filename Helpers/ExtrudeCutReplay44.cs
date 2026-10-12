using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using ADDIN.Commands;
using ADDIN.Commands.MirrorV7;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Helpers
{
    // A failed proof is not a wrong feature direction. This exception denotes a
    // safely read but unusable BRep, not a native access violation/corrupt edit.
    public sealed class BodyValidationUnavailable69 : InvalidOperationException
    {
        public BodyValidationUnavailable69(string message) : base(message) { }
    }

    public sealed class BodyHealthSnapshot69
    {
        public int FaultCount { get; internal set; } = -1;
        public int FaceCount { get; internal set; }
        public int EdgeCount { get; internal set; }
        public string Details { get; internal set; }
        public bool IsValid { get { return FaultCount == 0; } }
        // Every fault read back as a face-face inconsistency on a Face entity.
        public bool OnlyFaceFaceContact71 { get; internal set; }
    }

    public static class BodyAuditPolicy69
    {
        public static string FirstFaultStage(int source, int copy, int reflected)
        {
            if (source < 0 || copy < 0 || reflected < 0) return "CHECK_UNAVAILABLE";
            if (source != 0) return "SOURCE_BEFORE_COPY";
            if (copy != 0) return "COPY_BEFORE_REFLECTION";
            return reflected != 0 ? "ORACLE_AFTER_REFLECTION" : "NONE";
        }

        // Diagnostic only. Neither this determinant nor sample displacement is
        // allowed to replace native body equivalence or relax a tolerance.
        public static string DescribeTransform(double[] data)
        {
            if (data == null || data.Length != 16 || data.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
                return "matrixAvailable=False";
            double s = data[12];
            double determinant = s * s * s * (data[0] * (data[4] * data[8] - data[5] * data[7]) -
                data[1] * (data[3] * data[8] - data[5] * data[6]) +
                data[2] * (data[3] * data[7] - data[4] * data[6]));
            double translation = Math.Sqrt(data[9] * data[9] + data[10] * data[10] + data[11] * data[11]);
            return "matrixAvailable=True effectiveDeterminant=" + determinant.ToString("R", CultureInfo.InvariantCulture) +
                " translationLength_m=" + translation.ToString("R", CultureInfo.InvariantCulture) +
                " matrix=[" + string.Join(",", data.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "]";
        }

        public static bool CanRunBoolean(BodyHealthSnapshot69 target, BodyHealthSnapshot69 tool)
        {
            return target != null && target.IsValid && tool != null && tool.IsValid;
        }

        // Adjacent bend regions that touch at a corner are an accepted design
        // practice; SOLIDWORKS reports them as swFaceFaceInconsistency on the
        // touching faces. Two bodies qualify only when BOTH carry nothing but
        // that fault class, in the same number. Any other fault, or a fault
        // present on one side only, stays unusable. A native Boolean is still
        // never attempted on such bodies; see NativeBodyCoincidence67.
        public static bool SameToleratedSelfContact71(BodyHealthSnapshot69 expected, BodyHealthSnapshot69 actual)
        {
            return expected != null && actual != null && expected.FaultCount > 0 &&
                expected.FaultCount == actual.FaultCount &&
                expected.OnlyFaceFaceContact71 && actual.OnlyFaceFaceContact71;
        }
    }

    public static class NativeBodyAudit69
    {
        private sealed class OracleTrace
        {
            public string Id, Label;
            public BodyHealthSnapshot69 Source, Copy, Reflected;
        }

        // Weak managed keys: diagnostics must not extend the lifetime of a COM
        // body or retain source documents, faces or native selection pointers.
        private static readonly ConditionalWeakTable<Body2, OracleTrace> traces =
            new ConditionalWeakTable<Body2, OracleTrace>();

        public static BodyHealthSnapshot69 Read(Body2 body, string label, string stage)
        {
            if (body == null) throw new BodyValidationUnavailable69("BODY69 missing body: " + label + " stage=" + stage);
            var faults = body.Check3;
            int count = faults == null ? 0 : faults.Count;
            var details = new List<string>();
            bool onlyContact71 = count > 0 && count <= 32;
            for (int i = 0; i < Math.Min(count, 32); i++)
            {
                int error = faults.get_ErrorCode(i);
                object entity = faults.get_Entity2(i);
                onlyContact71 &= error == (int)swFaultEntityErrorCode_e.swFaceFaceInconsistency && entity is Face2;
                string description = "kind=" + (entity is Face2 ? "Face" : entity is Edge ? "Edge" :
                    entity is Vertex ? "Vertex" : entity is Body2 ? "Body" : "Other");
                var face = entity as Face2;
                if (face != null)
                {
                    var surface = face.GetSurface() as Surface;
                    var owner = face.GetFeature() as Feature;
                    var box = face.GetBox() as double[];
                    description += " surface=" + (surface == null ? "NONE" : surface.Identity().ToString(CultureInfo.InvariantCulture)) +
                        " owner=" + (owner == null ? "NONE" : owner.Name + ":" + owner.GetTypeName2()) +
                        " box=[" + (box == null ? "" : string.Join(",", box.Select(v => v.ToString("R", CultureInfo.InvariantCulture)))) + "]";
                }
                details.Add(i + ":" + error + ":" + (swFaultEntityErrorCode_e)error + ":" + description);
            }
            if (count > 32) details.Add("remaining=" + (count - 32));
            var result = new BodyHealthSnapshot69 { FaultCount = count, FaceCount = body.GetFaceCount(),
                EdgeCount = body.GetEdgeCount(), Details = count == 0 ? "NONE" : string.Join(";", details),
                OnlyFaceFaceContact71 = onlyContact71 };
            CreateMirrorPartPackage.LogDebug("[BODY69][HEALTH] label=" + label + " stage=" + stage +
                " faults=" + result.FaultCount + " faces=" + result.FaceCount + " edges=" + result.EdgeCount +
                " temporary=" + body.IsTemporaryBody() + " details={" + result.Details + "} geometryModified=False");
            return result;
        }

        public static Body2 CopyAndReflect(Body2 source, MathTransform transform, string label)
        {
            var trace = new OracleTrace { Id = Guid.NewGuid().ToString("N"), Label = label };
            string correlation = label + "/trace=" + trace.Id;
            trace.Source = Read(source, correlation, "SOURCE_BEFORE_COPY");
            Body2 copy = source.Copy2(false) as Body2;
            if (copy == null) throw new BodyValidationUnavailable69("BODY69 Copy2 returned null: " + correlation);
            trace.Copy = Read(copy, correlation, "COPY_BEFORE_REFLECTION");
            if (transform == null || !copy.ApplyTransform(transform))
                throw new BodyValidationUnavailable69("BODY69 reflection failed: " + correlation);
            trace.Reflected = Read(copy, correlation, "ORACLE_AFTER_REFLECTION");
            traces.Add(copy, trace);
            CreateMirrorPartPackage.LogDebug("[BODY69][ORACLE_STAGES] label=" + label + " trace=" + trace.Id +
                " sourceFaults=" + trace.Source.FaultCount + " copyFaults=" + trace.Copy.FaultCount +
                " reflectedFaults=" + trace.Reflected.FaultCount + " firstFaultStage=" +
                BodyAuditPolicy69.FirstFaultStage(trace.Source.FaultCount, trace.Copy.FaultCount, trace.Reflected.FaultCount) +
                " " + BodyAuditPolicy69.DescribeTransform(transform.ArrayData as double[]) + " outputFeatureInserted=False");
            string firstFault = BodyAuditPolicy69.FirstFaultStage(
                trace.Source.FaultCount, trace.Copy.FaultCount, trace.Reflected.FaultCount);
            if (firstFault != "NONE" && BodyAuditPolicy69.SameToleratedSelfContact71(trace.Source, trace.Copy) &&
                BodyAuditPolicy69.SameToleratedSelfContact71(trace.Source, trace.Reflected))
            {
                CreateMirrorPartPackage.LogDebug("[BODY71][TOLERATED_SOURCE_SELF_CONTACT] label=" + label + " trace=" + trace.Id +
                    " faults=" + trace.Source.FaultCount + " class=swFaceFaceInconsistency booleanProof=UNAVAILABLE" +
                    " requiredProof=NATIVE_COINCIDENCE_IDENTITY");
                return copy;
            }
            if (firstFault != "NONE")
                throw new BodyValidationUnavailable69("BODY69 source oracle is unusable: " + correlation +
                    " firstFaultStage=" + firstFault + " sourceFaults=" + trace.Source.FaultCount +
                    " copyFaults=" + trace.Copy.FaultCount + " reflectedFaults=" + trace.Reflected.FaultCount +
                    "; replay not attempted for this checkpoint; no direction conclusion; output not published.");
            return copy;
        }

        public static void RequireCheckpoint(Body2 expected, Body2 actual, string label)
        {
            OracleTrace trace;
            if (traces.TryGetValue(expected, out trace))
                CreateMirrorPartPackage.LogDebug("[BODY69][CHECKPOINT_ORIGIN] label=" + label + " trace=" + trace.Id +
                    " sourceLabel=" + trace.Label + " firstFaultStage=" +
                    BodyAuditPolicy69.FirstFaultStage(trace.Source.FaultCount, trace.Copy.FaultCount, trace.Reflected.FaultCount));
            var expectedHealth = Read(expected, label, "VERIFY_REFLECTED_ORACLE");
            var actualHealth = Read(actual, label, "VERIFY_NATIVE_RESULT");
            if (BodyAuditPolicy69.CanRunBoolean(expectedHealth, actualHealth)) return;
            if (BodyAuditPolicy69.SameToleratedSelfContact71(expectedHealth, actualHealth))
            {
                CreateMirrorPartPackage.LogDebug("[BODY71][TOLERATED_SELF_CONTACT] label=" + label + " faults=" +
                    expectedHealth.FaultCount + " expectedAndActualSameClass=True booleanWillBeSkipped=True");
                return;
            }
            string origin = traces.TryGetValue(expected, out trace) ?
                BodyAuditPolicy69.FirstFaultStage(trace.Source.FaultCount, trace.Copy.FaultCount, trace.Reflected.FaultCount) : "NOT_CAPTURED";
            string message = "BODY69 verification unavailable: label=" + label + " oracleOrigin=" + origin +
                " expectedFaults=" + expectedHealth.FaultCount + " actualFaults=" + actualHealth.FaultCount +
                "; invalid inputs are not a direction mismatch; Boolean not attempted; output not published.";
            CreateMirrorPartPackage.LogDebug("[BODY69][VERIFICATION_UNAVAILABLE] " + message);
            throw new BodyValidationUnavailable69(message);
        }

        public static bool CheckBooleanInputs(Body2 target, Body2 tool, string label, string stage, BodyBooleanResult result)
        {
            var targetHealth = Read(target, label, stage + "_TARGET");
            var toolHealth = Read(tool, label, stage + "_TOOL");
            if (BodyAuditPolicy69.CanRunBoolean(targetHealth, toolHealth)) return true;
            result.Success = false;
            result.VerificationUnavailable69 = true;
            result.ToleratedSelfContact71 = BodyAuditPolicy69.SameToleratedSelfContact71(targetHealth, toolHealth);
            // Application sentinel, explicitly NOT a fictitious native 1058.
            result.ErrorCode = -6901;
            result.ErrorMessage = "BODY69 invalid Boolean inputs at " + stage +
                ": targetFaults=" + targetHealth.FaultCount + " toolFaults=" + toolHealth.FaultCount;
            CreateMirrorPartPackage.LogDebug("[BODY69][BOOLEAN_SKIPPED] label=" + label + " stage=" + stage +
                " targetFaults=" + targetHealth.FaultCount + " toolFaults=" + toolHealth.FaultCount +
                " nativeOperationCalled=False errorCode=-6901 success=False failureAcceptedAsEmpty=False");
            return false;
        }
    }

    // A typed native cut cannot add material. Full source checkpoints are its
    // oracle evidence; local delta solids are needed only by qualified fallback.
    // No part name, sketch frame, end condition or candidate direction is used here.
    public static class NativeCutCachePolicy67
    {
        public static bool CanUse(bool suppressed, bool extrudeDefinition,
            bool bossFeature, bool supportedCutType, bool completeRecipe)
        {
            return !suppressed && extrudeDefinition && !bossFeature &&
                supportedCutType && completeRecipe;
        }

        public static bool CanUsePattern(bool suppressed, bool typedPattern, bool replayHandler)
        {
            return !suppressed && typedPattern && replayHandler;
        }

        public static bool IsPositiveFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0.0;
        }

        public static bool HasRequiredSourceEvidence(bool fullCheckpoints, bool removedRequired,
            bool removedAttempted, bool removedSuccess, int removedCount, double removedVolume)
        {
            return fullCheckpoints && (!removedRequired ||
                (removedAttempted && removedSuccess && removedCount > 0 && IsPositiveFinite(removedVolume)));
        }

        public static bool IsExactDifference(bool missingSuccess, int missingCount,
            bool extraSuccess, int extraCount)
        {
            // A Boolean failure is NOT an empty difference. Nonempty residuals
            // cannot be accepted merely because their mass is below a tolerance.
            return missingSuccess && extraSuccess && missingCount == 0 && extraCount == 0;
        }

        public static bool CanComplete(bool requested, bool replaySucceeded,
            bool beforeExact, bool afterExact)
        {
            return requested && replaySucceeded && beforeExact && afterExact;
        }
    }

    // Official geometric congruence API, restricted to identity in the original
    // coordinate frame. This is a second proof, NOT treating failed Booleans as empty.
    // https://help.solidworks.com/2023/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IBody2~GetCoincidenceTransform2.html
    public static class NativeBodyCoincidence67
    {
        public static bool IsIdentity(double[] data)
        {
            if (data == null || data.Length != 16 || data.Any(v => double.IsNaN(v) || double.IsInfinity(v))) return false;
            const double tolerance = 1e-10;
            for (int i = 0; i < 9; i++)
                if (Math.Abs(data[i] * data[12] - (i == 0 || i == 4 || i == 8 ? 1.0 : 0.0)) > tolerance) return false;
            return Math.Abs(data[9]) <= tolerance && Math.Abs(data[10]) <= tolerance && Math.Abs(data[11]) <= tolerance;
        }

        public static bool CanRequest(bool missingSuccess, int missingCount, int missingError,
            bool extraSuccess, int extraCount, int extraError)
        {
            int booleanFailure = (int)swBodyOperationError_e.swBodyOperationBooleanFail;
            return (!missingSuccess || !extraSuccess) &&
                (missingSuccess ? missingCount == 0 : missingError == booleanFailure) &&
                (extraSuccess ? extraCount == 0 : extraError == booleanFailure);
        }

        public static bool Confirm(Body2 expected, Body2 actual, string label)
        {
            if (expected == null || actual == null || expected.GetType() != actual.GetType() ||
                expected.GetFaceCount() != actual.GetFaceCount() || expected.GetEdgeCount() != actual.GetEdgeCount()) return false;
            MathTransform forward, backward;
            bool forwardShape = expected.GetCoincidenceTransform2(actual, out forward);
            bool backwardShape = actual.GetCoincidenceTransform2(expected, out backward);
            CreateMirrorPartPackage.LogDebug("[COINCIDENCE69][TRANSFORM] label=" + label + " direction=ACTUAL_TO_EXPECTED " +
                BodyAuditPolicy69.DescribeTransform(forward == null ? null : forward.ArrayData as double[]) + " diagnosticOnly=True");
            CreateMirrorPartPackage.LogDebug("[COINCIDENCE69][TRANSFORM] label=" + label + " direction=EXPECTED_TO_ACTUAL " +
                BodyAuditPolicy69.DescribeTransform(backward == null ? null : backward.ArrayData as double[]) + " diagnosticOnly=True");
            bool forwardIdentity = forwardShape && forward != null && IsIdentity(forward.ArrayData as double[]);
            bool backwardIdentity = backwardShape && backward != null && IsIdentity(backward.ArrayData as double[]);
            bool pass = forwardIdentity && backwardIdentity;
            CreateMirrorPartPackage.LogDebug("[COINCIDENCE67][GEOMETRY_PROOF] label=" + label +
                " forwardShape=" + forwardShape + " backwardShape=" + backwardShape +
                " forwardIdentity=" + forwardIdentity + " backwardIdentity=" + backwardIdentity +
                " booleanFailureAcceptedAsEmpty=False tolerance_m=1e-10 result=" + (pass ? "PASS" : "FAIL"));
            return pass;
        }

        // Set by the mapped replay engine once a Fold/Unfold feature of the
        // Part being mirrored has been replayed; cleared at the next capture.
        public static bool RefoldResidualAllowed71;

        // A natively re-folded bend is not bit-identical to the reflected
        // source: both Booleans succeed but leave slivers where cuts cross the
        // bend (native observation: 102 slivers totalling 0.03 mm3 on a
        // 447 000 mm3 body, every vertex coincident). Accepted only for a Part
        // that contains Fold/Unfold, and only with ALL of: equal face and edge
        // counts, every vertex matched one-to-one within 1e-7 m, and a bounded
        // sliver volume (see the limits below).
        public static bool ConfirmRefoldResidual71(Body2 expected, Body2 actual,
            BodyBooleanResult missing, BodyBooleanResult extra, string label)
        {
            if (!RefoldResidualAllowed71 || expected == null || actual == null || missing == null || extra == null ||
                !missing.Success || !extra.Success || missing.Bodies == null || extra.Bodies == null) return false;
            bool topology = expected.GetFaceCount() == actual.GetFaceCount() && expected.GetEdgeCount() == actual.GetEdgeCount();
            bool vertices = false;
            if (topology)
            {
                var a = (expected.GetVertices() as object[] ?? new object[0]).Cast<Vertex>().Select(v => v.GetPoint() as double[]).ToList();
                var b = (actual.GetVertices() as object[] ?? new object[0]).Cast<Vertex>().Select(v => v.GetPoint() as double[]).ToList();
                vertices = a.Count == b.Count;
                foreach (double[] p in a)
                {
                    if (!vertices) break;
                    int index = b.FindIndex(q => Math.Abs(p[0] - q[0]) <= 1e-7 && Math.Abs(p[1] - q[1]) <= 1e-7 && Math.Abs(p[2] - q[2]) <= 1e-7);
                    if (index < 0) vertices = false; else b.RemoveAt(index);
                }
            }
            double volume = ((double[])expected.GetMassProperties(1.0))[3];
            double residual = missing.Bodies.Concat(extra.Bodies).Sum(x => ((double[])x.GetMassProperties(1.0))[3]);
            // The slivers live on the re-folded (curved) faces. Expressed as a
            // mean thickness over those faces, the deviation must stay within
            // the 1e-7 m point tolerance used by every other gate; a 1e-5
            // share of the body volume is an additional hard cap.
            double curvedArea = (expected.GetFaces() as object[] ?? new object[0]).Cast<Face2>()
                .Where(f => ((Surface)f.GetSurface()).Identity() != (int)swSurfaceTypes_e.PLANE_TYPE).Sum(f => f.GetArea());
            double thickness = curvedArea > 0 ? residual / curvedArea : double.NaN;
            // Thickest single sliver (2 x volume / its own skin): a mean cannot see a
            // local error such as one hole wall. Same 5e-6 m limit as the surface-chain proof.
            double thickestPiece = 0;
            foreach (Body2 piece in missing.Bodies.Concat(extra.Bodies))
            {
                double pieceSkin = (piece.GetFaces() as object[] ?? new object[0]).Cast<Face2>().Sum(f => f.GetArea());
                if (pieceSkin > 0) thickestPiece = Math.Max(thickestPiece, 2 * ((double[])piece.GetMassProperties(1.0))[3] / pieceSkin);
            }
            bool pass = topology && vertices && volume > 0 && thickness <= 1e-7 && residual <= volume * 1e-5 && thickestPiece <= 5e-6;
            CreateMirrorPartPackage.LogDebug("[BODY71][REFOLD_RESIDUAL] label=" + label + " topologyEqual=" + topology +
                " verticesEqual_1e-7=" + vertices + " slivers=" + (missing.Bodies.Count + extra.Bodies.Count) +
                " residual_mm3=" + (residual * 1e9).ToString("F4", CultureInfo.InvariantCulture) +
                " curvedArea_mm2=" + (curvedArea * 1e6).ToString("F1", CultureInfo.InvariantCulture) +
                " meanThickness_m=" + thickness.ToString("E2", CultureInfo.InvariantCulture) +
                " thickestPiece_m=" + thickestPiece.ToString("E2", CultureInfo.InvariantCulture) +
                " relativeVolume=" + (volume > 0 ? residual / volume : double.NaN).ToString("E2", CultureInfo.InvariantCulture) +
                " limits=thickness1E-7,volume1E-5 result=" + (pass ? "PASS" : "FAIL"));
            return pass;
        }

        // Set once an in-context Offset Surface chain has been re-attached to a
        // fixed reflected surface in this run; cleared at the next capture.
        public static bool SurfaceChainResidualAllowed71;

        // User decision (option A1): a Part whose cut surface hangs from an
        // in-context Offset Surface keeps that surface chain editable. Native
        // Surface-Extend builds a corner of the reflected surface differently
        // (12 vertices against 11, every edge order tried), and where the
        // surface border runs along the border of the sheet this leaves a
        // sliver: natively one extra vertex and corners 1.4-2.9e-6 m from the
        // reflected source. Accepted only for such a Part and only with ALL of:
        // both Boolean differences computed; every vertex of either body within
        // 5e-6 m of a vertex of the other; total difference at most 5e-7 m as
        // a mean thickness over the body skin and at most 5e-4 of its volume
        // (natively, after Unfold: all vertices equal to 1e-14 m, six slivers
        // of 3.6 mm3 together, 1.6e-8 m mean thickness).
        // Native area integration of a re-folded curved face moved by up to
        // 1.6e-4 between two bodies with identical topology and vertices (a
        // 358 000 mm2 face of 44 m radius); planar faces are exact to 1e-4.
        // Faces are paired by area alone (sorted): a natively re-folded face can
        // come back as another surface type than the reflected source face.
        private static double FaceAreaNoise71(double surfaceType)
        {
            return surfaceType == (int)swSurfaceTypes_e.PLANE_TYPE ? 1e-4 : 5e-4;
        }

        // User decision of 2026-10-07 for the same Part classes (in-context surface
        // chain, Lofted Bend base re-attached to reflected geometry): a local
        // difference of up to 0.1 mm against the reflected source is accepted. Slivers
        // stay under 5e-6 m; anything thicker is measured skin against skin, and the
        // largest value a run accepted is kept for the log.
        // Second decision, same day: from the first re-Fold of the run onward up to
        // 0.2 mm. The Fold of SOLIDWORKS itself is not mirror-symmetric: on a Part
        // whose Lofted Bend matched the mirror image to 1e-9 m and whose every
        // feature in the flat state matched exactly, the folded result lay 0.149 mm
        // from the mirror image inside one curved face (150 mm3, 1.1e-3 of the
        // volume). The flat pattern is not affected. The mean and volume limits of
        // the reduced proof follow: 5e-6 m and 5e-3 after a Fold, 5e-7 m and 5e-4 before.
        public static bool FoldReplayed71;
        public static double LocalDeviationLimit71 { get { return FoldReplayed71 ? 2e-4 : 1e-4; } }
        private static double MeanDeviationLimit71 { get { return FoldReplayed71 ? 5e-6 : 5e-7; } }
        private static double VolumeShareLimit71 { get { return FoldReplayed71 ? 5e-3 : 5e-4; } }
        public static double LargestAcceptedDeviation71;

        // Third decision, 2026-10-08: the 0.1 mm limit holds for every Part, not only
        // for the two classes above ("lệch 0.1mm vẫn ok"; the case was a plain sheet
        // whose mirrored body lay 3.72e-6 m from the mirror image after a cut - a
        // difference the Boolean comparison passes unseen in most places and reports
        // where a sliver stands alone). This reduced proof therefore runs for any
        // Part once the exact proofs have failed; its evidence and limits are
        // unchanged, and what it accepts is logged and kept as before.
        public static bool ConfirmSurfaceChainResidual71(Body2 expected, Body2 actual, string label)
        {
            if (expected == null || actual == null) return false;
            const double reach = 5e-6;
            var a = (expected.GetVertices() as object[] ?? new object[0]).Cast<Vertex>().Select(v => v.GetPoint() as double[]).ToList();
            var b = (actual.GetVertices() as object[] ?? new object[0]).Cast<Vertex>().Select(v => v.GetPoint() as double[]).ToList();
            Func<List<double[]>, List<double[]>, double> farthest = (from, to) => from.Count == 0 || to.Count == 0 ? double.PositiveInfinity
                : from.Max(p => to.Min(q => Math.Sqrt((p[0] - q[0]) * (p[0] - q[0]) + (p[1] - q[1]) * (p[1] - q[1]) + (p[2] - q[2]) * (p[2] - q[2]))));
            double forward = farthest(a, b), backward = farthest(b, a);
            double volume = ((double[])expected.GetMassProperties(1.0))[3], residual = double.NaN;
            bool booleans = false, cornersNotPaired = false;
            double thickestPiece = 0, skinDeviation = double.NaN;
            skinDeviationAt71 = null;
            int skinSamples = 0, skinSamplesBack = 0;
            string evidence = "BOTH_DIFFERENCES";
            if (forward <= reach && backward <= reach)
            {
                BodyBooleanResult missing = BodyOperationsHelper.BooleanCutStrict(expected, actual, label + "_CHAIN71_MISSING");
                BodyBooleanResult extra = BodyOperationsHelper.BooleanCutStrict(actual, expected, label + "_CHAIN71_EXTRA");
                bool missingDone = missing.Success && missing.Bodies != null, extraDone = extra.Success && extra.Bodies != null;
                // Thickest single difference piece (2 x volume / its own skin). The mean over
                // the whole body hides a local error: natively a 6 mm hole whose wall came
                // out differently after Fold left pieces up to 69e-6 m thick while the mean
                // was 1e-8 m.
                foreach (Body2 piece in (missingDone ? missing.Bodies : new List<Body2>()).Concat(extraDone ? extra.Bodies : new List<Body2>()))
                {
                    double pieceSkin = (piece.GetFaces() as object[] ?? new object[0]).Cast<Face2>().Sum(f => f.GetArea());
                    double pieceVolume = ((double[])piece.GetMassProperties(1.0))[3];
                    if (pieceSkin > 0) thickestPiece = Math.Max(thickestPiece, 2 * pieceVolume / pieceSkin);
                }
                booleans = missingDone && extraDone;
                if (booleans) residual = missing.Bodies.Concat(extra.Bodies).Sum(x => ((double[])x.GetMassProperties(1.0))[3]);
                else if (!missingDone && !extraDone)
                {
                    // Natively, after Fold of a Part with many cuts, the kernel
                    // computes neither difference of the two near-coincident
                    // bodies. Without any Boolean the proof is: equal face and
                    // edge counts and vertex counts, every face paired by surface
                    // type and area (1e-4), volumes within 5e-4, and every sample
                    // point of every edge of the reflected source body lying on
                    // the skin of the result within 5e-6 m.
                    var fa = (expected.GetFaces() as object[] ?? new object[0]).Cast<Face2>().Select(f => new[] { (double)((Surface)f.GetSurface()).Identity(), f.GetArea() }).OrderBy(f => f[1]).ToList();
                    var actualFaces = (actual.GetFaces() as object[] ?? new object[0]).Cast<Face2>().ToList();
                    var fb = actualFaces.Select(f => new[] { (double)((Surface)f.GetSurface()).Identity(), f.GetArea() }).OrderBy(f => f[1]).ToList();
                    bool faces = fa.Count == fb.Count && expected.GetEdgeCount() == actual.GetEdgeCount() && a.Count == b.Count &&
                        Enumerable.Range(0, fa.Count).All(i => Math.Abs(fa[i][1] - fb[i][1]) <= Math.Max(1e-12, fa[i][1] * FaceAreaNoise71(fa[i][0] == fb[i][0] ? fa[i][0] : -1)));
                    double other = ((double[])actual.GetMassProperties(1.0))[3];
                    double offSkin = double.PositiveInfinity; int samples = 0;
                    if (faces && Math.Abs(other - volume) <= volume * 5e-4)
                    {
                        offSkin = 0;
                        try
                        {
                            foreach (Edge edge in (expected.GetEdges() as object[] ?? new object[0]).Cast<Edge>())
                                foreach (double[] p in SketchOperationsHelper.SampleEdge24(edge))
                                {
                                    samples++;
                                    offSkin = Math.Max(offSkin, actualFaces.Min(f =>
                                    {
                                        var near = f.GetClosestPointOn(p[0], p[1], p[2]) as double[];
                                        return near == null || near.Length < 3 ? double.PositiveInfinity
                                            : Math.Sqrt((near[0] - p[0]) * (near[0] - p[0]) + (near[1] - p[1]) * (near[1] - p[1]) + (near[2] - p[2]) * (near[2] - p[2]));
                                    }));
                                    if (offSkin > reach) break;
                                }
                        }
                        catch (InvalidOperationException) { offSkin = double.PositiveInfinity; }
                    }
                    int firstDifferent = fa.Count != fb.Count ? -1 : Enumerable.Range(0, fa.Count).FirstOrDefault(i => Math.Abs(fa[i][1] - fb[i][1]) > Math.Max(1e-12, fa[i][1] * FaceAreaNoise71(fa[i][0] == fb[i][0] ? fa[i][0] : -1)));
                    CreateMirrorPartPackage.LogDebug("[BODY71][SURFACE_CHAIN_FACES] label=" + label + " faces=" + fa.Count + "/" + fb.Count + " edges=" + expected.GetEdgeCount() + "/" + actual.GetEdgeCount() +
                        " vertices=" + a.Count + "/" + b.Count + " types=" + string.Join(",", fa.GroupBy(f => f[0]).Select(g => g.Key + "x" + g.Count())) + " | " + string.Join(",", fb.GroupBy(f => f[0]).Select(g => g.Key + "x" + g.Count())) +
                        (fa.Count == fb.Count ? " firstDifferent=" + firstDifferent + " expected=" + fa[firstDifferent][0] + ":" + (fa[firstDifferent][1] * 1e6).ToString("F4", CultureInfo.InvariantCulture) +
                            " actual=" + fb[firstDifferent][0] + ":" + (fb[firstDifferent][1] * 1e6).ToString("F4", CultureInfo.InvariantCulture) : ""));
                    CreateMirrorPartPackage.LogDebug("[BODY71][SURFACE_CHAIN_NO_BOOLEAN] label=" + label + " facesPaired=" + faces +
                        " volumeRelative=" + (Math.Abs(other - volume) / volume).ToString("E2", CultureInfo.InvariantCulture) +
                        " edgeSamples=" + samples + " farthestOffSkin_m=" + offSkin.ToString("E2", CultureInfo.InvariantCulture));
                    if (offSkin <= reach)
                    {
                        booleans = true;
                        evidence = "NO_DIFFERENCE_COMPUTABLE_EDGES_ON_SKIN_PLUS_FACE_PAIRING";
                        residual = 0;
                    }
                    else if (!faces && fa.Count == fb.Count && expected.GetEdgeCount() == actual.GetEdgeCount() && a.Count == b.Count)
                    {
                        // Same topology, faces not paired by area (natively 6 mm hole walls of
                        // 37.72 against 37.91 mm2 after Fold) and no difference computable: the
                        // two skins are measured against each other, and the difference of the
                        // two volumes stands in for the difference volume (limits at the end).
                        skinDeviation = Math.Max(SkinDeviation71(expected, actual, LocalDeviationLimit71 * 10, out skinSamples),
                            SkinDeviation71(actual, expected, LocalDeviationLimit71 * 10, out skinSamplesBack));
                        CreateMirrorPartPackage.LogDebug("[BODY71][SURFACE_CHAIN_SKIN] label=" + label + " skinDeviation_m=" + skinDeviation.ToString("E2", CultureInfo.InvariantCulture) +
                            " samples=" + skinSamples + "/" + skinSamplesBack + " volumeRelative=" + (Math.Abs(other - volume) / volume).ToString("E2", CultureInfo.InvariantCulture) +
                            " expectedVolume_mm3=" + (volume * 1e9).ToString("F3", CultureInfo.InvariantCulture) + " actualVolume_mm3=" + (other * 1e9).ToString("F3", CultureInfo.InvariantCulture));
                        if (skinDeviation <= LocalDeviationLimit71)
                        {
                            booleans = true;
                            evidence = "NO_DIFFERENCE_COMPUTABLE_SKIN_DEVIATION_PLUS_VOLUME";
                            residual = Math.Abs(other - volume);
                        }
                    }
                }
                else if (missingDone || extraDone)
                {
                    // Natively, after Fold, one difference of two near-coincident
                    // re-folded bodies is computed (slivers) and the other one
                    // fails inside the kernel. The computed one then counts
                    // twice, and the bodies must in addition have equal face
                    // and edge counts, every face paired by surface type and
                    // area (1e-4), and volumes within 5e-4.
                    var fa = (expected.GetFaces() as object[] ?? new object[0]).Cast<Face2>().Select(f => new[] { (double)((Surface)f.GetSurface()).Identity(), f.GetArea() }).OrderBy(f => f[1]).ToList();
                    var fb = (actual.GetFaces() as object[] ?? new object[0]).Cast<Face2>().Select(f => new[] { (double)((Surface)f.GetSurface()).Identity(), f.GetArea() }).OrderBy(f => f[1]).ToList();
                    bool faces = fa.Count == fb.Count && expected.GetEdgeCount() == actual.GetEdgeCount() &&
                        Enumerable.Range(0, fa.Count).All(i => Math.Abs(fa[i][1] - fb[i][1]) <= Math.Max(1e-12, fa[i][1] * FaceAreaNoise71(fa[i][0] == fb[i][0] ? fa[i][0] : -1)));
                    double other = ((double[])actual.GetMassProperties(1.0))[3];
                    CreateMirrorPartPackage.LogDebug("[BODY71][SURFACE_CHAIN_FACE_LIST] label=" + label + " computedDifference_mm3=" +
                        ((missingDone ? missing.Bodies : extra.Bodies).Sum(x => ((double[])x.GetMassProperties(1.0))[3]) * 1e9).ToString("F4", CultureInfo.InvariantCulture) +
                        " pieces=" + string.Join(" ", (missingDone ? missing.Bodies : extra.Bodies).Select(x =>
                        {
                            var m = (double[])x.GetMassProperties(1.0);
                            double skinOfPiece = (x.GetFaces() as object[] ?? new object[0]).Cast<Face2>().Sum(f => f.GetArea());
                            return "[v_mm3=" + (m[3] * 1e9).ToString("F4", CultureInfo.InvariantCulture) + " at_mm=" + (m[0] * 1e3).ToString("F1", CultureInfo.InvariantCulture) + "," +
                                (m[1] * 1e3).ToString("F1", CultureInfo.InvariantCulture) + "," + (m[2] * 1e3).ToString("F1", CultureInfo.InvariantCulture) +
                                " thickness_um=" + (skinOfPiece > 0 ? 2 * m[3] / skinOfPiece * 1e6 : double.NaN).ToString("F3", CultureInfo.InvariantCulture) + "]";
                        })) +
                        " expected=" + string.Join(" ", fa.Select(f => f[0] + ":" + (f[1] * 1e6).ToString("F3", CultureInfo.InvariantCulture))) +
                        " | actual=" + string.Join(" ", fb.Select(f => f[0] + ":" + (f[1] * 1e6).ToString("F3", CultureInfo.InvariantCulture))));
                    CreateMirrorPartPackage.LogDebug("[BODY71][SURFACE_CHAIN_ONE_SIDED] label=" + label + " missingDone=" + missingDone + " extraDone=" + extraDone +
                        " facesPaired=" + faces + " faceCounts=" + fa.Count + "/" + fb.Count + " edges=" + expected.GetEdgeCount() + "/" + actual.GetEdgeCount() +
                        " volumeRelative=" + (Math.Abs(other - volume) / volume).ToString("E2", CultureInfo.InvariantCulture) +
                        " worstFaceArea=" + (fa.Count == fb.Count && fa.Count > 0 ? Enumerable.Range(0, fa.Count).Max(i => Math.Abs(fa[i][1] - fb[i][1]) / Math.Max(1e-12, fa[i][1])) : double.NaN).ToString("E2", CultureInfo.InvariantCulture));
                    // Volume cap 5e-4 here: on the re-folded 44 m radius spline faces the
                    // native mass integration itself moved by 2.2e-4 (and face areas by
                    // 1e-4) between two bodies whose vertices agree to 1e-13 m.
                    if (faces && a.Count == b.Count && Math.Abs(other - volume) <= volume * 5e-4)
                    {
                        booleans = true;
                        evidence = "ONE_DIFFERENCE_PLUS_FACE_PAIRING";
                        residual = 2 * (missingDone ? missing.Bodies : extra.Bodies).Sum(x => ((double[])x.GetMassProperties(1.0))[3]);
                    }
                    else if (fa.Count == fb.Count && expected.GetEdgeCount() == actual.GetEdgeCount() && a.Count == b.Count &&
                        Math.Abs(other - volume) <= volume * 5e-4)
                    {
                        // Same topology, a face of another area (natively a 6 mm hole wall of
                        // 46.87 against 37.86 mm2 after Fold of a Lofted Bend): the faces are
                        // not paired by area but measured, skin against skin.
                        skinDeviation = Math.Max(SkinDeviation71(expected, actual, LocalDeviationLimit71 * 10, out skinSamples),
                            SkinDeviation71(actual, expected, LocalDeviationLimit71 * 10, out skinSamplesBack));
                        if (skinDeviation <= LocalDeviationLimit71)
                        {
                            booleans = true;
                            evidence = "ONE_DIFFERENCE_PLUS_SKIN_DEVIATION";
                            residual = 2 * (missingDone ? missing.Bodies : extra.Bodies).Sum(x => ((double[])x.GetMassProperties(1.0))[3]);
                        }
                    }
                }
            }
            else
            {
                // Corners that do not pair: natively a
                // Curve Driven Pattern of holes on a 44 m radius sheet gave the mirror
                // image with eight more corners than the reflected source (edges the
                // source body has in one piece; volumes equal to 4e-8, skins 1.5e-9 m
                // apart), 65e-6 m from the nearest source corner and, after a Move
                // Face, 19.5 mm from it. A corner in the middle of an edge of the other
                // body says nothing about the shape, so the corners are not compared
                // here: the two skins are measured against each other - every edge of
                // either body, its ends included, against the faces of the other - and
                // the difference of the volumes stands in for the difference volume
                // (limits at the end). The numbers of faces need not be equal either
                // (natively the body of an Unfold with 74 against 76 corners and one
                // face in two pieces): a face missing from one body has edges that lie
                // off the skin of the other, and that is measured.
                cornersNotPaired = true;
                CreateMirrorPartPackage.LogDebug("[BODY71][SURFACE_CHAIN_CORNERS_NOT_PAIRED] label=" + label + " faces=" + expected.GetFaceCount() + "/" + actual.GetFaceCount() +
                    " edges=" + expected.GetEdgeCount() + "/" + actual.GetEdgeCount() + " vertices=" + a.Count + "/" + b.Count);
                double other = ((double[])actual.GetMassProperties(1.0))[3];
                if (Math.Abs(other - volume) <= volume * VolumeShareLimit71)
                {
                    skinDeviation = Math.Max(SkinDeviation71(expected, actual, LocalDeviationLimit71 * 10, out skinSamples),
                        SkinDeviation71(actual, expected, LocalDeviationLimit71 * 10, out skinSamplesBack));
                    if (skinDeviation <= LocalDeviationLimit71)
                    {
                        booleans = true;
                        evidence = "CORNERS_NOT_PAIRED_SKIN_DEVIATION_PLUS_VOLUME";
                        residual = Math.Abs(other - volume);
                    }
                }
            }
            // The difference is judged as a mean thickness over the whole skin of
            // the body (a thin sheet has little volume, so a volume share alone
            // says little); a share of the volume is a hard cap. Limits below.
            double skin = (expected.GetFaces() as object[] ?? new object[0]).Cast<Face2>().Sum(f => f.GetArea());
            double thickness = skin > 0 ? residual / skin : double.NaN;
            // Limits 5e-7 m and 5e-4: a natively re-folded 44 m radius sheet with all
            // vertices equal to 3e-10 m measured 1.01e-7 m and 1.04e-4, still far
            // inside the micrometre deviation the user accepted for these Parts.
            bool pass = booleans && volume > 0 && thickness <= MeanDeviationLimit71 && residual <= volume * VolumeShareLimit71 && thickestPiece <= LocalDeviationLimit71;
            // A piece thicker than 5e-6 m is a local difference, not a sliver: then the two
            // skins are measured against each other and must stay inside the local limit.
            if (pass && thickestPiece > 5e-6 && double.IsNaN(skinDeviation))
                skinDeviation = Math.Max(SkinDeviation71(expected, actual, LocalDeviationLimit71 * 10, out skinSamples),
                    SkinDeviation71(actual, expected, LocalDeviationLimit71 * 10, out skinSamplesBack));
            if (pass && !double.IsNaN(skinDeviation)) pass = skinDeviation <= LocalDeviationLimit71;
            if (pass)
                LargestAcceptedDeviation71 = Math.Max(LargestAcceptedDeviation71,
                    Math.Max(cornersNotPaired ? 0 : Math.Max(forward, backward), Math.Max(thickestPiece, double.IsNaN(skinDeviation) ? 0 : skinDeviation)));
            CreateMirrorPartPackage.LogDebug("[BODY71][SURFACE_CHAIN_RESIDUAL] label=" + label + " vertices=" + a.Count + "/" + b.Count +
                " meanThickness_m=" + thickness.ToString("E2", CultureInfo.InvariantCulture) +
                " thickestPiece_m=" + thickestPiece.ToString("E2", CultureInfo.InvariantCulture) +
                " skinDeviation_m=" + skinDeviation.ToString("E2", CultureInfo.InvariantCulture) + " skinSamples=" + skinSamples + "/" + skinSamplesBack +
                (skinDeviationAt71 == null ? "" : " farthestAt_mm=" + (skinDeviationAt71[0] * 1e3).ToString("F2", CultureInfo.InvariantCulture) + "," +
                    (skinDeviationAt71[1] * 1e3).ToString("F2", CultureInfo.InvariantCulture) + "," + (skinDeviationAt71[2] * 1e3).ToString("F2", CultureInfo.InvariantCulture) +
                    (skinDeviationAt71[4] > 0 ? "(face)" : "(edge)")) +
                " farthestVertex_m=" + Math.Max(forward, backward).ToString("E2", CultureInfo.InvariantCulture) +
                " booleans=" + booleans + " residual_mm3=" + (residual * 1e9).ToString("F4", CultureInfo.InvariantCulture) +
                " relativeVolume=" + (volume > 0 ? residual / volume : double.NaN).ToString("E2", CultureInfo.InvariantCulture) +
                " evidence=" + evidence + " limits=vertex5E-6,thickness" + MeanDeviationLimit71.ToString("E0", CultureInfo.InvariantCulture) +
                ",volume" + VolumeShareLimit71.ToString("E0", CultureInfo.InvariantCulture) + ",piece" + LocalDeviationLimit71.ToString("E0", CultureInfo.InvariantCulture) +
                ",skin" + LocalDeviationLimit71.ToString("E0", CultureInfo.InvariantCulture) + " afterFold=" + FoldReplayed71 + " result=" + (pass ? "PASS" : "FAIL"));
            return pass;
        }

        // Farthest sampled point of the skin of one body from the skin of the other:
        // 33 points on every edge and a 7 x 7 parameter grid on every face, each grid
        // point taken on the trimmed face itself. Stops at the first point beyond
        // stopAbove. A face of the other body is only asked when its box is nearer than
        // the best distance found so far.
        // Both directions, for a caller that has two bodies with equal corners and
        // needs to know how far their skins are apart. Kept as the run's accepted
        // deviation when it is inside the local limit.
        public static double MeasureSkinDeviation71(Body2 expected, Body2 actual, string label)
        {
            skinDeviationAt71 = null;
            int forward, backward;
            double deviation = Math.Max(SkinDeviation71(expected, actual, LocalDeviationLimit71 * 10, out forward),
                SkinDeviation71(actual, expected, LocalDeviationLimit71 * 10, out backward));
            CreateMirrorPartPackage.LogDebug("[BODY71][SKIN_DEVIATION] label=" + label + " deviation_m=" + deviation.ToString("E2", CultureInfo.InvariantCulture) +
                " samples=" + forward + "/" + backward + (skinDeviationAt71 == null ? "" : " farthestAt_mm=" +
                    (skinDeviationAt71[0] * 1e3).ToString("F2", CultureInfo.InvariantCulture) + "," + (skinDeviationAt71[1] * 1e3).ToString("F2", CultureInfo.InvariantCulture) + "," +
                    (skinDeviationAt71[2] * 1e3).ToString("F2", CultureInfo.InvariantCulture) + (skinDeviationAt71[4] > 0 ? "(face)" : "(edge)")) +
                " limit_m=" + LocalDeviationLimit71.ToString("E0", CultureInfo.InvariantCulture));
            if (deviation <= LocalDeviationLimit71) LargestAcceptedDeviation71 = Math.Max(LargestAcceptedDeviation71, deviation);
            return deviation;
        }

        // Where the last measurement found its farthest point (model space, metres).
        private static double[] skinDeviationAt71;

        private static double SkinDeviation71(Body2 from, Body2 to, double stopAbove, out int samples)
        {
            // Measured on copies, and the copies are held until the measurement is
            // over. Thousands of calls go to faces and edges; a temporary body that
            // nothing refers to any more is released by the runtime in the middle of
            // such a loop, and its faces with it (natively RPC_E_DISCONNECTED on a
            // face, once after 35 s and once at the first call).
            // The same pair of bodies is proved at several checkpoints of a run (candidate,
            // feature, final, after reopening); one measurement of 6 000 points on a Part
            // with 45 faces took 3.5 minutes. A pair with the same mass properties,
            // face and edge counts as a pair already measured in this run is that pair.
            string key = BodyFingerprint71(from) + "|" + BodyFingerprint71(to) + "|" + stopAbove.ToString("R", CultureInfo.InvariantCulture);
            Tuple<double, int, double[]> known;
            double[] previous = skinDeviationAt71;
            if (!skinDeviations71.TryGetValue(key, out known))
            {
                Body2 fromCopy = from.Copy() as Body2 ?? from, toCopy = to.Copy() as Body2 ?? to;
                skinDeviationAt71 = null;
                try
                {
                    double measured = SkinDeviationOfCopies71(fromCopy, toCopy, stopAbove, out samples);
                    known = Tuple.Create(measured, samples, skinDeviationAt71);
                    skinDeviations71[key] = known;
                }
                finally { GC.KeepAlive(fromCopy); GC.KeepAlive(toCopy); GC.KeepAlive(from); GC.KeepAlive(to); }
            }
            samples = known.Item2;
            skinDeviationAt71 = previous != null && (known.Item3 == null || previous[3] >= known.Item3[3]) ? previous : known.Item3;
            return known.Item1;
        }

        private static readonly Dictionary<string, Tuple<double, int, double[]>> skinDeviations71 =
            new Dictionary<string, Tuple<double, int, double[]>>(StringComparer.Ordinal);

        // Cleared with the other per-run state when a run captures its source.
        public static void ResetSkinDeviations71() { skinDeviations71.Clear(); }

        // Eight significant digits: two reflected copies of one body differ in the last
        // digits of their mass properties (natively no pair repeated at full precision).
        private static string BodyFingerprint71(Body2 body)
        {
            var mass = body.GetMassProperties(1.0) as double[] ?? new double[0];
            return body.GetFaceCount() + "/" + body.GetEdgeCount() + "/" + string.Join(",", mass.Select(v => v.ToString("G8", CultureInfo.InvariantCulture)));
        }

        private static double SkinDeviationOfCopies71(Body2 from, Body2 to, double stopAbove, out int samples)
        {
            samples = 0;
            var targets = (to.GetFaces() as object[] ?? new object[0]).Cast<Face2>().Select(f => new KeyValuePair<Face2, double[]>(f, f.GetBox() as double[])).ToList();
            if (targets.Count == 0 || targets.Any(t => t.Value == null || t.Value.Length < 6)) return double.PositiveInfinity;
            Func<double[], double> off = p =>
            {
                double best = double.PositiveInfinity;
                foreach (var t in targets.Select(t => new { Face = t.Key, Gap = Math.Sqrt(Enumerable.Range(0, 3).Sum(i =>
                    {
                        double d = Math.Max(0, Math.Max(Math.Min(t.Value[i], t.Value[i + 3]) - p[i], p[i] - Math.Max(t.Value[i], t.Value[i + 3])));
                        return d * d;
                    })) }).OrderBy(t => t.Gap))
                {
                    if (t.Gap >= best || best <= 1e-9) break;
                    var near = t.Face.GetClosestPointOn(p[0], p[1], p[2]) as double[];
                    if (near == null || near.Length < 3) continue;
                    best = Math.Min(best, Math.Sqrt((near[0] - p[0]) * (near[0] - p[0]) + (near[1] - p[1]) * (near[1] - p[1]) + (near[2] - p[2]) * (near[2] - p[2])));
                }
                return best;
            };
            double worst = 0;
            foreach (Edge edge in (from.GetEdges() as object[] ?? new object[0]).Cast<Edge>())
            {
                var parameters = edge.GetCurveParams2() as double[];
                if (parameters == null || parameters.Length < 8 || parameters[6] == parameters[7]) return double.PositiveInfinity;
                for (int i = 0; i <= 32; i++)
                {
                    var value = edge.Evaluate2(parameters[6] + (parameters[7] - parameters[6]) * i / 32.0, 0) as double[];
                    if (value == null || value.Length < 3) return double.PositiveInfinity;
                    samples++;
                    double here = off(value);
                    if (here > worst) { worst = here; if (skinDeviationAt71 == null || here > skinDeviationAt71[3]) skinDeviationAt71 = new[] { value[0], value[1], value[2], here, 0.0 }; }
                    if (worst > stopAbove) return worst;
                }
            }
            foreach (Face2 face in (from.GetFaces() as object[] ?? new object[0]).Cast<Face2>())
            {
                var surface = face.GetSurface() as Surface;
                var uv = face.GetUVBounds() as double[];
                if (surface == null || uv == null || uv.Length < 4) return double.PositiveInfinity;
                for (int i = 1; i <= 7; i++)
                    for (int j = 1; j <= 7; j++)
                    {
                        var raw = surface.Evaluate(uv[0] + (uv[1] - uv[0]) * i / 8.0, uv[2] + (uv[3] - uv[2]) * j / 8.0, 0, 0) as double[];
                        if (raw == null || raw.Length < 3) continue;
                        var on = face.GetClosestPointOn(raw[0], raw[1], raw[2]) as double[];
                        if (on == null || on.Length < 3) continue;
                        samples++;
                        double here = off(on);
                        if (here > worst) { worst = here; if (skinDeviationAt71 == null || here > skinDeviationAt71[3]) skinDeviationAt71 = new[] { on[0], on[1], on[2], here, 1.0 }; }
                        if (worst > stopAbove) return worst;
                    }
            }
            return samples == 0 ? double.PositiveInfinity : worst;
        }

        // Reduced proof for two bodies that both carry the accepted self-contact
        // class, where a Boolean cannot run and Confirm's 1e-10 identity is not
        // reachable (the native congruence fit itself is only ~1e-7 accurate on
        // such bodies). ALL of: equal face/edge counts; every vertex matched
        // one-to-one within 1e-7 m; every face matched one-to-one by surface
        // type and area (1e-6 relative); congruent in both directions with a
        // fit that moves no point of the body by more than 1e-6 m; volume
        // within 1e-4 relative (integration over self-contact is not exact).
        public static bool ConfirmSelfContact71(Body2 expected, Body2 actual, string label)
        {
            const double pointTolerance = 1e-7, fitTolerance = 1e-6;
            string reason = null;
            double fit = double.NaN, volumeError = double.NaN;
            if (expected == null || actual == null || expected.GetFaceCount() != actual.GetFaceCount() ||
                expected.GetEdgeCount() != actual.GetEdgeCount()) reason = "TOPOLOGY_COUNT";
            if (reason == null && !BodyAuditPolicy69.SameToleratedSelfContact71(
                NativeBodyAudit69.Read(expected, label, "SELF_CONTACT_EXPECTED"),
                NativeBodyAudit69.Read(actual, label, "SELF_CONTACT_ACTUAL"))) reason = "NOT_SELF_CONTACT_CLASS";
            if (reason == null)
            {
                var a = (expected.GetVertices() as object[] ?? new object[0]).Cast<Vertex>().Select(v => v.GetPoint() as double[]).ToList();
                var b = (actual.GetVertices() as object[] ?? new object[0]).Cast<Vertex>().Select(v => v.GetPoint() as double[]).ToList();
                if (a.Count != b.Count) reason = "VERTEX_COUNT";
                foreach (double[] p in a)
                {
                    if (reason != null) break;
                    int index = b.FindIndex(q => Math.Abs(p[0] - q[0]) <= pointTolerance &&
                        Math.Abs(p[1] - q[1]) <= pointTolerance && Math.Abs(p[2] - q[2]) <= pointTolerance);
                    if (index < 0) reason = "VERTEX_POSITION"; else b.RemoveAt(index);
                }
            }
            if (reason == null)
            {
                // Pair faces by surface type and bounding box, then compare area,
                // so a mismatch names the face instead of shifting a sorted list.
                // The self-contact region itself is not well defined: SOLIDWORKS
                // trims the touching bend faces slightly differently when the
                // flange is rebuilt (measured ~2e-5 relative with all vertices
                // coincident). Only faces of the features that own a faulted
                // face in the native result get the 1e-4 area tolerance; every
                // other face keeps 1e-6.
                // Native observation (ks2): the differing faces are every bend
                // face meeting at the touching corner, including bends that own
                // no fault and share no vertex with the faulted ones. So, for a
                // body pair in the accepted self-contact class (re-checked at
                // the top of this method), curved faces get the 1e-4 area
                // tolerance. Planar faces keep 1e-6.
                Func<Body2, bool, List<Tuple<int, double, double[], bool>>> faces = (body, native) =>
                    (body.GetFaces() as object[] ?? new object[0]).Cast<Face2>().Select(f =>
                    {
                        int identity = ((Surface)f.GetSurface()).Identity();
                        return Tuple.Create(identity, f.GetArea(), f.GetBox() as double[],
                            native && identity != (int)swSurfaceTypes_e.PLANE_TYPE);
                    }).ToList();
                var fa = faces(expected, false); var fb = faces(actual, true);
                var mismatches = new List<string>();
                int relaxed = 0;
                foreach (var face in fa)
                {
                    int index = fb.FindIndex(o => o.Item1 == face.Item1 && face.Item3 != null && o.Item3 != null &&
                        Enumerable.Range(0, 6).All(i => Math.Abs(face.Item3[i] - o.Item3[i]) <= 1e-6));
                    string box = face.Item3 == null ? "" : string.Join(",", face.Item3.Select(v => (v * 1000).ToString("F3", CultureInfo.InvariantCulture)));
                    if (index < 0) mismatches.Add("NO_COUNTERPART type=" + face.Item1 + " area_mm2=" + (face.Item2 * 1e6).ToString("F4", CultureInfo.InvariantCulture) + " box_mm=[" + box + "]");
                    else
                    {
                        double areaError = Math.Abs(face.Item2 - fb[index].Item2);
                        if (areaError > Math.Max(1e-12, face.Item2 * 1e-6) && fb[index].Item4 && areaError <= face.Item2 * 1e-4)
                        {
                            relaxed++;
                            CreateMirrorPartPackage.LogDebug("[BODY71][CONTACT_FACE_AREA] label=" + label + " type=" + face.Item1 +
                                " expected_mm2=" + (face.Item2 * 1e6).ToString("F4", CultureInfo.InvariantCulture) +
                                " actual_mm2=" + (fb[index].Item2 * 1e6).ToString("F4", CultureInfo.InvariantCulture) +
                                " relative=" + (areaError / face.Item2).ToString("E2", CultureInfo.InvariantCulture) + " tolerance=1E-4 curvedFaceOfSelfContactBody=True");
                        }
                        else if (areaError > Math.Max(1e-12, face.Item2 * 1e-6))
                            mismatches.Add("AREA type=" + face.Item1 + " expected_mm2=" + (face.Item2 * 1e6).ToString("F4", CultureInfo.InvariantCulture) +
                                " actual_mm2=" + (fb[index].Item2 * 1e6).ToString("F4", CultureInfo.InvariantCulture) + " box_mm=[" + box + "]");
                        fb.RemoveAt(index);
                    }
                }
                foreach (var extra in fb)
                    mismatches.Add("EXTRA_ACTUAL type=" + extra.Item1 + " area_mm2=" + (extra.Item2 * 1e6).ToString("F4", CultureInfo.InvariantCulture) +
                        " box_mm=[" + (extra.Item3 == null ? "" : string.Join(",", extra.Item3.Select(v => (v * 1000).ToString("F3", CultureInfo.InvariantCulture)))) + "]");
                foreach (string mismatch in mismatches.Take(12))
                    CreateMirrorPartPackage.LogDebug("[BODY71][FACE_MISMATCH] label=" + label + " " + mismatch);
                if (mismatches.Count > 0) reason = "FACE_TYPE_OR_AREA count=" + mismatches.Count;
            }
            if (reason == null)
            {
                MathTransform forward, backward;
                bool forwardShape = expected.GetCoincidenceTransform2(actual, out forward);
                bool backwardShape = actual.GetCoincidenceTransform2(expected, out backward);
                double[] box = expected.GetBodyBox() as double[];
                double reach = box == null ? double.NaN : Math.Sqrt(Enumerable.Range(0, 3).Sum(i =>
                    Math.Pow(Math.Max(Math.Abs(box[i]), Math.Abs(box[i + 3])), 2)));
                fit = Math.Max(FitDisplacement71(forwardShape ? forward : null, reach),
                    FitDisplacement71(backwardShape ? backward : null, reach));
                if (!(fit <= fitTolerance)) reason = "CONGRUENCE_FIT";
            }
            if (reason == null)
            {
                double va = ((double[])expected.GetMassProperties(1.0))[3], vb = ((double[])actual.GetMassProperties(1.0))[3];
                volumeError = Math.Abs(va - vb) / Math.Abs(va);
                if (!(volumeError <= 1e-4)) reason = "VOLUME";
            }
            CreateMirrorPartPackage.LogDebug("[BODY71][REDUCED_PROOF] label=" + label +
                " boolean=SKIPPED_SELF_CONTACT vertices_m=" + pointTolerance.ToString("R", CultureInfo.InvariantCulture) +
                " congruenceFit_m=" + fit.ToString("R", CultureInfo.InvariantCulture) +
                " volumeRelativeError=" + volumeError.ToString("R", CultureInfo.InvariantCulture) +
                " result=" + (reason == null ? "PASS" : "FAIL reason=" + reason));
            return reason == null;
        }

        // Upper bound of how far the congruence fit moves any point within
        // `reach` of the origin: |translation| + ||scale*R - I|| * reach.
        private static double FitDisplacement71(MathTransform transform, double reach)
        {
            double[] data = transform == null ? null : transform.ArrayData as double[];
            if (data == null || data.Length != 16 || data.Any(v => double.IsNaN(v) || double.IsInfinity(v))) return double.NaN;
            double rotation = 0;
            for (int i = 0; i < 9; i++)
                rotation += Math.Pow(data[i] * data[12] - (i == 0 || i == 4 || i == 8 ? 1.0 : 0.0), 2);
            return Math.Sqrt(data[9] * data[9] + data[10] * data[10] + data[11] * data[11]) + Math.Sqrt(rotation) * reach;
        }

        public static bool ConfirmUnavailableBoolean(Body2 expected, Body2 actual,
            BodyBooleanResult missing, BodyBooleanResult extra, string label)
        {
            if (missing != null && extra != null && missing.VerificationUnavailable69 && extra.VerificationUnavailable69 &&
                missing.ToleratedSelfContact71 && extra.ToleratedSelfContact71)
            {
                // Both bodies carry the same accepted self-contact class, so a
                // Boolean was never attempted. Congruence at identity, in both
                // directions, with equal topology counts is the whole proof.
                return ConfirmSelfContact71(expected, actual, label);
            }
            if (missing != null && extra != null && (missing.VerificationUnavailable69 || extra.VerificationUnavailable69))
            {
                CreateMirrorPartPackage.LogDebug("[COINCIDENCE69][SKIPPED] label=" + label +
                    " reason=INVALID_BREP_INPUTS result=UNPROVED failureAcceptedAsEmpty=False");
                return false;
            }
            return missing != null && extra != null && CanRequest(
                missing.Success, missing.Bodies == null ? -1 : missing.Bodies.Count, missing.ErrorCode,
                extra.Success, extra.Bodies == null ? -1 : extra.Bodies.Count, extra.ErrorCode) &&
                Confirm(expected, actual, label);
        }
    }

    public sealed class ExtrudeCutRecipe44
    {
        public double[] SketchNormal;
        public int FromType;
        public double FromOffset;
        public bool NormalCut, OptimizeGeometry, FeatureScope, AutoSelect;
        public ExtrudeDirectionAudit50 FromReference, DirectionReference1, DirectionReference2;
        public double[] SourceDirection;
        public byte[][] ScopeBodies;
        public DirectionOptions44 D1, D2;
        public bool MissingSurfaceReference54;
        public bool Applied;
    }
    public sealed class DirectionOptions44
    {
        public bool Draft, DraftOutward, ReverseOffset, TranslateSurface;
        public double DraftAngle;
    }

    // Pure geometry: independent of file names, feature names and the sketch baseline.
    public static class CutOrientation44
    {
        public static double[] Unit(double[] v)
        {
            if (v == null || v.Length < 3) throw new InvalidOperationException("CUT44: missing direction vector.");
            double n = Math.Sqrt(v[0]*v[0] + v[1]*v[1] + v[2]*v[2]);
            if (double.IsNaN(n) || double.IsInfinity(n) || n < 1e-12)
                throw new InvalidOperationException("CUT44: degenerate direction vector.");
            return new[] { v[0]/n, v[1]/n, v[2]/n };
        }
        public static double[] Reflect(double[] v, double[] planeNormal)
        {
            double[] n = Unit(planeNormal), d = Unit(v);
            double dot = d[0]*n[0] + d[1]*n[1] + d[2]*n[2];
            return new[] { d[0]-2*dot*n[0], d[1]-2*dot*n[1], d[2]-2*dot*n[2] };
        }
        public static bool Opposed(double[] expected, double[] actual)
        {
            double[] a = Unit(expected), b = Unit(actual);
            double dot = a[0]*b[0] + a[1]*b[1] + a[2]*b[2];
            if (Math.Abs(dot) < 1.0-1e-8)
                throw new InvalidOperationException("CUT44: mapped direction is not parallel to reflected source. dot=" + dot.ToString("R"));
            return dot < 0;
        }
    }

    public static class CutBlindFallback54
    {
        public static bool CanQualify(int fromType, bool bothDirections, int endCondition,
            bool endReferencePresent)
        {
            return fromType == (int)swExtrudeFrom_e.swExtrudeFrom_SketchPlane &&
                !bothDirections &&
                endCondition == (int)swEndConditions_e.swEndCondUpToSurface &&
                !endReferencePresent;
        }

        public static double[] SignedSpanFromReflectedVertices(double[] origin, double[] normal,
            IEnumerable<double[]> vertices)
        {
            if (origin == null || normal == null || vertices == null ||
                origin.Length < 3 || normal.Length < 3)
                throw new InvalidOperationException("CUT54: sketch plane or removed vertices are unavailable.");
            double[] unit = CutOrientation44.Unit(normal);
            double minimum = double.PositiveInfinity, maximum = double.NegativeInfinity;
            int count = 0;
            foreach (double[] point in vertices)
            {
                if (point == null || point.Length < 3 || point.Take(3).Any(value =>
                    double.IsNaN(value) || double.IsInfinity(value)))
                    throw new InvalidOperationException("CUT54: removed-body vertex is non-finite.");
                double signedDistance = (point[0] - origin[0]) * unit[0] +
                    (point[1] - origin[1]) * unit[1] +
                    (point[2] - origin[2]) * unit[2];
                minimum = Math.Min(minimum, signedDistance);
                maximum = Math.Max(maximum, signedDistance);
                count++;
            }
            if (count == 0 || double.IsNaN(minimum) || double.IsInfinity(minimum) ||
                double.IsNaN(maximum) || double.IsInfinity(maximum))
                throw new InvalidOperationException("CUT54: no finite removed-body vertex span.");
            return new[] { minimum, maximum };
        }

        public static double ExactDepthFromSignedSpan(double[] span)
        {
            if (span == null || span.Length < 2 ||
                span.Any(value => double.IsNaN(value) || double.IsInfinity(value)) ||
                span[0] > span[1])
                throw new InvalidOperationException("CUT54: signed removed-body span is invalid.");
            // A single-direction Blind cut cannot reproduce removed material on
            // both sides of the sketch plane. A geometric oracle remains required.
            if (span[0] < -1e-7 && span[1] > 1e-7)
                throw new InvalidOperationException("CUT54: removed material crosses both sides of the sketch plane.");
            double depth = Math.Max(Math.Abs(span[0]), Math.Abs(span[1]));
            if (depth <= 1e-9)
                throw new InvalidOperationException("CUT54: removed-body depth is too small to replay safely.");
            return depth;
        }
    }

    public static partial class SketchOperationsHelper
    {
        private static double[] SketchNormal44(Sketch sketch)
        {
            if (sketch == null) throw new InvalidOperationException("CUT44: driving sketch missing.");
            var math = (IMathUtility)SwAddin.InstanceSwApp.GetMathUtility();
            var vector = (MathVector)math.CreateVector(new[] { 0.0, 0.0, 1.0 });
            return CutOrientation44.Unit(((MathVector)vector.MultiplyTransform(
                sketch.ModelToSketchTransform.IInverse())).ArrayData as double[]);
        }

        private static ExtrudeDirectionAudit50 CaptureReference44(ModelDoc2 model, object entity, int type)
        {
            var r = new ExtrudeDirectionAudit50 { ReferencePresent = entity != null,
                ReferenceEntityKind = EndReferenceKind50(entity), ReferenceSelectionType = type };
            if (entity == null) return r;
            var vertex = entity as Vertex;
            var point = entity as SketchPoint;
            var edge = entity as Edge;
            var face = entity as Face2;
            if (vertex != null) r.ReferencePoint = vertex.GetPoint() as double[];
            else if (point != null) r.ReferencePoint = SketchPointWorld50(point);
            else if (edge != null) r.ReferenceEdgeSamples = SampleEdge24(edge);
            else if (face != null) r.ReferenceFace = CaptureFaceSupport24(face);
            else throw new InvalidOperationException("CUT44: unsupported reference kind=" + r.ReferenceEntityKind +
                "; no silent substitution of the original option.");
            return r;
        }

        private static double[] DirectionVector44(object first, object second, double[] defaultNormal)
        {
            if (first == null && second == null) return defaultNormal;
            Func<object, double[]> point = obj => obj is Vertex ? ((Vertex)obj).GetPoint() as double[] :
                obj is SketchPoint ? SketchPointWorld50((SketchPoint)obj) : null;
            if (second != null)
            {
                double[] a = point(first), b = point(second);
                if (a == null || b == null) throw new InvalidOperationException("CUT44: two-point direction requires points/vertices.");
                return CutOrientation44.Unit(new[] { b[0]-a[0], b[1]-a[1], b[2]-a[2] });
            }
            var edge = first as Edge;
            if (edge != null && ((Curve)edge.GetCurve()).IsLine())
            {
                double[] a = ((Vertex)edge.GetStartVertex()).GetPoint() as double[];
                double[] b = ((Vertex)edge.GetEndVertex()).GetPoint() as double[];
                return CutOrientation44.Unit(new[] { b[0]-a[0], b[1]-a[1], b[2]-a[2] });
            }
            var face = first as Face2;
            if (face != null && ((Surface)face.GetSurface()).IsPlane())
                return CutOrientation44.Unit(face.Normal as double[]);
            throw new InvalidOperationException("CUT44: explicit direction has no supported geometric vector; replay stopped.");
        }

        private static DirectionOptions44 CaptureDirectionOptions44(IExtrudeFeatureData2 d, bool forward, int end)
        {
            var value = new DirectionOptions44 { Draft = d.GetDraftWhileExtruding(forward) };
            if (value.Draft) { value.DraftAngle = d.GetDraftAngle(forward); value.DraftOutward = d.GetDraftOutward(forward); }
            if (end == (int)swEndConditions_e.swEndCondOffsetFromSurface)
            { value.ReverseOffset = d.GetReverseOffset(forward); value.TranslateSurface = d.GetTranslateSurface(forward); }
            return value;
        }

        private static bool NeedsEndReference44(int end)
        {
            return end == (int)swEndConditions_e.swEndCondUpToVertex ||
                end == (int)swEndConditions_e.swEndCondUpToSurface ||
                end == (int)swEndConditions_e.swEndCondOffsetFromSurface ||
                end == (int)swEndConditions_e.swEndCondUpToBody ||
                end == (int)swEndConditions_e.swEndCondUpToSelection;
        }

        private static ExtrudeCutRecipe44 CaptureCutRecipe44(ModelDoc2 model, IExtrudeFeatureData2 d,
            Sketch sketch, CutAuditSnapshot21 audit)
        {
            if (d.IsThinFeature())
                throw new InvalidOperationException("CUT44: thin-wall side mapping is not implemented; source option is not replaced.");
            if (audit.D1_50 == null || audit.D1_50.CaptureError != null ||
                (audit.BothDirections50 && (audit.D2_50 == null || audit.D2_50.CaptureError != null)))
                throw new InvalidOperationException("CUT44: active direction options were not completely captured.");
            bool missingSurface54 = CutBlindFallback54.CanQualify(d.FromType,
                audit.BothDirections50, audit.D1_50.EndCondition, audit.D1_50.ReferencePresent);
            foreach (var direction in audit.BothDirections50 ? new[] { audit.D1_50, audit.D2_50 } : new[] { audit.D1_50 })
            {
                if (!Enum.IsDefined(typeof(swEndConditions_e), direction.EndCondition) ||
                    (NeedsEndReference44(direction.EndCondition) && !direction.ReferencePresent &&
                        !(missingSurface54 && direction.Forward)))
                    throw new InvalidOperationException("CUT44: unknown end condition or required reference is missing: " + direction.EndConditionName);
                // The legacy audit tolerates missing depth; a replay recipe cannot.
                if (direction.EndCondition == (int)swEndConditions_e.swEndCondBlind ||
                    direction.EndCondition == (int)swEndConditions_e.swEndCondMidPlane ||
                    direction.EndCondition == (int)swEndConditions_e.swEndCondOffsetFromSurface)
                {
                    direction.Depth = d.GetDepth(direction.Forward);
                    if (double.IsNaN(direction.Depth) || double.IsInfinity(direction.Depth))
                        throw new InvalidOperationException("CUT44: non-finite extrusion depth.");
                }
            }
            var r = new ExtrudeCutRecipe44 { SketchNormal = SketchNormal44(sketch), FromType = d.FromType,
                MissingSurfaceReference54 = missingSurface54,
                NormalCut = d.NormalCut, OptimizeGeometry = d.OptimizeGeometry,
                FeatureScope = d.FeatureScope, AutoSelect = d.AutoSelect };
            if (r.FromType == (int)swExtrudeFrom_e.swExtrudeFrom_Offset) r.FromOffset = d.FromOffsetDistance;
            object from; int fromType;
            if (r.FromType == (int)swExtrudeFrom_e.swExtrudeFrom_SurfaceFacePlane ||
                r.FromType == (int)swExtrudeFrom_e.swExtrudeFrom_Vertex)
            { d.GetFromEntity(out from, out fromType); r.FromReference = CaptureReference44(model, from, fromType);
              if (!r.FromReference.ReferencePresent) throw new InvalidOperationException("CUT44: From reference missing."); }
            object dir1, dir2; int type1, type2;
            int directionCount = d.GetDirectionReference(out dir1, out type1, out dir2, out type2);
            // SOLIDWORKS can return a non-positive count for the implicit sketch-normal
            // direction. The out-entities are authoritative: no entities means no
            // reference to reparent, while a positive count without its entities is
            // genuinely incomplete and must fail closed.
            CreateMirrorPartPackage.LogDebug("[CUT44][DIRECTION_REFERENCE_CAPTURE] count=" + directionCount +
                " ref1=" + EndReferenceKind50(dir1) + " type1=" + type1 +
                " ref2=" + EndReferenceKind50(dir2) + " type2=" + type2);
            if (directionCount > 2 ||
                (dir1 == null && dir2 != null) ||
                (directionCount > 0 && dir1 == null) ||
                (directionCount == 1 && dir2 != null) ||
                (directionCount == 2 && dir2 == null) ||
                (directionCount <= 0 && (dir1 != null || dir2 != null)))
                throw new InvalidOperationException("CUT44: inconsistent extrusion direction reference count=" +
                    directionCount + " ref1=" + EndReferenceKind50(dir1) +
                    " ref2=" + EndReferenceKind50(dir2) + ".");
            if (missingSurface54 && (dir1 != null || dir2 != null))
                throw new InvalidOperationException("CUT54: missing surface fallback requires the implicit sketch-normal direction.");
            r.DirectionReference1 = CaptureReference44(model, dir1, type1);
            r.DirectionReference2 = CaptureReference44(model, dir2, type2);
            r.SourceDirection = DirectionVector44(dir1, dir2, r.SketchNormal);
            r.D1 = CaptureDirectionOptions44(d, true, audit.D1_50.EndCondition);
            if (audit.BothDirections50) r.D2 = CaptureDirectionOptions44(d, false, audit.D2_50.EndCondition);
            if (r.FeatureScope && !r.AutoSelect)
            {
                Array bodies = d.FeatureScopeBodies as Array;
                if (bodies == null || bodies.Length == 0) throw new InvalidOperationException("CUT44: explicit feature scope is empty.");
                r.ScopeBodies = bodies.Cast<object>().Select(body => model.Extension.GetPersistReference3(body) as byte[]).ToArray();
                if (r.ScopeBodies.Any(id => id == null || id.Length == 0))
                    throw new InvalidOperationException("CUT44: body scope identity unavailable.");
            }
            CreateMirrorPartPackage.LogDebug("[CUT44][CAPTURE] from=" + r.FromType + " D1=" + audit.D1_50.EndConditionName +
                " D2Enabled=" + audit.BothDirections50 + " normalCut=" + r.NormalCut +
                " explicitScope=" + (r.ScopeBodies != null) +
                " sourceScopeCount=" + (r.ScopeBodies == null ? 0 : r.ScopeBodies.Length));
            if (missingSurface54)
                CreateMirrorPartPackage.LogDebug("[CUT54][SOURCE_REFERENCE_MISSING] end=UpToSurface selectionType=" +
                    audit.D1_50.ReferenceSelectionType + " action=GEOMETRY_GATED_BLIND_CANDIDATES sourceOptionPreservedInAudit=True");
            return r;
        }

        private static object ResolveReference44(ModelDoc2 model, ExtrudeDirectionAudit50 reference, PlaneData plane)
        {
            if (reference == null || !reference.ReferencePresent) return null;
            if (reference.ReferenceEntityKind == "Body" && reference.PersistentReference != null)
            {
                int state;
                object body = model.Extension.GetObjectByPersistReference3(reference.PersistentReference, out state);
                if (!(body is Body2) || state != 0) throw new InvalidOperationException("CUT44: end body identity mapping failed.");
                return body;
            }
            return ResolveEndReference50(model, reference, plane);
        }

        private static void CheckReadback44<T>(List<string> differences, string name, T expected, Func<T> read)
        {
            try
            {
                T actual = read();
                if (!EqualityComparer<T>.Default.Equals(expected, actual))
                    differences.Add(name + " expected=" + expected + " actual=" + actual);
            }
            catch (Exception ex) { differences.Add(name + " readError=" + ex.GetType().Name + ":" + ex.Message); }
        }

        private static void CheckReadbackDouble44(List<string> differences, string name,
            double expected, Func<double> read, double tolerance)
        {
            try
            {
                double actual = read();
                if (double.IsNaN(actual) || double.IsInfinity(actual) || Math.Abs(actual - expected) > tolerance)
                    differences.Add(name + " expected=" + expected.ToString("R") + " actual=" + actual.ToString("R"));
            }
            catch (Exception ex) { differences.Add(name + " readError=" + ex.GetType().Name + ":" + ex.Message); }
        }

        private static void CheckDirectionReadback44(List<string> differences, IExtrudeFeatureData2 read,
            ExtrudeDirectionAudit50 source, DirectionOptions44 options, double? fallbackDepth54 = null)
        {
            bool forward = source.Forward;
            string prefix = forward ? "D1." : "D2.";
            int expectedEnd = fallbackDepth54.HasValue ? (int)swEndConditions_e.swEndCondBlind : source.EndCondition;
            CheckReadback44(differences, prefix + "EndCondition", expectedEnd, () => read.GetEndCondition(forward));
            CheckReadback44(differences, prefix + "Draft", options.Draft, () => read.GetDraftWhileExtruding(forward));
            if (fallbackDepth54.HasValue)
                CheckReadbackDouble44(differences, prefix + "FallbackDepth54", fallbackDepth54.Value,
                    () => read.GetDepth(forward), 1e-9);
            else if (source.EndCondition == (int)swEndConditions_e.swEndCondBlind ||
                source.EndCondition == (int)swEndConditions_e.swEndCondMidPlane ||
                source.EndCondition == (int)swEndConditions_e.swEndCondOffsetFromSurface)
                CheckReadbackDouble44(differences, prefix + "Depth", source.Depth, () => read.GetDepth(forward), 1e-9);
            if (options.Draft)
            {
                CheckReadbackDouble44(differences, prefix + "DraftAngle", options.DraftAngle,
                    () => read.GetDraftAngle(forward), 1e-10);
                CheckReadback44(differences, prefix + "DraftOutward", options.DraftOutward,
                    () => read.GetDraftOutward(forward));
            }
            if (source.EndCondition == (int)swEndConditions_e.swEndCondOffsetFromSurface)
            {
                CheckReadback44(differences, prefix + "ReverseOffset", options.ReverseOffset,
                    () => read.GetReverseOffset(forward));
                CheckReadback44(differences, prefix + "TranslateSurface", options.TranslateSurface,
                    () => read.GetTranslateSurface(forward));
            }
        }

        private static void ApplyDirection44(ModelDoc2 model, IExtrudeFeatureData2 d,
            ExtrudeDirectionAudit50 source, DirectionOptions44 options, PlaneData plane,
            double? fallbackDepth54 = null)
        {
            bool forward = source.Forward;
            d.SetEndCondition(forward, fallbackDepth54.HasValue
                ? (int)swEndConditions_e.swEndCondBlind : source.EndCondition);
            if (fallbackDepth54.HasValue)
                d.SetDepth(forward, fallbackDepth54.Value);
            else if (source.EndCondition == (int)swEndConditions_e.swEndCondBlind ||
                source.EndCondition == (int)swEndConditions_e.swEndCondMidPlane ||
                source.EndCondition == (int)swEndConditions_e.swEndCondOffsetFromSurface)
                d.SetDepth(forward, source.Depth);
            if (!fallbackDepth54.HasValue && NeedsEndReference44(source.EndCondition))
            {
                object target = ResolveReference44(model, source, plane);
                if (target == null) throw new InvalidOperationException("CUT44: mapped end reference is missing.");
                d.SetEndConditionReference(forward, target);
                int type; object actual = d.GetEndConditionReference(forward, out type);
                if (type != source.ReferenceSelectionType || !SingleSketchTargetBuilderV7.SameComObject(target, actual))
                    throw new InvalidOperationException("CUT44: end reference identity/type readback differs.");
                CreateMirrorPartPackage.LogDebug("[CUT44][END_BOUND] direction=" + (forward ? "D1" : "D2") +
                    " end=" + source.EndConditionName + " kind=" + source.ReferenceEntityKind + " identity=True");
            }
            if (source.EndCondition == (int)swEndConditions_e.swEndCondOffsetFromSurface)
            { d.SetReverseOffset(forward, options.ReverseOffset); d.SetTranslateSurface(forward, options.TranslateSurface); }
            d.SetDraftWhileExtruding(forward, options.Draft);
            if (options.Draft) { d.SetDraftAngle(forward, options.DraftAngle); d.SetDraftOutward(forward, options.DraftOutward); }
        }

        private static object[] ResolveScopeBodies56(ModelDoc2 model, Feature feature,
            CutAuditSnapshot21 audit, byte[][] scopeReferences)
        {
            var resolved = new object[scopeReferences.Length];
            string failure = null;
            for (int i = 0; i < scopeReferences.Length; i++)
            {
                int state = -1;
                object body = null;
                try { body = model.Extension.GetObjectByPersistReference3(scopeReferences[i], out state); }
                catch (Exception ex) { failure = "index=" + i + " exception=" + ex.GetType().Name; }
                if (failure == null && (!(body is Body2) || state != 0))
                    failure = "index=" + i + " state=" + state + " type=" +
                        (body == null ? "null" : body.GetType().Name);
                if (failure != null) break;
                resolved[i] = body;
            }
            if (failure == null) return resolved;

            // Persistent body IDs can become obsolete after upstream in-place
            // feature replay. With exactly one explicitly scoped source body and
            // one live solid, the mapping is unique, provided the reflected
            // before-cut body was already proven equal by CUT23. Never broaden
            // a subset scope on a multi-body part.
            object[] live = ((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[]
                ?? new object[0];
            bool unique = audit != null && audit.BeforeChainVerified23 &&
                scopeReferences.Length == 1 && live.Length == 1 && live[0] is Body2;
            CreateMirrorPartPackage.LogDebug("[SCOPE56][RESOLVE] feature=" + feature.Name +
                " persistent=" + failure + " sourceScopeCount=" + scopeReferences.Length +
                " liveSolidCount=" + live.Length + " cut23Verified=" +
                (audit != null && audit.BeforeChainVerified23) +
                " result=" + (unique ? "UNIQUE_SINGLE_SOLID" : "FAIL_CLOSED"));
            if (!unique)
                throw new InvalidOperationException("CUT44: body scope mapping failed; " + failure +
                    "; sourceScopeCount=" + scopeReferences.Length + " liveSolidCount=" + live.Length +
                    "; not expanding to all bodies.");
            return new[] { live[0] };
        }

        public static void ApplyReflectedCut44(ModelDoc2 model, Feature feature, Feature profile,
            CutAuditSnapshot21 audit, PlaneData plane, double? fallbackDepth54 = null,
            bool? reverseOverride54 = null, bool? flipOverride71 = null)
        {
            if (audit == null || audit.Recipe44 == null || !string.IsNullOrEmpty(audit.CaptureError))
                throw new InvalidOperationException("CUT44: complete source recipe required. " + (audit == null ? "" : audit.CaptureError));
            var recipe = audit.Recipe44;
            recipe.Applied = false;
            if (recipe.MissingSurfaceReference54 != fallbackDepth54.HasValue ||
                (fallbackDepth54.HasValue && (double.IsNaN(fallbackDepth54.Value) ||
                    double.IsInfinity(fallbackDepth54.Value) || fallbackDepth54.Value <= 0.0 ||
                    !reverseOverride54.HasValue)))
                throw new InvalidOperationException("CUT54: missing surface reference needs a finite, verified candidate depth and direction.");
            if (model.SketchManager.ActiveSketch != null)
                throw new InvalidOperationException("CUT44: finish sketch mutation before editing feature definition.");
            double[] targetNormal = SketchNormal44(profile.GetSpecificFeature2() as Sketch);
            bool normalOpposed = CutOrientation44.Opposed(CutOrientation44.Reflect(recipe.SketchNormal, plane.Normal), targetNormal);
            bool access = false, committed = false;
            IExtrudeFeatureData2 d = feature.GetDefinition() as IExtrudeFeatureData2;
            if (d == null) throw new InvalidOperationException("CUT44: extrusion definition unavailable.");
            try
            {
                access = d.AccessSelections(model, null);
                if (!access) throw new InvalidOperationException("CUT44: AccessSelections failed.");
                object dir1 = ResolveReference44(model, recipe.DirectionReference1, plane);
                object dir2 = ResolveReference44(model, recipe.DirectionReference2, plane);
                double[] targetDirection = DirectionVector44(dir1, dir2, targetNormal);
                bool directionOpposed = CutOrientation44.Opposed(CutOrientation44.Reflect(recipe.SourceDirection, plane.Normal), targetDirection);
                bool reverse = reverseOverride54 ?? (audit.ReverseDirection50 ^ directionOpposed);
                // det(local 2D reflection) = -dot(targetNormal, reflected sourceNormal).
                bool flip = flipOverride71 ?? (audit.FlipSideToCut50 ^ (audit.ProfileKind50 == "OPEN" && !normalOpposed));
                if (dir1 != null || dir2 != null) d.SetDirectionReference(dir1, dir2);
                d.ReverseDirection = reverse;
                d.FlipSideToCut = flip;
                d.BothDirections = audit.BothDirections50;
                d.FromType = recipe.FromType;
                if (recipe.FromReference != null) d.SetFromEntity(ResolveReference44(model, recipe.FromReference, plane));
                if (recipe.FromType == (int)swExtrudeFrom_e.swExtrudeFrom_Offset)
                { d.FromOffsetDistance = recipe.FromOffset; d.FromOffsetReverse = audit.FromOffsetReverse50 ^ normalOpposed; }
                d.NormalCut = recipe.NormalCut;
                d.OptimizeGeometry = recipe.OptimizeGeometry;
                // LinkToThickness applies to an extruded boss, not a cut. Some cut
                // definitions report True before edit but SolidWorks normalizes it to
                // False on ModifyDefinition; it is not a cut option to replay.
                d.FeatureScope = recipe.FeatureScope;
                d.AutoSelect = recipe.AutoSelect;
                if (recipe.ScopeBodies != null)
                {
                    object[] bodies = ResolveScopeBodies56(model, feature, audit, recipe.ScopeBodies);
                    d.FeatureScopeBodies = bodies;
                }
                ApplyDirection44(model, d, audit.D1_50, recipe.D1, plane, fallbackDepth54);
                if (audit.BothDirections50) ApplyDirection44(model, d, audit.D2_50, recipe.D2, plane);
                CreateMirrorPartPackage.LogDebug("[CUT44][ATOMIC_APPLY] feature=" + feature.Name + " reverse=" + reverse +
                    " flip=" + flip + " normalOpposed=" + normalOpposed + " directionOpposed=" + directionOpposed +
                    " endConditionPreserved=" + !fallbackDepth54.HasValue +
                    (fallbackDepth54.HasValue ? " fallback=UpToSurfaceToBlind depth_m=" + fallbackDepth54.Value.ToString("R") : ""));
                committed = feature.ModifyDefinition(d, model, null);
                // A cut made from chosen regions of its sketch has no side to flip, even
                // when that sketch also holds open figures and counts as an open profile
                // (natively seven slot regions between two open arcs). When the flip
                // derived for an open profile is refused there, the source value stays.
                if (!committed && !flipOverride71.HasValue && flip != audit.FlipSideToCut50 &&
                    audit.SelectedContours71 != null && audit.SelectedContours71.Any(c => c != null && c.Region))
                {
                    d.ReleaseSelectionAccess();
                    access = false;
                    CreateMirrorPartPackage.LogDebug("[CUT71][REGION_CUT_FLIP_REFUSED] feature=" + feature.Name + " derivedFlip=" + flip +
                        " sourceFlip=" + audit.FlipSideToCut50 + " retry=SOURCE_FLIP");
                    ApplyReflectedCut44(model, feature, profile, audit, plane, fallbackDepth54, reverseOverride54, audit.FlipSideToCut50);
                    return;
                }
                if (!committed) throw new InvalidOperationException("CUT44: atomic ModifyDefinition rejected; no ThroughAll fallback. feature=" + feature.Name);
                var read = feature.GetDefinition() as IExtrudeFeatureData2;
                if (read == null) throw new InvalidOperationException("CUT44: committed definition unavailable.");
                var differences = new List<string>();
                CheckReadback44(differences, "ReverseDirection", reverse, () => read.ReverseDirection);
                CheckReadback44(differences, "FlipSideToCut", flip, () => read.FlipSideToCut);
                CheckReadback44(differences, "BothDirections", audit.BothDirections50, () => read.BothDirections);
                CheckReadback44(differences, "FromType", recipe.FromType, () => read.FromType);
                CheckReadback44(differences, "NormalCut", recipe.NormalCut, () => read.NormalCut);
                CheckReadback44(differences, "OptimizeGeometry", recipe.OptimizeGeometry, () => read.OptimizeGeometry);
                CheckReadback44(differences, "FeatureScope", recipe.FeatureScope, () => read.FeatureScope);
                CheckReadback44(differences, "AutoSelect", recipe.AutoSelect, () => read.AutoSelect);
                CheckDirectionReadback44(differences, read, audit.D1_50, recipe.D1, fallbackDepth54);
                if (audit.BothDirections50) CheckDirectionReadback44(differences, read, audit.D2_50, recipe.D2);
                if (recipe.FromType == (int)swExtrudeFrom_e.swExtrudeFrom_Offset)
                {
                    CheckReadbackDouble44(differences, "FromOffsetDistance", recipe.FromOffset,
                        () => read.FromOffsetDistance, 1e-9);
                    CheckReadback44(differences, "FromOffsetReverse", audit.FromOffsetReverse50 ^ normalOpposed,
                        () => read.FromOffsetReverse);
                }
                CreateMirrorPartPackage.LogDebug("[CUT44][READBACK_ALL] feature=" + feature.Name +
                    " result=" + (differences.Count == 0 ? "PASS" : "FAIL") +
                    " mismatchCount=" + differences.Count +
                    (differences.Count == 0 ? "" : " differences=[" + string.Join("; ", differences) + "]"));
                if (differences.Count != 0)
                    throw new InvalidOperationException("CUT44: committed option mismatch: " + string.Join("; ", differences));
                recipe.Applied = true;
                CreateMirrorPartPackage.LogDebug("[CUT44][COMMIT_PASS] feature=" + feature.Name + " geometryOracleStillRequired=True");
            }
            finally { if (access && !committed) d.ReleaseSelectionAccess(); }
        }

        // The source API occasionally omits an Up-to-Surface target even while the
        // native feature rebuilds. A finite Blind cut is permitted only on the
        // disposable staging Part and only when its entire after-cut body is the
        // exact reflection of the source checkpoint. No feature-name rule is used.
        public static void ApplyReflectedCutWithEvidence54(ISldWorks app, ModelDoc2 model,
            Feature feature, Feature profile, CutAuditSnapshot21 audit, PlaneData plane,
            FeatureBodyState source)
        {
            if (audit == null || audit.Recipe44 == null || !audit.Recipe44.MissingSurfaceReference54)
                throw new InvalidOperationException("CUT54: fallback was not qualified by source capture.");
            if (source == null || source.BeforeBody == null || source.AfterBody == null ||
                source.RemovedBodies == null || source.RemovedBodies.Count == 0 ||
                BodyOperationsHelper.SumBodyVolumes(source.RemovedBodies) <= 0.0)
                throw new InvalidOperationException("CUT54: source before/after/removed geometry is incomplete.");
            var sketch = profile.GetSpecificFeature2() as Sketch;
            double[] origin, normal;
            CaptureSketchPlane50(sketch, out origin, out normal);
            if (origin == null || normal == null || origin.Length < 3 || normal.Length < 3)
                throw new InvalidOperationException("CUT54: reflected sketch plane is unavailable.");
            var reflectedVertices = new List<double[]>();
            foreach (Body2 removed in source.RemovedBodies)
            {
                object[] vertices = removed.GetVertices() as object[];
                if (vertices == null || vertices.Length == 0)
                    throw new InvalidOperationException("CUT54: removed-body vertices are unavailable; bounding-box corners are not a valid cut depth.");
                foreach (Vertex vertex in vertices)
                {
                    double[] point = vertex.GetPoint() as double[];
                    if (point == null || point.Length < 3)
                        throw new InvalidOperationException("CUT54: removed-body vertex coordinates unavailable.");
                    double[] reflected = BodyOperationsHelper.ReflectPointAcrossPlane(point, plane);
                    if (reflected == null || reflected.Length < 3)
                        throw new InvalidOperationException("CUT54: removed-body reflection failed.");
                    reflectedVertices.Add(reflected);
                }
            }
            double[] signedSpan = CutBlindFallback54.SignedSpanFromReflectedVertices(
                origin, normal, reflectedVertices);
            double depth = CutBlindFallback54.ExactDepthFromSignedSpan(signedSpan);
            int expectedMaterialSide = signedSpan[1] >= Math.Abs(signedSpan[0]) ? 1 : -1;
            double sheetThickness = double.NaN;
            for (Feature current = model.FirstFeature() as Feature; current != null;
                current = current.GetNextFeature() as Feature)
            {
                if (!string.Equals(current.GetTypeName2(), "SheetMetal", StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    SheetMetalFeatureData sheet = current.GetDefinition() as SheetMetalFeatureData;
                    if (sheet != null && sheet.Thickness > 0.0)
                    {
                        sheetThickness = sheet.Thickness;
                        break;
                    }
                }
                catch { /* Diagnostic only; geometry proof below remains mandatory. */ }
            }
            CreateMirrorPartPackage.LogDebug("[CUT55][MATERIAL_DIRECTION] feature=" + feature.Name +
                " supportOrigin_m=" + string.Join(",", origin.Take(3).Select(value => value.ToString("R"))) +
                " supportNormal=" + string.Join(",", normal.Take(3).Select(value => value.ToString("R"))) +
                " removedSignedMin_m=" + signedSpan[0].ToString("R") +
                " removedSignedMax_m=" + signedSpan[1].ToString("R") +
                " expectedMaterialSide=" + (expectedMaterialSide > 0 ? "+SKETCH_NORMAL" : "-SKETCH_NORMAL") +
                " measuredCutDepth_m=" + depth.ToString("R") +
                " sheetThickness_m=" + (double.IsNaN(sheetThickness) ? "UNAVAILABLE" : sheetThickness.ToString("R")) +
                " depthIsSheetThickness=False vertexCount=" + reflectedVertices.Count);
            BodyTransformResult expected = BodyOperationsHelper.MirrorBodyStrict(app, source.AfterBody, plane);
            if (expected == null || !expected.Success || expected.Body == null)
                throw new InvalidOperationException("CUT54: reflected source after-body unavailable.");
            BodyTransformResult expectedBefore = BodyOperationsHelper.MirrorBodyStrict(app, source.BeforeBody, plane);
            if (expectedBefore == null || !expectedBefore.Success || expectedBefore.Body == null)
                throw new InvalidOperationException("CUT54: reflected source before-body unavailable.");

            bool normalOpposed = CutOrientation44.Opposed(
                CutOrientation44.Reflect(audit.Recipe44.SketchNormal, plane.Normal), normal);
            bool firstReverse = audit.ReverseDirection50 ^ normalOpposed;
            string lastFailure = "No candidate was evaluated.";
            for (int candidate = 0; candidate < 2; candidate++)
            {
                bool reverse = candidate == 0 ? firstReverse : !firstReverse;
                string label = feature.Name + "_CUT54_CANDIDATE_" + candidate;
                try
                {
                    CreateMirrorPartPackage.LogDebug("[CUT54][TRY] feature=" + feature.Name +
                        " candidate=" + candidate + " reverse=" + reverse +
                        " depth_m=" + depth.ToString("R") + " sourceEnd=UpToSurface appliedEnd=Blind");
                    ApplyReflectedCut44(model, feature, profile, audit, plane, depth, reverse);
                    model.ForceRebuild3(false);
                    bool warning;
                    int errorCode = feature.GetErrorCode2(out warning);
                    if (errorCode != 0 && !warning)
                        throw new InvalidOperationException("rebuild error=" + errorCode);
                    string captureError;
                    Body2 actual = BodyOperationsHelper.GetSolidBodyCopyStrict(model, out captureError);
                    if (actual == null)
                        throw new InvalidOperationException("after-body unavailable: " + captureError);
                    BodyBooleanResult missing = BodyOperationsHelper.BooleanCutStrict(
                        expected.Body, actual, label + "_MISSING");
                    BodyBooleanResult extra = BodyOperationsHelper.BooleanCutStrict(
                        actual, expected.Body, label + "_EXTRA");
                    BodyBooleanResult removedActual = BodyOperationsHelper.BooleanCutStrict(
                        expectedBefore.Body, actual, label + "_ACTUAL_REMOVED");
                    double removedVolume = double.NaN;
                    double[] removedCentroid = null;
                    bool measuredRemoval = removedActual.Success &&
                        BodyOperationsHelper.TryGetBodiesVolumeCentroid(
                            removedActual.Bodies, out removedVolume, out removedCentroid);
                    if (!measuredRemoval)
                    {
                        removedVolume = removedActual.Success
                            ? BodyOperationsHelper.SumBodyVolumes(removedActual.Bodies) : double.NaN;
                        removedCentroid = null;
                    }
                    double actualSignedCentroid = removedCentroid == null ? double.NaN :
                        (removedCentroid[0] - origin[0]) * normal[0] +
                        (removedCentroid[1] - origin[1]) * normal[1] +
                        (removedCentroid[2] - origin[2]) * normal[2];
                    bool cutsExpectedMaterialSide = removedVolume > 0.0 &&
                        !double.IsNaN(actualSignedCentroid) &&
                        actualSignedCentroid * expectedMaterialSide > 1e-9;
                    bool exact = missing.Success && extra.Success && missing.Bodies != null &&
                        extra.Bodies != null && removedActual.Success &&
                        missing.Bodies.Count == 0 && extra.Bodies.Count == 0 && removedVolume > 0.0;
                    CreateMirrorPartPackage.LogDebug("[CUT54][GEOMETRY] feature=" + feature.Name +
                        " candidate=" + candidate + " reverse=" + reverse +
                        " missingBoolean=" + missing.Success + " missingCount=" +
                        (missing.Bodies == null ? -1 : missing.Bodies.Count) +
                        " extraBoolean=" + extra.Success + " extraCount=" +
                        (extra.Bodies == null ? -1 : extra.Bodies.Count) +
                        " removedActual_m3=" + removedVolume.ToString("R") +
                        " cutsMaterial=" + (removedVolume > 0.0) +
                        " actualRemovedSignedCentroid_m=" + actualSignedCentroid.ToString("R") +
                        " cutsExpectedMaterialSide=" + cutsExpectedMaterialSide +
                        " result=" + (exact ? "PASS" : "FAIL"));
                    if (exact)
                    {
                        audit.Recipe44.Applied = true;
                        CreateMirrorPartPackage.LogDebug("[CUT54][ACCEPT] feature=" + feature.Name +
                            " originalEnd=UpToSurface outputEnd=Blind oracle=EXACT_AFTER_BODY");
                        return;
                    }
                    lastFailure = "after-body Boolean difference remains";
                    // When the first candidate demonstrably cuts material on the
                    // expected side, reversing cannot repair a depth/profile mismatch and may make
                    // SolidWorks reject a second ModifyDefinition on this feature.
                    if (candidate == 0 && cutsExpectedMaterialSide)
                    {
                        CreateMirrorPartPackage.LogDebug("[CUT55][DIRECTION_DECISION] feature=" +
                            feature.Name + " action=KEEP_MATERIAL_SIDE reverseTrialSkipped=True" +
                            " reason=firstCandidateRemovedMaterialOnExpectedSideButGeometryDiffers");
                        break;
                    }
                }
                catch (Exception error)
                {
                    lastFailure = error.GetType().Name + ": " + error.Message;
                    CreateMirrorPartPackage.LogDebug("[CUT54][REJECT] feature=" + feature.Name +
                        " candidate=" + candidate + " reason=" + lastFailure);
                }
                audit.Recipe44.Applied = false;
            }
            throw new InvalidOperationException("CUT54: no finite-depth direction reproduces the reflected source cut. " + lastFailure);
        }
    }
}
