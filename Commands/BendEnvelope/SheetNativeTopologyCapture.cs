using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using ADDIN.Commands.BendPlacement;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands.BendEnvelope
{
    internal sealed class NativeEnvelopeCaptureBundle
    {
        public IReadOnlyList<NativeEnvelopeBody> Bodies { get; }
        public string SnapshotKey { get; }
        public int CaptureThreadId { get; }
        internal NativeEnvelopeCaptureBundle(string key, IEnumerable<NativeEnvelopeBody> bodies)
        { SnapshotKey = key; Bodies = PlacementLists.Copy(bodies); CaptureThreadId = Thread.CurrentThread.ManagedThreadId; }
    }
    internal static class SheetNativeTopologyCapture
    {
        private static readonly ConditionalWeakTable<BendMinimumContext, NativeEnvelopeCaptureBundle> Cache = new ConditionalWeakTable<BendMinimumContext, NativeEnvelopeCaptureBundle>();
        private static int captures, wrongThread;
        private static long captureTicks;
        internal static int BodyCaptureCount => Volatile.Read(ref captures);
        internal static int WrongThreadCount => Volatile.Read(ref wrongThread);
        internal static double TotalCaptureMilliseconds => 1000.0*Interlocked.Read(ref captureTicks)/Stopwatch.Frequency;
        private static long Key(object obj)
        { var p = Marshal.GetIUnknownForObject(obj); try { return p.ToInt64(); } finally { Marshal.Release(p); } }
        internal static NativeEnvelopeCaptureBundle GetOrCapture(BendCheckItem item, string snapshotKey, IList<BendPanelBody> bodies)
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            { Interlocked.Increment(ref wrongThread); throw new InvalidOperationException("Native envelope capture requires STA."); }
            if (item.Context == null) throw new InvalidOperationException("Missing original capture context.");
            NativeEnvelopeCaptureBundle old;
            if (Cache.TryGetValue(item.Context, out old))
            {
                if (old.SnapshotKey == snapshotKey) return old;
                Cache.Remove(item.Context);
            }
            var result = new NativeEnvelopeCaptureBundle(snapshotKey, bodies.Select((b, i) => Copy(b, "body:" + i,item.Model)));
            Cache.Add(item.Context, result); return result;
        }
        private static PlacementVector3 V(double[] a, int start = 0, bool metres = false) => metres ?
            PlacementVector3.Metres(a[start], a[start + 1], a[start + 2]) : new PlacementVector3(a[start], a[start + 1], a[start + 2]);
        private static NativeEnvelopeBody Copy(BendPanelBody body, string id,ModelDoc2 model)
        {
            Interlocked.Increment(ref captures);
            var timer=Stopwatch.StartNew();
            try
            {
                var native = body.NativeBody;
                var faces = native.GetFaces() as object[];
                var edges = native.GetEdges() as object[];
                if (faces == null || edges == null || faces.Length > 12000 || edges.Length > 24000)
                    return new NativeEnvelopeBody(id, false, 0, 0, null, null, "INCOMPLETE_NATIVE_BREP_CAPTURE");
                var faceIds = new NativeEntityReferenceIndex<object>(faces,Key,f=>BendPanelGeometryReader.PersistentKey(model,f));
                var edgeIds = new NativeEntityReferenceIndex<object>(edges,Key,e=>BendPanelGeometryReader.PersistentKey(model,e));
                var edgeData = new List<PlacementEdgeSnapshot>(); var faceData = new List<NativeEnvelopeFace>();var circles=new List<NativeCircleDefinition>();
                for (int i = 0; i < edges.Length; i++)
                {
                    var edge = (Edge)edges[i]; var curve = edge.GetCurve() as Curve; var p = edge.GetCurveParams3() as CurveParamData;
                    if (curve == null || p == null) throw new InvalidOperationException("MISSING_NATIVE_CURVE");
                    int kind = curve.IsLine() ? 1 : curve.IsCircle() ? 2 : 0;
                    if(kind==2)
                    {
                        var circle=(double[])curve.CircleParams;
                        circles.Add(new NativeCircleDefinition(i,V(circle,3).Unit,p.Sense));
                    }
                    var endpoints=NativeEnvelopeEdgePolicy.EdgeEndpoints(V((double[])p.StartPoint,metres:true),V((double[])p.EndPoint,metres:true),p.Sense);
                    var adjacent = (edge.GetTwoAdjacentFaces2() as object[] ?? new object[0]).Where(f => f != null).Select(faceIds.Resolve).ToArray();
                    edgeData.Add(new PlacementEdgeSnapshot(i, kind, endpoints.Item1, endpoints.Item2, adjacent,
                        kind == 2 ? BendPlacementNativeCapture.TryReadArc(edge, curve, p) : null));
                }
                for (int i = 0; i < faces.Length; i++)
                {
                    var face = (Face2)faces[i]; var surface = face.GetSurface() as Surface;
                    int kind = 0; var normal = new PlacementVector3(); var point = normal; var axis = normal; double radius = 0;
                    if (surface?.IsPlane() == true)
                    { var data = (double[])surface.PlaneParams; kind = 1; normal = V(data).Unit; point = V(data, 3, true); }
                    else if (surface?.IsCylinder() == true)
                    { var data = (double[])surface.CylinderParams; kind = 2; point = V(data, metres: true); axis = V(data, 3).Unit; radius = data[6] * 1000; }
                    var loops = new List<NativeFaceLoop>();
                    foreach (Loop2 loop in face.GetLoops() as object[] ?? new object[0])
                    {
                        var directed = (loop.GetEdges() as object[] ?? new object[0]).Cast<Edge>().Select(e =>
                            new NativeLoopEdge(edgeIds.Resolve(e), e.EdgeInFaceSense(face))).ToArray();
                        loops.Add(new NativeFaceLoop(loop.IsOuter(), loop.IsSingular(), directed));
                    }
                    faceData.Add(new NativeEnvelopeFace(i, kind, normal, point, axis, radius, face.FaceInSurfaceSense(), loops));
                }
                return new NativeEnvelopeBody(id, native.GetType() == 0, native.GetFaceCount(), native.GetEdgeCount(), edgeData, faceData,circles:circles);
            }
            catch (Exception ex)
            { BendNativeCaptureDiagnostics.Failure(ex,"NativeEnvelope.Copy."+id);
                return new NativeEnvelopeBody(id, false, 0, 0, null, null, "NATIVE_TOPOLOGY_CAPTURE_FAILED:" + ex.GetType().Name); }
            finally {Interlocked.Add(ref captureTicks,timer.ElapsedTicks);}
        }
    }
}
