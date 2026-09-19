using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands.MirrorV7
{
    public static class UnconstrainedLineSketchReflectionV7
    {
        public static void ApplyToActiveSketch(ModelDoc2 workingDocument, Sketch expectedSketch, IList<SketchPointTargetV7> targets)
        {
            if (workingDocument == null) throw new ArgumentNullException("workingDocument"); if (targets == null || targets.Count == 0) throw new InvalidOperationException("No mutation targets.");
            SketchManager manager = workingDocument.SketchManager; if (manager == null || manager.ActiveSketch == null) throw new InvalidOperationException("Expected sketch edit mode.");
            if (!SingleSketchTargetBuilderV7.SameComObject(manager.ActiveSketch, expectedSketch)) throw new InvalidOperationException("Active sketch is not the selected sketch.");
            List<SketchPoint> points = new List<SketchPoint>();
            foreach (SketchPointTargetV7 target in targets)
            {
                int state; SketchPoint point = PersistentReferenceServiceV7.Resolve(workingDocument, target.Reference, out state) as SketchPoint; if (point == null) throw new InvalidOperationException("Cannot resolve point before mutation. state=" + state);
                if (point.GetSketch() == null || !SingleSketchTargetBuilderV7.SameComObject(point.GetSketch(), expectedSketch)) throw new InvalidOperationException("Resolved point ownership mismatch.");
                double[] current = { point.X, point.Y, point.Z }; if (SketchMutationMathV7.Distance(current, target.BeforeSketch) > SketchMutationMathV7.PositionToleranceMetres) throw new InvalidOperationException("Point changed before mutation."); points.Add(point);
            }
            for (int i = 0; i < points.Count; i++) { double[] target = targets[i].TargetSketch; SketchMutationMathV7.Validate(target); if (!points[i].SetCoords(target[0], target[1], target[2])) throw new InvalidOperationException("SetCoords rejected target point " + i + "."); }
        }
    }
}
