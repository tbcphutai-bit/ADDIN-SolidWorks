using System;

// Only the managed row/context contract needed by the production draft adapter.
// Linking the actual draft source keeps these tests free of SW PIAs, COM and CAD.
// There is deliberately no Feature RCW member to accidentally call from a test.
namespace ADDIN.Commands
{
    internal sealed class VCutPropertySetupCommand
    {
        internal sealed class BendRow
        {
            internal int FeatureId;
            internal string FeatureName, PersistentId, Kind;
            internal double? BendAngleDeg, BendRadiusMm, ModelThicknessMm;
            internal BendMinimumContext MinimumContext;
        }
    }
    internal sealed class BendMinimumContext
    {
        internal bool Current = true;
        internal Func<VCutPropertySetupCommand.BendRow, BendMinimumRowResult> Read;
        internal BendMinimumRowResult Evaluate(VCutPropertySetupCommand.BendRow row) { return Read(row); }
    }
    internal enum BendMinimumStatus { MeetsMinimum, BelowMinimum, InsufficientData, GroovedAccepted }
    internal sealed class BendMinimumRowResult { internal BendMinimumStatus Status; }
}
