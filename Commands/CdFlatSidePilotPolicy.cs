// PR #62 / CHECK DRAWING ONLY
// Deterministic, COM-free flat-pattern side decision policy integrated from
// docs/anti/PR62_FlatSidePilotPolicy.cs.txt (base commit cfdfa94).
// Result polarity: true = pink toward viewer = ウラ; false = colored side = 表.

using System;

namespace ADDIN.Commands
{
    internal enum CdFlatSideChoice
    {
        UseWorkingCopy,
        UseDirectRead,
        Unknown
    }

    internal sealed class CdFlatSideProof
    {
        // Established independently from structured COM/view provenance,
        // NOT parsed from localized messages.
        public bool DrawingPathVerified { get; set; }
        public bool ReferencedPartPathVerified { get; set; }
        public bool ConfigurationVerified { get; set; }
        public bool DisplayStateVerified { get; set; }
        public bool FlatPatternBodyVerified { get; set; }
        public bool FaceColorCompleteAndUnambiguous { get; set; }
        public bool SourceFreshnessVerified { get; set; }

        // Optional structured audit metadata for logging/reporting.
        public string BodyResolutionMethod { get; set; }
        public string AppearanceSources { get; set; }
        public string FreshnessReason { get; set; }
        public string ProvenanceSummary { get; set; }

        // Positive obligation across every link in the chain.
        public bool IsComplete
        {
            get
            {
                return DrawingPathVerified &&
                       ReferencedPartPathVerified &&
                       ConfigurationVerified &&
                       DisplayStateVerified &&
                       FlatPatternBodyVerified &&
                       FaceColorCompleteAndUnambiguous &&
                       SourceFreshnessVerified;
            }
        }
    }

    internal sealed class CdFlatSidePolicyInput
    {
        // OFF by default. Never infer true from the fact that the direct probe ran.
        public bool PilotEnabled { get; set; }

        public bool? DirectPinkFacingViewer { get; set; }
        public bool? WorkingCopyPinkFacingViewer { get; set; }
        public double WorkingCopyFrontPinkMm2 { get; set; }
        public double WorkingCopyBackPinkMm2 { get; set; }

        // Typed/structured classifier result from the CHECK DRAWING call boundary.
        public bool WorkingCopyBlockedSpecificallyByDirtyAssembly { get; set; }
        public CdFlatSideProof DirectProof { get; set; }
    }

    internal sealed class CdFlatSidePolicyDecision
    {
        public CdFlatSideChoice Choice { get; private set; }
        public bool? PinkFacingViewer { get; private set; }
        public string Code { get; private set; }
        public string Explanation { get; private set; }

        public static CdFlatSidePolicyDecision Of(
            CdFlatSideChoice choice, bool? pink, string code, string explanation)
        {
            return new CdFlatSidePolicyDecision
            {
                Choice = choice,
                PinkFacingViewer = pink,
                Code = code,
                Explanation = explanation
            };
        }
    }

    internal static class CdFlatSidePilotPolicy
    {
        private const double AreaEpsilonMm2 = 0.000001;

        public static CdFlatSidePolicyDecision Evaluate(CdFlatSidePolicyInput input)
        {
            if (input == null)
                return CdFlatSidePolicyDecision.Of(CdFlatSideChoice.Unknown, null,
                    "INPUT_MISSING", "Cannot evaluate missing inputs.");

            bool? direct = input.DirectPinkFacingViewer;
            bool? legacy = input.WorkingCopyPinkFacingViewer;

            // SHADOW contract: do not modify current CHECK DRAWING statuses.
            if (!input.PilotEnabled)
                return CdFlatSidePolicyDecision.Of(CdFlatSideChoice.UseWorkingCopy,
                    legacy, "SHADOW_LEGACY", "Pilot is OFF; retain Working Copy result.");

            // Standalone Working Copy's ClassifyFlatPinkSide returns true when
            // BOTH front and back are pink (because it returns 'front').
            // That is ambiguous, not an independent proof of ウラ.
            bool bothPink = input.WorkingCopyFrontPinkMm2 > AreaEpsilonMm2 &&
                            input.WorkingCopyBackPinkMm2 > AreaEpsilonMm2;
            if (bothPink)
                return CdFlatSidePolicyDecision.Of(CdFlatSideChoice.Unknown, null,
                    "WC_BOTH_SIDES_PINK_AMBIGUOUS",
                    "Working Copy painted both sides; cannot certify polarity.");

            // Both readers can decide and disagree -> never arbitrarily choose.
            if (direct.HasValue && legacy.HasValue && direct.Value != legacy.Value)
                return CdFlatSidePolicyDecision.Of(CdFlatSideChoice.Unknown, null,
                    "DIRECT_WORKCOPY_DISAGREE",
                    "Direct Read disagrees with a determinate Working Copy.");

            // The proven legacy checker remains authoritative when determinate
            // and unambiguous. This also covers agreement with Direct Read.
            if (legacy.HasValue)
                return CdFlatSidePolicyDecision.Of(CdFlatSideChoice.UseWorkingCopy,
                    legacy, "LEGACY_CONFIRMED",
                    "Working Copy verified the view side.");

            // The only proposed Direct promotion: a tightly-scoped PILOT in which
            // legacy cannot run SPECIFICALLY because the reference Assembly is dirty.
            // IMPORTANT: SourceFreshnessVerified requires real independent proof;
            // do not automatically set it true for merely in-memory face readings.
            if (direct.HasValue &&
                input.WorkingCopyBlockedSpecificallyByDirtyAssembly &&
                input.DirectProof != null &&
                input.DirectProof.IsComplete)
                return CdFlatSidePolicyDecision.Of(CdFlatSideChoice.UseDirectRead,
                    direct, "PILOT_DIRTY_ASSEMBLY_DIRECT_VERIFIED",
                    "Dirty Assembly blocked Working Copy; independently verified Direct View.");

            // Any other missing/ambiguous path retains the real legacy result
            // (usually Warning), without pretending that a cached view is fresh.
            return CdFlatSidePolicyDecision.Of(CdFlatSideChoice.UseWorkingCopy,
                legacy, "DIRECT_PROMOTION_BLOCKED",
                "Insufficient independent evidence to use Direct Read.");
        }

        // Synthetic unit tests for the policy ONLY. They are NOT a substitute for
        // SOLIDWORKS 2024 SP5 live COM/view/face tests.
        public static int RunSelfTests()
        {
            int count = 0;
            Func<CdFlatSideProof> makeVerified = () => new CdFlatSideProof
            {
                DrawingPathVerified = true,
                ReferencedPartPathVerified = true,
                ConfigurationVerified = true,
                DisplayStateVerified = true,
                FlatPatternBodyVerified = true,
                FaceColorCompleteAndUnambiguous = true,
                SourceFreshnessVerified = true
            };
            Action<bool, string> require = (condition, name) =>
            {
                if (!condition) throw new InvalidOperationException("PR62 test failed: " + name);
                count++;
            };

            // 1. Shadow OFF never promotes even with verified proof and dirty assembly
            var p = new CdFlatSidePolicyInput
            {
                PilotEnabled = false,
                DirectPinkFacingViewer = false,
                WorkingCopyPinkFacingViewer = null,
                WorkingCopyBlockedSpecificallyByDirtyAssembly = true,
                DirectProof = makeVerified()
            };
            CdFlatSidePolicyDecision d = Evaluate(p);
            require(d.Choice == CdFlatSideChoice.UseWorkingCopy && d.Code == "SHADOW_LEGACY",
                "shadow never promotes");

            // 2. Controlled dirty-assembly pilot with complete proof promotes Direct Read
            p.PilotEnabled = true;
            d = Evaluate(p);
            require(d.Choice == CdFlatSideChoice.UseDirectRead &&
                d.Code == "PILOT_DIRTY_ASSEMBLY_DIRECT_VERIFIED" &&
                d.PinkFacingViewer == false,
                "controlled dirty-asm pilot");

            // 3. Stale source blocks promotion even when Pilot ON
            p.DirectProof.SourceFreshnessVerified = false;
            d = Evaluate(p);
            require(d.Choice == CdFlatSideChoice.UseWorkingCopy && d.Code == "DIRECT_PROMOTION_BLOCKED",
                "stale source blocks");

            // 4. Unrelated Working Copy failure blocks promotion
            p.DirectProof = makeVerified();
            p.WorkingCopyBlockedSpecificallyByDirtyAssembly = false;
            d = Evaluate(p);
            require(d.Choice == CdFlatSideChoice.UseWorkingCopy && d.Code == "DIRECT_PROMOTION_BLOCKED",
                "non-dirty failure blocks");

            // 5. Clean agreement uses legacy
            p.WorkingCopyPinkFacingViewer = false;
            d = Evaluate(p);
            require(d.Choice == CdFlatSideChoice.UseWorkingCopy && d.Code == "LEGACY_CONFIRMED" &&
                d.PinkFacingViewer == false,
                "agreement legacy");

            // 6. Determinate disagreement fails closed to Unknown
            p.WorkingCopyPinkFacingViewer = true;
            d = Evaluate(p);
            require(d.Choice == CdFlatSideChoice.Unknown && d.Code == "DIRECT_WORKCOPY_DISAGREE" &&
                !d.PinkFacingViewer.HasValue,
                "conflicting polarities fail closed");

            // 7. Working Copy both sides pink is classified ambiguous (Unknown)
            p.WorkingCopyFrontPinkMm2 = 42109.7;
            p.WorkingCopyBackPinkMm2 = 42239.0;
            d = Evaluate(p);
            require(d.Choice == CdFlatSideChoice.Unknown &&
                d.Code == "WC_BOTH_SIDES_PINK_AMBIGUOUS" &&
                !d.PinkFacingViewer.HasValue,
                "both sides pink");

            // 8. Two unknown readers (e.g. no paint) remain unknown
            p.WorkingCopyFrontPinkMm2 = 0;
            p.WorkingCopyBackPinkMm2 = 0;
            p.WorkingCopyPinkFacingViewer = null;
            p.DirectPinkFacingViewer = null;
            d = Evaluate(p);
            require(d.Choice == CdFlatSideChoice.UseWorkingCopy &&
                !d.PinkFacingViewer.HasValue && d.Code == "DIRECT_PROMOTION_BLOCKED",
                "two unknown readers remain unknown");

            // 9. Part dirty blocks promotion (neither dirty-asm only nor fresh)
            p.DirectPinkFacingViewer = false;
            p.WorkingCopyBlockedSpecificallyByDirtyAssembly = false;
            p.DirectProof = makeVerified();
            p.DirectProof.SourceFreshnessVerified = false;
            d = Evaluate(p);
            require(d.Choice == CdFlatSideChoice.UseWorkingCopy && d.Code == "DIRECT_PROMOTION_BLOCKED",
                "part dirty blocks promotion");

            // 10. Drawing dirty / unverified path blocks promotion
            p.WorkingCopyBlockedSpecificallyByDirtyAssembly = true;
            p.DirectProof = makeVerified();
            p.DirectProof.DrawingPathVerified = false;
            p.DirectProof.SourceFreshnessVerified = false;
            d = Evaluate(p);
            require(d.Choice == CdFlatSideChoice.UseWorkingCopy && d.Code == "DIRECT_PROMOTION_BLOCKED",
                "drawing dirty/unverified blocks promotion");

            // 11. Unverified or multi-body flat pattern body blocks promotion
            p.DirectProof = makeVerified();
            p.DirectProof.FlatPatternBodyVerified = false;
            d = Evaluate(p);
            require(d.Choice == CdFlatSideChoice.UseWorkingCopy && d.Code == "DIRECT_PROMOTION_BLOCKED",
                "unverified/multi-body blocks promotion");

            // 12. Incomplete/ambiguous face colors block promotion
            p.DirectProof = makeVerified();
            p.DirectProof.FaceColorCompleteAndUnambiguous = false;
            d = Evaluate(p);
            require(d.Choice == CdFlatSideChoice.UseWorkingCopy && d.Code == "DIRECT_PROMOTION_BLOCKED",
                "incomplete face color blocks promotion");

            // 13. Both sides pink check even when Working Copy was null/false & Direct disagreed
            p = new CdFlatSidePolicyInput
            {
                PilotEnabled = true,
                DirectPinkFacingViewer = false,
                WorkingCopyPinkFacingViewer = true,
                WorkingCopyFrontPinkMm2 = 51480.4,
                WorkingCopyBackPinkMm2 = 51480.4,
                WorkingCopyBlockedSpecificallyByDirtyAssembly = false,
                DirectProof = makeVerified()
            };
            d = Evaluate(p);
            require(d.Choice == CdFlatSideChoice.Unknown &&
                d.Code == "WC_BOTH_SIDES_PINK_AMBIGUOUS",
                "1D both-sides-pink guarded even before disagree");

            return count;
        }
    }
}
