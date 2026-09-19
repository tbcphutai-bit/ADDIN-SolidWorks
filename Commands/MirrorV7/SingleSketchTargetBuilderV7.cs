using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.MirrorV7
{
    public static class SingleSketchTargetBuilderV7
    {
        public static List<SketchPointTargetV7> Capture(ModelDoc2 workingDocument, Sketch sketch, IMathUtility math, PartReflectionTransformV7 reflection, string sketchName)
        {
            if (workingDocument == null || sketch == null || math == null || reflection == null) throw new ArgumentNullException();
            MathTransform modelToSketch = sketch.ModelToSketchTransform as MathTransform;
            MathTransform sketchToModel = modelToSketch == null ? null : modelToSketch.IInverse();
            if (modelToSketch == null || sketchToModel == null) throw new InvalidOperationException("Sketch transforms unavailable.");
            object[] rawPoints = sketch.GetSketchPoints2() as object[];
            if (rawPoints == null || rawPoints.Length == 0) throw new InvalidOperationException("No points available for mutation.");
            List<SketchPointTargetV7> targets = new List<SketchPointTargetV7>(); HashSet<string> identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (object raw in rawPoints)
            {
                SketchPoint point = raw as SketchPoint; if (point == null) throw new InvalidOperationException("Unexpected object in sketch point enumeration.");
                if (point.Type == (int)swSketchPointType_e.swSketchPointType_Origin || point.Type == (int)swSketchPointType_e.swSketchPointType_External || point.Type == (int)swSketchPointType_e.swSketchPointType_Datum) throw new InvalidOperationException("Origin/external/datum point is not eligible for mutation.");
                string identity = GetIdentity(point); if (!identities.Add(identity)) throw new InvalidOperationException("Duplicate logical sketch point identity.");
                double[] beforeSketch = { point.X, point.Y, point.Z }; double[] beforeModel = SketchMutationMathV7.Transform(math, sketchToModel, beforeSketch); double[] targetModel = reflection.ReflectPoint(beforeModel); double[] targetSketch = SketchMutationMathV7.Transform(math, modelToSketch, targetModel);
                if (Math.Abs(targetSketch[2]) > SketchMutationMathV7.PositionToleranceMetres) throw new InvalidOperationException("Reflected point leaves current sketch support plane.");
                PersistReferenceV7 reference = PersistentReferenceServiceV7.Capture(workingDocument, point, sketchName, "PHASE6C1_POINT"); if (reference == null) throw new InvalidOperationException("Point persistent reference unavailable.");
                targets.Add(new SketchPointTargetV7 { Reference = reference, BeforeSketch = beforeSketch, BeforeModel = beforeModel, TargetSketch = targetSketch, TargetModel = targetModel });
            }
            return targets;
        }
        private static string GetIdentity(SketchPoint point)
        {
            try
            {
                object raw = point.GetID();
                Array values = raw as Array;
                if (values != null) { List<string> ids = new List<string>(); foreach (object value in values) ids.Add(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)); return "ID:" + string.Join(",", ids.ToArray()); }
                if (raw != null) return "ID:" + Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { }
            throw new InvalidOperationException("Sketch point identity unavailable; index fallback is forbidden.");
        }
        public static bool SameComObject(object left, object right)
        {
            if (left == null || right == null) return false; IntPtr lp = IntPtr.Zero, rp = IntPtr.Zero;
            try { lp = Marshal.GetIUnknownForObject(left); rp = Marshal.GetIUnknownForObject(right); return lp == rp; }
            finally { if (lp != IntPtr.Zero) Marshal.Release(lp); if (rp != IntPtr.Zero) Marshal.Release(rp); }
        }
    }
}
