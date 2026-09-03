using System;
using ADDIN.Commands;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Helpers
{
    public static class ExtrudeCutRecoveryHelper
    {
        public static bool TryRecoverExtrudeCutRebuild(
            ModelDoc2 partDoc,
            PostBaseFeatureInfo info,
            FeatureBodyState originalCache,
            out string details)
        {
            return BodyOperationsHelper.TrySuperRecoverExtrudeCut(partDoc, info, out details);
        }
    }
}
