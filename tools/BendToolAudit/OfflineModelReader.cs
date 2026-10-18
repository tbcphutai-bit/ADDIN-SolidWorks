using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Runtime.Serialization.Json;
using System.Xml;
using System.Xml.Linq;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.Tools.BendToolAudit
{
    internal static class OfflineModelReader
    {
        internal const string NativeSourceContract = "NativeAnalyticShellV2";
        internal static string Payload(byte[] capture, string sourceSha, string machine, string setup) =>
            ToolEvidenceCanonical.Hash("OfflineModelCaptureV1", PlacementSourceIo.Hash(capture), sourceSha, machine, setup);

        // Rehydrate the saved managed DTO, then run the ORIGINAL Phase 3D.1
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
        // Keep the original numeric token until its field type is known;
        // reconstruct binary64 directly without an intermediate Decimal
        // conversion or any rounding/normalization of saved source values.
        private sealed class CaptureNumber
        {
            internal string Token;
        }
        private static object JsonValue(XElement e)
        {
            switch ((string)e.Attribute("type"))
            {
                case "object": return e.Elements().ToDictionary(x => (string)x.Attribute("item") ?? x.Name.LocalName, JsonValue, StringComparer.Ordinal);
                case "array": return e.Elements().Select(JsonValue).ToArray();
                case "number": return new CaptureNumber { Token = e.Value };
                case "string": return e.Value;
                case "boolean": return XmlConvert.ToBoolean(e.Value);
                case "null": return null;
                default: throw new InvalidDataException("INVALID_CAPTURE_JSON_TYPE");
            }
        }
        private static object Get(IDictionary<string, object> d, string k) => d.TryGetValue(k, out var x) ? x : throw new InvalidDataException("MISSING_CAPTURE_FIELD:" + k);
        private static string Text(IDictionary<string, object> d, string k) => Get(d, k) as string ?? throw new InvalidDataException("MISSING_CAPTURE_TEXT:" + k);
        private static bool Bool(IDictionary<string, object> d, string k) => Get(d, k) is bool x ? x : throw new InvalidDataException("MISSING_CAPTURE_BOOLEAN:" + k);
        private static double Number(IDictionary<string, object> d, string k)
        {
            object x = Get(d, k);
            if (!(x is CaptureNumber number)) throw new InvalidDataException("MISSING_CAPTURE_NUMBER:" + k);
            double n = double.Parse(number.Token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
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
        {
            if (!(n is CaptureNumber number) || !int.TryParse(number.Token, System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out int result)) throw new InvalidDataException("INVALID_CAPTURE_INCIDENCE");
            return result;
        }
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
                var t = Object(v); var edges = List(t, "Edges").Select(Edge).ToArray();
                var faces = List(t, "Faces").Select(NativeFace).ToArray();
                int edgeCount = Integer(t, "NativeEdgeCount"), faceCount = Integer(t, "NativeFaceCount");
                if (edgeCount != edges.Length || edgeCount > 24000 || faceCount != faces.Length || faceCount > 12000 ||
                    edges.Select(e => e.Id).Distinct().Count() != edges.Length)
                    throw new InvalidDataException("CAPTURE_NATIVE_EDGE_IDENTITY_INVALID");
                if (Text(t, "BodyId") != Text(d, "Id")) throw new InvalidDataException("CAPTURE_NATIVE_BODY_IDENTITY_MISMATCH");
                // V2 requires explicit circle plane/sense for EVERY circular
                // edge, including arcs handled by an earlier compiler path.
                // Missing legacy metadata is never inferred from endpoints.
                if (!t.TryGetValue("Circles", out var circleValues) || !(circleValues is object[] values))
                    throw new InvalidDataException("CAPTURE_NATIVE_CIRCLE_METADATA_MISSING");
                var circles = values.Select(Circle).ToArray();
                var byId = edges.ToDictionary(e => e.Id);
                if (circles.Select(c => c.EdgeId).Distinct().Count() != circles.Length ||
                    circles.Any(c => !byId.ContainsKey(c.EdgeId) || byId[c.EdgeId].Kind != 2))
                    throw new InvalidDataException("CAPTURE_NATIVE_CIRCLE_EDGE_BINDING_INVALID");
                if (circles.Length != edges.Count(e => e.Kind == 2))
                    throw new InvalidDataException("CAPTURE_NATIVE_CIRCLE_METADATA_INCOMPLETE");
                if (edges.Any(e => e.Kind == 2 && e.Arc == null)) throw new InvalidDataException("CAPTURE_NATIVE_CIRCLE_ARC_MISSING");
                t.TryGetValue("SourceSha256", out var hash); string sourceHash = hash as string;
                if (!ToolDatumEvidence.IsSha256(sourceHash))
                    throw new InvalidDataException("CAPTURE_NATIVE_SOURCE_HASH_REQUIRED");
                t.TryGetValue("Error", out var error);
                if (error != null && !(error is string)) throw new InvalidDataException("INVALID_CAPTURE_NATIVE_ERROR");
                // Preserve source order and raw values. The production DTO is
                // the sole canonical V2 hash implementation; no rehash/repair
                // of the saved packet, ignored hash or V1 downgrade is allowed.
                topology = new NativeEnvelopeBody(Text(t, "BodyId"), Bool(t, "SolidBody"), faceCount,
                    edgeCount, edges, faces, error as string, circles);
                if (!string.Equals(sourceHash, topology.SourceSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("CAPTURE_NATIVE_SOURCE_HASH_MISMATCH");
            }
            return new PlacementBodySnapshot(Text(d, "Id"), List(d, "Faces").Select(Face), List(d, "Edges").Select(Edge), topology);
        }
        private static NativeCircleDefinition Circle(object value)
        {
            var d = Object(value); var axis = Vector(Get(d, "Axis"));
            if (Math.Abs(axis.Length - 1) > 1e-12) throw new InvalidDataException("CAPTURE_NATIVE_CIRCLE_AXIS_INVALID");
            return new NativeCircleDefinition(Integer(d, "EdgeId"), axis, Bool(d, "CurveAndEdgeSameSense"));
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
            if (!Environment.Is64BitProcess) throw new InvalidDataException("CAPTURE_REQUIRES_X64_PROCESS");
            if (bytes == null || bytes.Length > 8 * 1024 * 1024) throw new InvalidDataException("CAPTURE_JSON_SIZE_LIMIT");
            IDictionary<string, object> d;
            try
            {
                int offset = bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191 ? 3 : 0;
                using (var reader = JsonReaderWriterFactory.CreateJsonReader(bytes, offset, bytes.Length - offset, System.Text.Encoding.UTF8,
                    new XmlDictionaryReaderQuotas { MaxDepth = 100, MaxStringContentLength = 8 * 1024 * 1024, MaxArrayLength = 8 * 1024 * 1024, MaxNameTableCharCount = 65536 }, null))
                    d = Object(JsonValue(XElement.Load(reader)));
            }
            catch (Exception ex) when (ex is XmlException || ex is ArgumentException)
            { throw new InvalidDataException("INVALID_CAPTURE_JSON", ex); }
            var bodies = List(d, "Bodies").Select(Body).ToArray(); var bends = List(d, "Bends").Select(Bend).ToArray();
            if (bodies.Length > 64 || bends.Length == 0 || bends.Length > 256 || bends.Select(b => b.FeatureId).Distinct().Count() != bends.Length ||
                bodies.Select(b => b.Id).Distinct().Count() != bodies.Length || bends.Any(b => !bodies.Any(p => p.Id == b.BodyId)))
                throw new InvalidDataException("INVALID_CAPTURE_IDENTITIES_OR_BUDGET");
            return new PlacementModelCapture(Text(d, "SnapshotId"), Integer(d, "UpdateStamp"), Text(d, "Configuration"), bodies, bends,
                Bool(d, "CompleteBodyCoverage"), Bool(d, "SimulationOnly"));
        }
    }
}
