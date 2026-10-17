using System.Collections.Generic;

namespace ADDIN.Commands.BendPlacement
{
    internal enum SheetEnvelopeRequirement { SingleProvenUniformSection, MultipleSlicesOrNative3DRequired }
    // Future interface only. No fabricated slices or reinterpretation of an
    // unsupported 2D model as an empty/non-colliding workpiece.
    internal sealed class SheetEnvelopeReadiness
    {
        public SheetEnvelopeRequirement RequiredMethod { get; }
        public bool HasUsableEnvelope { get; }
        public IReadOnlyList<string> Reasons { get; }
        internal SheetEnvelopeReadiness(SheetSection2D section)
        {
            HasUsableEnvelope=section!=null&&section.Coverage!=SheetCoverage.Unsupported2DEnvelope;
            RequiredMethod=HasUsableEnvelope?SheetEnvelopeRequirement.SingleProvenUniformSection:SheetEnvelopeRequirement.MultipleSlicesOrNative3DRequired;
            Reasons=PlacementLists.Copy(HasUsableEnvelope?new string[0]:new[]{"UNSUPPORTED_2D_ENVELOPE","MULTIPLE_SLICES_OR_3D_EVIDENCE_REQUIRED"});
        }
    }
}
