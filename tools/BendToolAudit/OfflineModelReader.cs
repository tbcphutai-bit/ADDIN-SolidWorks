using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;

namespace ADDIN.Tools.BendToolAudit
{
    internal static class OfflineModelReader
    {
        internal static string Payload(byte[] capture, string sourceSha, string machine, string setup) =>
            ToolEvidenceCanonical.Hash("OfflineModelCaptureV1", PlacementSourceIo.Hash(capture), sourceSha, machine, setup);

        // Rehydrate the saved managed DTO, then run the ORIGINAL Phase 3D
        // compiler. Serialized certificates, ready flags and source hashes
        // never replace geometric validation. This is an as-of-saved-file
        // audit, not a live SOLIDWORKS capture lease or a collision result.
        internal static AuditModelContext Read(AuditIntake intake, string root, ToolReviewVerifier verifier, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(intake.ModelCapturePath)) return null;
            try
            {
                if (string.IsNullOrWhiteSpace(intake.ModelSourcePath) || string.IsNullOrWhiteSpace(intake.MachineId) || string.IsNullOrWhiteSpace(intake.SetupId))
                    throw new InvalidDataException("MISSING_OFFLINE_MODEL_SOURCE_OR_SETUP_SCOPE");
                var capture = PlacementSourceIo.Read(PlacementSourceIo.Resolve(root, intake.ModelCapturePath), 8 * 1024 * 1024, token);
                var source = PlacementSourceIo.Read(PlacementSourceIo.Resolve(root, intake.ModelSourcePath), 256 * 1024 * 1024, token);
                string sourceSha = PlacementSourceIo.Hash(source);
                if (sourceSha != intake.ModelSourceSha256) throw new InvalidDataException("OFFLINE_MODEL_SOURCE_SHA_MISMATCH");
                var review = verifier.Verify("OfflineModelCapture", Payload(capture, sourceSha, intake.MachineId, intake.SetupId), intake.ModelCaptureReview, root, token);
                if (!review.RecordVerified) return new AuditModelContext { CaptureReview = review, Diagnostic = review.Reason };
                var model = Decode(capture);
                return new AuditModelContext { Model = model, CaptureReview = review,
                    Envelopes = model.Bends.Select(b => PrismaticEnvelopeCompiler.Read(model, b, token)).ToArray() };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is ArgumentException || ex is InvalidOperationException || ex is UnauthorizedAccessException || ex is InvalidCastException || ex is OverflowException || ex is FormatException)
            { return new AuditModelContext { Diagnostic = ex is InvalidDataException ? ex.Message : "INVALID_OFFLINE_MODEL_PACKET" }; }
        }
        private static IDictionary<string, object> Object(object x) => x as IDictionary<string, object> ?? throw new InvalidDataException("INVALID_CAPTURE_OBJECT");
        private static object Get(IDictionary<string, object> d, string k) => d.TryGetValue(k, out var x) ? x : throw new InvalidDataException("MISSING_CAPTURE_FIELD:" + k);
        private static string Text(IDictionary<string, object> d, string k) => Get(d, k) as string ?? throw new InvalidDataException("MISSING_CAPTURE_TEXT:" + k);
        private static bool Bool(IDictionary<string, object> d, string k) => Get(d, k) is bool x ? x : throw new InvalidDataException("MISSING_CAPTURE_BOOLEAN:" + k);
        private static double Number(IDictionary<string, object> d, string k)
        {
            object x = Get(d, k);
            if (!(x is int || x is long || x is double || x is decimal)) throw new InvalidDataException("MISSING_CAPTURE_NUMBER:" + k);
            double n = Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture);
            if (!ADDIN.Commands.BendTools.ToolPoint.Finite(n)) throw new InvalidDataException("NONFINITE_CAPTURE_NUMBER:" + k);
            return n;
        }
        private static int Integer(IDictionary<string, object> d, string k)
        {
            double n = Number(d, k);
            if (n != Math.Truncate(n) || n < int.MinValue || n > int.MaxValue) throw new InvalidDataException("INVALID_CAPTURE_INTEGER:" + k);
            return (int)n;
        }
        private static object[] List(IDictionary<string, object> d, string k) => Get(d, k) is object[] x ? x : throw new InvalidDataException("MISSING_CAPTURE_ARRAY:" + k);
        private static PlacementVector3 Vector(object value)
        { var d = Object(value); return new PlacementVector3(Number(d, "X"), Number(d, "Y"), Number(d, "Z")); }
        private static PlacementEdgeSnapshot Edge(object value)
        {
            var d = Object(value); PlacementArc3D arc = null;
            if (d.TryGetValue("Arc", out var x) && x != null)
            { var a = Object(x); arc = new PlacementArc3D(Vector(Get(a, "CenterMm")), Vector(Get(a, "MidMm")), Number(a, "RadiusMm"), Bool(a, "FullCircle")); }
            return new PlacementEdgeSnapshot(Integer(d, "Id"), Integer(d, "Kind"), Vector(Get(d, "A")), Vector(Get(d, "B")),
                List(d, "Faces").Select(i => StrictInt(i)), arc);
        }
        private static int StrictInt(object n)
        { if (!(n is int)) throw new InvalidDataException("INVALID_CAPTURE_INCIDENCE"); return (int)n; }
        private static PlacementFaceSnapshot Face(object value)
        {
            var d = Object(value); return new PlacementFaceSnapshot(Integer(d, "Id"), Integer(d, "Kind"), Vector(Get(d, "Normal")),
                Vector(Get(d, "PointMm")), Vector(Get(d, "Axis")), List(d, "Edges").Select(StrictInt));
        }
        private static NativeEnvelopeFace NativeFace(object value)
        {
            var d = Object(value);
            var loops = List(d, "Loops").Select(v => {
                var l = Object(v); return new NativeFaceLoop(Bool(l, "IsOuter"), Bool(l, "Singular"), List(l, "Edges").Select(w => {
                    var e = Object(w); return new NativeLoopEdge(Integer(e, "EdgeId"), Bool(e, "Forward")); })); });
            return new NativeEnvelopeFace(Integer(d, "Id"), Integer(d, "Kind"), Vector(Get(d, "SurfaceNormal")), Vector(Get(d, "PointMm")),
                Vector(Get(d, "Axis")), Number(d, "RadiusMm"), Bool(d, "ReversedSurfaceSense"), loops);
        }
        private static PlacementBodySnapshot Body(object value)
        {
            var d = Object(value); NativeEnvelopeBody topology = null;
            if (d.TryGetValue("EnvelopeTopology", out var v) && v != null)
            {
                var t = Object(v); topology = new NativeEnvelopeBody(Text(t, "BodyId"), Bool(t, "SolidBody"), Integer(t, "NativeFaceCount"),
                    Integer(t, "NativeEdgeCount"), List(t, "Edges").Select(Edge), List(t, "Faces").Select(NativeFace),
                    t.TryGetValue("Error", out var error) ? error as string : null);
                if (t.TryGetValue("SourceSha256", out var hash) && (string)hash != topology.SourceSha256)
                    throw new InvalidDataException("CAPTURE_NATIVE_SOURCE_HASH_MISMATCH");
            }
            return new PlacementBodySnapshot(Text(d, "Id"), List(d, "Faces").Select(Face), List(d, "Edges").Select(Edge), topology);
        }
        private static PlacementBendSnapshot Bend(object value)
        {
            var d = Object(value); var f = Object(Get(d, "Frame"));
            var frame = new BendWorkFrame(Text(f, "FrameId"), Vector(Get(f, "OriginMm")), Vector(Get(f, "X")), Vector(Get(f, "Y")), Vector(Get(f, "BendAxis")));
            double? thickness = Get(d, "ThicknessMm") == null ? (double?)null : Number(d, "ThicknessMm");
            return new PlacementBendSnapshot(Integer(d, "FeatureId"), Text(d, "BodyId"), frame, thickness, Number(d, "BendMinMm"), Number(d, "BendMaxMm"));
        }
        internal static PlacementModelCapture Decode(byte[] bytes)
        {
            var d = Object(new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024, RecursionLimit = 100 }.DeserializeObject(System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF')));
            var bodies = List(d, "Bodies").Select(Body).ToArray(); var bends = List(d, "Bends").Select(Bend).ToArray();
            if (bodies.Length > 64 || bends.Length == 0 || bends.Length > 256 || bends.Select(b => b.FeatureId).Distinct().Count() != bends.Length ||
                bodies.Select(b => b.Id).Distinct().Count() != bodies.Length || bends.Any(b => !bodies.Any(p => p.Id == b.BodyId)))
                throw new InvalidDataException("INVALID_CAPTURE_IDENTITIES_OR_BUDGET");
            return new PlacementModelCapture(Text(d, "SnapshotId"), Integer(d, "UpdateStamp"), Text(d, "Configuration"), bodies, bends,
                Bool(d, "CompleteBodyCoverage"), Bool(d, "SimulationOnly"));
        }
    }
}
