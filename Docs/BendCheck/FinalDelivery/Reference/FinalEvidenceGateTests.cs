// Reference assertions for Codex to integrate with the real test runner.
// Not included in any .csproj. The tests MUST be wired into ToolProfiles.Tests
// and actually run on Debug + Release before claiming PASS.
using System;
using System.Linq;

namespace ADDIN.Commands.BendPlanning
{
    internal static class FinalEvidenceGateTests
    {
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("FINAL_GATE_TEST: " + message);
        }

        private static FinalEvidenceFacts AllReviewed()
        {
            return new FinalEvidenceFacts
            {
                ToolId = "AMD-STD-003",
                SourceCurrent = true,
                FixedCadPairMatched = true,
                PunchMaterialValid = true,
                HolderMaterialReviewed = true,
                SourceToWorkPoseReviewed = true,
                FiniteAxialSpanReviewed = true,
                RegisteredSheetStatesReviewed = true,
                ProcessMotionReviewed = true,
                SameHardwareAndSetupThroughAllBends = true,
                AllNativeBendsIncluded = true,
                CompleteContinuousPairPhaseCertificate = true,
                ValidCompleteClearCertificate = true,
                NativeShutdownClean = true
            };
        }

        internal static void Run()
        {
            var input = AllReviewed();
            var result = FinalEvidenceGate.Evaluate(input);
            Require(result.StaticNumericInputReady &&
                result.ProcessNumericInputReady &&
                result.CanShowCheckedScopeClear, "Reviewed complete checked scope");
            Require(result.ManufacturingStatus == "CHUA_XAC_MINH",
                "Numeric checked scope is not manufacturing approval");

            input = AllReviewed();
            input.SimulationOnly = true;
            result = FinalEvidenceGate.Evaluate(input);
            Require(result.CanShowCheckedScopeClear &&
                result.ScopeLabel == "SIMULATION_ONLY" &&
                result.ManufacturingStatus == "CHUA_XAC_MINH",
                "Even numeric CLEAR is simulation only");

            input = AllReviewed();
            input.SourceToWorkPoseReviewed = false;
            result = FinalEvidenceGate.Evaluate(input);
            Require(!result.StaticNumericInputReady &&
                !result.ProcessNumericInputReady &&
                !result.CanShowCheckedScopeClear &&
                result.CheckedStatus == "UNKNOWN", "Missing physical pose blocks numeric");

            input = AllReviewed();
            input.HolderMaterialReviewed = false;
            result = FinalEvidenceGate.Evaluate(input);
            Require(result.Reasons.Contains("HOLDER_MATERIAL_ROLES_UNREVIEWED") &&
                !result.CanShowCheckedScopeClear, "No auto-fill of Holder");

            input = AllReviewed();
            input.RegisteredSheetStatesReviewed = false;
            result = FinalEvidenceGate.Evaluate(input);
            Require(result.StaticNumericInputReady &&
                !result.ProcessNumericInputReady &&
                !result.CanShowCheckedScopeClear, "Static does not prove process");

            input = AllReviewed();
            input.ValidCompleteClearCertificate = false;
            input.ValidBoundWitnessForOneCandidate = true;
            result = FinalEvidenceGate.Evaluate(input);
            Require(result.CanShowCandidateWitnessedCollision &&
                !result.CanShowCheckedScopeClear &&
                result.CheckedStatus == "WITNESSED_CANDIDATE_COLLISION",
                "A witnessed candidate collision is not a Part-wide verdict");

            input = AllReviewed();
            input.InterruptedOrBudgetLimited = true;
            result = FinalEvidenceGate.Evaluate(input);
            Require(!result.CanShowCheckedScopeClear &&
                !result.CanShowCandidateWitnessedCollision &&
                result.CheckedStatus == "UNKNOWN",
                "Budget does not prove geometry");

            input = AllReviewed();
            input.SourceCurrent = false;
            result = FinalEvidenceGate.Evaluate(input);
            Require(!result.CanShowCheckedScopeClear &&
                result.CheckedStatus == "UNKNOWN", "Invalidate old CLEAR");

            input = AllReviewed();
            input.NativeShutdownClean = false;
            result = FinalEvidenceGate.Evaluate(input);
            Require(result.CanShowCheckedScopeClear &&
                !result.NativeReleaseGatePassed &&
                result.Reasons.Contains("NATIVE_RELEASE_LIFECYCLE_NOT_ACCEPTED"),
                "Native functional result independent of release lifecycle");

            input = AllReviewed();
            input.ColdNumericQueriesObserved = 0; // valid warm cache scenario
            result = FinalEvidenceGate.Evaluate(input);
            Require(result.CanShowCheckedScopeClear,
                "Warm reuse needs current numeric certificate, not a new query");
        }
    }
}
