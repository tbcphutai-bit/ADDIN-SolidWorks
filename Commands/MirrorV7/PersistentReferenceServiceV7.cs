using System;
using SolidWorks.Interop.sldworks;
namespace ADDIN.Commands.MirrorV7
{
    public static class PersistentReferenceServiceV7
    {
        public static PersistReferenceV7 Capture(ModelDoc2 model, object target, string ownerFeatureName, string purpose)
        {
            if (model == null) throw new ArgumentNullException("model");
            if (target == null) return null;
            ModelDocExtension extension = model.Extension;
            if (extension == null) throw new InvalidOperationException("ModelDocExtension is unavailable.");
            try
            {
                object raw = extension.GetPersistReference3(target);
                byte[] data = raw as byte[];
                return data == null || data.Length == 0 ? null : new PersistReferenceV7(ownerFeatureName, purpose, data);
            }
            catch (Exception ex)
            {
                MirrorV7Diagnostics.Log("[PHASE4][PERSIST_CAPTURE_WARNING] " + (ownerFeatureName ?? "") + " purpose=" + (purpose ?? "") + " :: " + ex.Message);
                return null;
            }
        }
        public static object Resolve(ModelDoc2 model, PersistReferenceV7 reference, out int resolveState)
        {
            if (model == null) throw new ArgumentNullException("model");
            if (reference == null) throw new ArgumentNullException("reference");
            ModelDocExtension extension = model.Extension;
            if (extension == null) throw new InvalidOperationException("ModelDocExtension is unavailable.");
            resolveState = 0;
            return extension.GetObjectByPersistReference3(reference.Data, out resolveState);
        }
    }
}
