using System;
using System.Collections.Generic;
using System.Linq;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendEnvelope
{
    internal static class NativeEnvelopeEdgePolicy
    {
        // ICurveParamData.StartPoint/EndPoint are curve-oriented. A false
        // Sense reverses them relative to IEdge; EdgeInFaceSense is relative
        // to that IEdge, not to the underlying parameterized curve.
        internal static Tuple<PlacementVector3,PlacementVector3> EdgeEndpoints(PlacementVector3 start,PlacementVector3 end,bool curveAndEdgeSameSense) =>
            curveAndEdgeSameSense ? Tuple.Create(start,end) : Tuple.Create(end,start);
    }
    internal sealed class NativeLoopEdge
    {
        public int EdgeId { get; }
        public bool Forward { get; }
        internal NativeLoopEdge(int edge, bool forward) { EdgeId = edge; Forward = forward; }
    }
    internal sealed class NativeFaceLoop
    {
        public bool IsOuter { get; }
        public bool Singular { get; }
        public IReadOnlyList<NativeLoopEdge> Edges { get; }
        internal NativeFaceLoop(bool outer, bool singular, IEnumerable<NativeLoopEdge> edges)
        { IsOuter = outer; Singular = singular; Edges = PlacementLists.Copy(edges); }
    }
    internal sealed class NativeEnvelopeFace
    {
        public int Id { get; }
        public int Kind { get; }
        public PlacementVector3 SurfaceNormal { get; }
        public PlacementVector3 OutwardNormal => SurfaceNormal * (ReversedSurfaceSense ? -1 : 1);
        public PlacementVector3 PointMm { get; }
        public PlacementVector3 Axis { get; }
        public double RadiusMm { get; }
        public bool ReversedSurfaceSense { get; }
        public IReadOnlyList<NativeFaceLoop> Loops { get; }
        internal NativeEnvelopeFace(int id, int kind, PlacementVector3 normal, PlacementVector3 point, PlacementVector3 axis,
            double radius, bool reversed, IEnumerable<NativeFaceLoop> loops)
        { Id = id; Kind = kind; SurfaceNormal = normal; PointMm = point; Axis = axis; RadiusMm = radius; ReversedSurfaceSense = reversed; Loops = PlacementLists.Copy(loops); }
    }
    // Contains no native identity pointers, COM objects, delegates or live
    // geometry evaluators. A whole source shell is captured, not a silhouette.
    internal sealed class NativeEnvelopeBody
    {
        public string BodyId { get; }
        public bool SolidBody { get; }
        public int NativeFaceCount { get; }
        public int NativeEdgeCount { get; }
        public IReadOnlyList<PlacementEdgeSnapshot> Edges { get; }
        public IReadOnlyList<NativeEnvelopeFace> Faces { get; }
        public string Error { get; }
        public string SourceSha256 { get; }
        internal NativeEnvelopeBody(string body, bool solid, int faceCount, int edgeCount, IEnumerable<PlacementEdgeSnapshot> edges,
            IEnumerable<NativeEnvelopeFace> faces, string error = null)
        {
            BodyId = body; SolidBody = solid; NativeFaceCount = faceCount; NativeEdgeCount = edgeCount;
            Edges = PlacementLists.Copy(edges); Faces = PlacementLists.Copy(faces); Error = error;
            SourceSha256 = ToolEvidenceCanonical.Hash("NativeAnalyticShellV1", body, solid.ToString(), error,
                faceCount.ToString(System.Globalization.CultureInfo.InvariantCulture), edgeCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ToolEvidenceCanonical.Hash(Edges.Select(e => ToolEvidenceCanonical.Hash(e.Id.ToString(), e.Kind.ToString(),
                    EnvelopeBinding.Vector(e.A), EnvelopeBinding.Vector(e.B), e.Arc == null ? null : ToolEvidenceCanonical.Hash(
                        EnvelopeBinding.Vector(e.Arc.CenterMm), EnvelopeBinding.Vector(e.Arc.MidMm), ToolEvidenceCanonical.Number(e.Arc.RadiusMm), e.Arc.FullCircle.ToString()),
                    string.Join(",", e.Faces))).ToArray()),
                ToolEvidenceCanonical.Hash(Faces.Select(f => ToolEvidenceCanonical.Hash(f.Id.ToString(), f.Kind.ToString(),
                    EnvelopeBinding.Vector(f.SurfaceNormal), EnvelopeBinding.Vector(f.PointMm), EnvelopeBinding.Vector(f.Axis),
                    ToolEvidenceCanonical.Number(f.RadiusMm), f.ReversedSurfaceSense.ToString(),
                    ToolEvidenceCanonical.Hash(f.Loops.Select(l => ToolEvidenceCanonical.Hash(l.IsOuter.ToString(), l.Singular.ToString(),
                        string.Join(",", l.Edges.Select(e => e.EdgeId + ":" + e.Forward)))).ToArray()))).ToArray()));
        }
    }
}
