// DESIGN REFERENCE ONLY; not compiled into product. No manufacturing approval.
// A pure, managed UI/report classifier to keep missing evidence separate from
// simulated or source-reviewed numeric results. C# 7.3 / .NET Framework 4.8.
// Integrate only after checking actual solver result guards/certificates.
using System;
using System.Collections.Generic;
using System.Linq;

namespace ADDIN.Commands.BendPlanning
{
    internal sealed class FinalEvidenceFacts
    {
        public string ToolId { get; set; }
        public bool SourceCurrent { get; set; }
        public bool FixedCadPairMatched { get; set; }
        public bool PunchMaterialValid { get; set; }
        public bool HolderMaterialReviewed { get; set; }
        public bool SourceToWorkPoseReviewed { get; set; }
        public bool FiniteAxialSpanReviewed { get; set; }
        public bool RegisteredSheetStatesReviewed { get; set; }
        public bool ProcessMotionReviewed { get; set; }
        public bool SameHardwareAndSetupThroughAllBends { get; set; }
        public bool AllNativeBendsIncluded { get; set; }
        public bool CompleteContinuousPairPhaseCertificate { get; set; }
        public bool ValidCompleteClearCertificate { get; set; }
        public bool ValidBoundWitnessForOneCandidate { get; set; }
        public bool InterruptedOrBudgetLimited { get; set; }
        public bool SimulationOnly { get; set; }
        public bool NativeShutdownClean { get; set; }
        public long ColdNumericQueriesObserved { get; set; }
    }

    internal sealed class FinalEvidenceVerdict
    {
        public string ToolId { get; private set; }
        public bool StaticNumericInputReady { get; private set; }
        public bool ProcessNumericInputReady { get; private set; }
        public bool CanShowCheckedScopeClear { get; private set; }
        public bool CanShowCandidateWitnessedCollision { get; private set; }
        public bool NativeReleaseGatePassed { get; private set; }
        public string ScopeLabel { get; private set; }
        public string CheckedStatus { get; private set; }
        // Never use this to issue company manufacturing sign-off.
        public string ManufacturingStatus { get { return "CHUA_XAC_MINH"; } }
        public IReadOnlyList<string> Reasons { get; private set; }

        internal FinalEvidenceVerdict(FinalEvidenceFacts f, bool statics, bool process,
            bool clear, bool collision, string label, string status, IEnumerable<string> reasons)
        {
            ToolId = f.ToolId;
            StaticNumericInputReady = statics;
            ProcessNumericInputReady = process;
            CanShowCheckedScopeClear = clear;
            CanShowCandidateWitnessedCollision = collision;
            NativeReleaseGatePassed = f.NativeShutdownClean;
            ScopeLabel = label;
            CheckedStatus = status;
            Reasons = Array.AsReadOnly(reasons.Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal).ToArray());
        }
    }

    internal static class FinalEvidenceGate
    {
        internal static FinalEvidenceVerdict Evaluate(FinalEvidenceFacts f)
        {
            if (f == null) throw new ArgumentNullException("f");
            var reasons = new List<string>();
            if (String.IsNullOrWhiteSpace(f.ToolId)) reasons.Add("MISSING_TOOL_ID");
            if (!f.SourceCurrent) reasons.Add("SOURCE_STALE_OR_MISSING");
            if (!f.FixedCadPairMatched) reasons.Add("FIXED_PUNCH_HOLDER_PAIR_MISSING");
            if (!f.PunchMaterialValid) reasons.Add("PUNCH_MATERIAL_OPEN_OR_UNSUPPORTED");
            if (!f.HolderMaterialReviewed) reasons.Add("HOLDER_MATERIAL_ROLES_UNREVIEWED");
            if (!f.SourceToWorkPoseReviewed) reasons.Add("SOURCE_TO_WORK_POSE_UNREVIEWED");
            if (!f.FiniteAxialSpanReviewed) reasons.Add("TOOL_AXIAL_EXTENT_UNVERIFIED");

            bool statics = reasons.Count == 0 && !f.InterruptedOrBudgetLimited;
            if (!f.RegisteredSheetStatesReviewed)
                reasons.Add("NO_REVIEWED_INTERMEDIATE_SHEET_STATES");
            if (!f.ProcessMotionReviewed)
                reasons.Add("UNKNOWN_MACHINE_MOTION");
            bool process = statics && f.RegisteredSheetStatesReviewed &&
                f.ProcessMotionReviewed && !f.InterruptedOrBudgetLimited;

            if (f.InterruptedOrBudgetLimited) reasons.Add("INTERRUPTED_OR_BOUNDED");
            if (!f.SameHardwareAndSetupThroughAllBends)
                reasons.Add("COMMON_TOOL_SETUP_NOT_PROVEN");
            if (!f.AllNativeBendsIncluded)
                reasons.Add("INCOMPLETE_NATIVE_BEND_COVERAGE");

            // A current, complete solver certificate may have been reused from
            // an already validated cache: cold query counts are reported, but
            // zero new warm queries must not make a valid warm certificate false.
            bool clear = process && f.AllNativeBendsIncluded &&
                f.SameHardwareAndSetupThroughAllBends &&
                f.CompleteContinuousPairPhaseCertificate &&
                f.ValidCompleteClearCertificate &&
                !f.ValidBoundWitnessForOneCandidate;
            // A witnessed collision is only a claim about the specific checked
            // candidate; never about every tool or every possible sequence.
            bool collision = process && f.ValidBoundWitnessForOneCandidate;
            string status = collision ? "WITNESSED_CANDIDATE_COLLISION" :
                clear ? "COMPLETE_CLEAR_IN_CHECKED_SCOPE" : "UNKNOWN";
            if (!collision && !clear && process)
                reasons.Add("INCOMPLETE_OR_UNKNOWN_NUMERIC_CERTIFICATE");
            if (!f.NativeShutdownClean) reasons.Add("NATIVE_RELEASE_LIFECYCLE_NOT_ACCEPTED");

            return new FinalEvidenceVerdict(f, statics, process, clear, collision,
                f.SimulationOnly ? "SIMULATION_ONLY" : "REVIEWED_INPUT_SCOPE",
                status, reasons);
        }
    }
}
