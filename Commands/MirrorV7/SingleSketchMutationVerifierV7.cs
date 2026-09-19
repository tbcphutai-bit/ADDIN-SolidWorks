using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands.MirrorV7
{
    public static class SingleSketchMutationVerifierV7
    {
        public static double VerifyPoints(ModelDoc2 workingDocument, Sketch currentSketch, IMathUtility math, IList<SketchPointTargetV7> targets)
        {
            MathTransform modelToSketch = currentSketch.ModelToSketchTransform as MathTransform; MathTransform sketchToModel = modelToSketch == null ? null : modelToSketch.IInverse(); if (sketchToModel == null) throw new InvalidOperationException("Post-edit transform unavailable."); double maximumError = 0;
            foreach (SketchPointTargetV7 target in targets)
            {
                int state; SketchPoint point = PersistentReferenceServiceV7.Resolve(workingDocument, target.Reference, out state) as SketchPoint; if (point == null) throw new InvalidOperationException("Point reference lost after mutation.");
                double[] actualModel = SketchMutationMathV7.Transform(math, sketchToModel, new[] { point.X, point.Y, point.Z }); double error = SketchMutationMathV7.Distance(actualModel, target.TargetModel); maximumError = Math.Max(maximumError, error);
                MirrorV7Diagnostics.Log("[PHASE6C1][POINT_VERIFY] beforeModel=" + Format(target.BeforeModel) + " expectedModel=" + Format(target.TargetModel) + " actualModel=" + Format(actualModel) + " errorSI=" + error.ToString("G17", CultureInfo.InvariantCulture) + " state=" + state);
                if (error > SketchMutationMathV7.PositionToleranceMetres) throw new InvalidOperationException("Post-solver point does not match reflected target.");
            }
            return maximumError;
        }
        private static string Format(double[] p) { return p == null ? "<null>" : "(" + p[0].ToString("G17", CultureInfo.InvariantCulture) + "," + p[1].ToString("G17", CultureInfo.InvariantCulture) + "," + p[2].ToString("G17", CultureInfo.InvariantCulture) + ")"; }
    }
}
